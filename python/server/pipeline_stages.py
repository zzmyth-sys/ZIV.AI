"""Pipeline stage functions (S4-A: structure-only split of ``pipeline._run_once``).

The pipeline's natural stage boundaries (encode / sample / decode) are exactly the seam
positions; S4-B wires the ``seams.apply`` anchors at each boundary. This module is
CPU-importable: heavy imports (``comfy`` / ``torch``) stay inside the functions, and the
pipeline primitives (``encode_prompt`` / ``vae_decode`` / ``to_pil`` / ``sample``) are
imported lazily from :mod:`pipeline` at call time so the existing
``mock.patch.object(pipeline, ...)`` targets keep resolving.

Each stage takes one ctx dict and returns a ctx dict (``stage(ctx, ...) -> ctx``); the
orchestrator in :mod:`pipeline` threads a single ctx through the three stages.
"""

import logging

import config
import mem_guard
import pipeline_io
import plugin_sampling
import preview as preview_module
import seams
import vram_probe

_LOG = logging.getLogger("zivai.server")


def _lift_cleanups(main_ctx, result):
    """Move plugin cleanups registered at an anchor into the orchestrator's ctx list.

    The per-anchor ctx excludes ``CLEANUPS_KEY``, so ``dispatch.call_chain`` mints a fresh
    list for that anchor; extending (not aliasing) the orchestrator's shared list keeps the
    two separate. Because stage ``ctx = dict(ctx)`` is a shallow copy, the shared list
    survives a stage raise, so the orchestrator's ``finally`` still sees every cleanup.
    """
    bucket = result.get(seams.CLEANUPS_KEY)
    if bucket:
        main_ctx.setdefault(seams.CLEANUPS_KEY, []).extend(bucket)


def _stage_encode(ctx):
    """Encode prompt + reference(s): before_encode -> encode_prompt -> after_encode."""
    from pipeline import encode_prompt  # lazy: keeps mock.patch.object(pipeline, ...) valid

    ctx = dict(ctx)

    encode_ctx = seams.apply("before_encode", {
        "prompt": ctx["prompt"],
        "image_path": ctx["image_path"],
        "mask_path": ctx["mask_path"],
        "additional_images": ctx["additional_images"],
        "spec": ctx["spec"],
    })
    _lift_cleanups(ctx, encode_ctx)
    positive, negative, latent, mask = encode_prompt(
        ctx["clip"], ctx["vae"], encode_ctx["prompt"], encode_ctx["image_path"],
        encode_ctx["mask_path"], spec=encode_ctx["spec"], mask_binary=ctx["mask_binary"],
        additional_images=encode_ctx["additional_images"], need_negative=ctx["need_negative"],
    )
    ctx["positive"] = positive
    ctx["negative"] = negative
    ctx["latent"] = latent
    ctx["mask"] = mask

    encode_ctx = seams.apply("after_encode", {
        "positive": ctx["positive"],
        "negative": ctx["negative"],
        "latent": ctx["latent"],
        "mask": ctx["mask"],
    })
    _lift_cleanups(ctx, encode_ctx)
    ctx["positive"] = encode_ctx["positive"]
    ctx["negative"] = encode_ctx["negative"]
    ctx["latent"] = encode_ctx["latent"]
    ctx["mask"] = encode_ctx["mask"]
    return ctx


def _stage_sample(ctx, *, on_progress, on_preview, poll_cancel):
    """Run one sampler pass (plugin before_sample seam + AuraFlow/sigmas) -> samples."""
    import comfy.model_management as mm
    import comfy.sample

    from pipeline import sample as legacy_sample  # lazy: keeps the pipeline binding current

    ctx = dict(ctx)

    sample_ctx = seams.apply("before_sample", plugin_sampling.build_context(
        op=ctx["op"], model=ctx["model"], clip=ctx["clip"], vae=ctx["vae"],
        latent=ctx["latent"], mask=ctx["mask"], prompt=ctx["prompt"],
        image_path=ctx["image_path"], mask_path=ctx["mask_path"], steps=ctx["steps"],
        denoise=ctx["denoise"], seed=ctx["seed"], cfg=ctx["cfg"],
        sampler_preset=ctx["sampler_preset"], model_id=ctx["model_id"],
    ))
    _lift_cleanups(ctx, sample_ctx)
    model = sample_ctx["model"]
    steps = plugin_sampling.report_steps(sample_ctx, ctx["steps"])

    previewer = preview_module.get_previewer(model)
    preview_every = max(1, int(config.PREVIEW_EVERY))

    # First inference triggers ComfyUI's lazy `load_models_gpu()`; announce the
    # sampling stage before sampling so that cost shows up client-side.
    pipeline_io._emit(on_progress, 0, steps, 0.0, "sampling", "moving_to_gpu")
    mem_guard.enforce()  # pre-flight: don't start the (weight-staging) first step already over budget

    def callback(step, x0, x, total_steps):
        if poll_cancel is not None:
            poll_cancel()
        mm.throw_exception_if_processing_interrupted()
        # host-RAM guard: bail out at the top of each step, before DynamicVRAM thrashes shared GPU
        # memory / host RAM to the point of freezing the machine (mem_guard, no-op off Windows).
        mem_guard.enforce()
        total = total_steps or steps
        pipeline_io._emit(
            on_progress, step + 1, total, min(1.0, (step + 1) / float(total)),
            "sampling", "sampling",
        )
        if on_preview is not None and previewer is not None and step % preview_every == 0:
            # B10: a preview is an optional observation. A failure while encoding
            # it (latent decode) or shipping it (callback / pipe write) must not
            # fail the whole task, so only this block is guarded. Everything above
            # (cancel poll / interrupt check / mem_guard) stays outside: a real
            # cancellation raises InterruptProcessingException, a BaseException
            # that `except Exception` deliberately does not catch.
            try:
                jpeg = preview_module.encode_jpeg(previewer, x0)
                if jpeg:
                    on_preview(step, total, jpeg)
            except Exception as exc:  # noqa: BLE001 - preview must never fail the task
                _LOG.warning(
                    "preview failed at step %s (%s: %s); continuing",
                    step, type(exc).__name__, exc,
                )

    noise = comfy.sample.prepare_noise(ctx["latent"], ctx["seed"])
    vram_probe.stage("sample BEG (first call loads weights)")

    # Before_sample cleanups are lifted into ctx (above) and run by the orchestrator's
    # finally, so a plugin side-branch is torn down even on OOM / sampler failure.
    samples = plugin_sampling.sample_from(
        sample_ctx, noise=noise, positive=ctx["positive"], negative=ctx["negative"],
        latent=ctx["latent"], mask=ctx["mask"], seed=ctx["seed"], callback=callback,
        steps=steps, denoise=ctx["denoise"], legacy_sample=legacy_sample,
    )
    vram_probe.stage("sample END")

    after_ctx = seams.apply("after_sample", {"samples": samples})
    _lift_cleanups(ctx, after_ctx)
    samples = after_ctx["samples"]

    # A cancel that lands after the last sampling step still aborts here; the
    # VAE decode itself is not interruptible (it is short, see contract §3.3).
    if poll_cancel is not None:
        poll_cancel()
    mm.throw_exception_if_processing_interrupted()
    pipeline_io._emit(on_progress, steps, steps, 1.0, "vae_decode", "vae_decode")

    ctx["model"] = model
    ctx["steps"] = steps
    ctx["samples"] = samples
    return ctx


def _stage_decode(ctx):
    """Decode latents: before_decode -> vae_decode/to_pil -> after_decode."""
    from pipeline import to_pil, vae_decode  # lazy: keeps mock.patch.object(pipeline, ...) valid

    ctx = dict(ctx)

    decode_ctx = seams.apply("before_decode", {"vae": ctx["vae"], "samples": ctx["samples"]})
    _lift_cleanups(ctx, decode_ctx)
    decoded = vae_decode(decode_ctx["vae"], decode_ctx["samples"])
    vram_probe.stage("vae_decode END")
    image, height, width = to_pil(decoded[0])
    ctx["image"] = image
    ctx["height"] = height
    ctx["width"] = width

    decode_ctx = seams.apply("after_decode", {
        "image": ctx["image"],
        "width": ctx["width"],
        "height": ctx["height"],
    })
    _lift_cleanups(ctx, decode_ctx)
    ctx["image"] = decode_ctx["image"]
    ctx["width"] = decode_ctx["width"]
    ctx["height"] = decode_ctx["height"]
    return ctx
