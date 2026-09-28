"""Inference pipeline for the ZIV.AI backend.

Qwen-Image-2.1 text encoding -> ModelSamplingAuraFlow(shift=3.1) ->
``comfy.sample.sample`` (euler / simple / cfg=1.0) -> VAE decode -> PNG.
An optional mask (Z19, revised) is applied as the sampler noise mask and
reference latents inject the source image for image editing. User / C# masks
are read as grayscale (0-255) by the handlers; the backend-generated outpaint
mask is soft too. Output is always a new file (Z24); the source is never
written.

OOM fallback (Step 4): the request is retried at MAX_RESOLUTION, then down the
RESOLUTION_FALLBACK list, freeing caches between attempts.

Heavy imports (torch / comfy) stay inside the functions so a cold ``ping``
never touches the heavy stack.
"""

import gc
import importlib.util
import logging
import math
import os
import sys
import time

import config
import model_loader
import models
import multi_image
import outpaint
import pipeline_hooks
import pipeline_io
import plugin_sampling
import preview as preview_module
import vram_probe
from pipeline_io import (
    _emit,
    _load_image_tensor,
    _load_mask_tensor,
    _mask_is_binary,
    _remove_tree,
    _resize_mask,
    _resolve_denoise,
    _resolve_output_path,
    _resolve_seed,
    _to_pil,
    save_png,
    to_pil,
    vae_decode,
)
from resolution import (
    _normalize_payload_resolution,
    _resolution_specs,
    _size_for_no_source,
    _spec_label,
    _target_size_from_spec,
)

_LOG = logging.getLogger("zivai.server")

# TE-Speed node class cache (loaded at most once per process); None = not loaded yet.
_TE_SPEED_CLS = None


def _load_te_speed():
    """Load the external TE-Speed custom node as a package (import-by-file).

    The node's ``nodes.pyd`` is a Cython module named ``nodes``; importing it
    top-level would collide with ComfyUI's ``nodes.py``, so it is loaded under a
    unique package name via its ``__init__.py`` (whose own relative import pulls
    ``nodes.pyd``). Returns the node class, or ``None`` when it is not deployed.
    """
    global _TE_SPEED_CLS
    if _TE_SPEED_CLS is not None:
        return _TE_SPEED_CLS
    init_py = os.path.join(config.TE_SPEED_NODE_DIR, "__init__.py")
    if not os.path.isfile(init_py):
        _LOG.warning("TE-Speed node not found at %s", config.TE_SPEED_NODE_DIR)
        return None
    name = "te_speed_qwen_image21"
    spec = importlib.util.spec_from_file_location(
        name, init_py, submodule_search_locations=[config.TE_SPEED_NODE_DIR]
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    _TE_SPEED_CLS = module.NODE_CLASS_MAPPINGS["TESpeedQwenImage21"]
    return _TE_SPEED_CLS


def apply_te_speed(model):
    """Patch the loaded MODEL with the TE-Speed predictor (default off; never fatal).

    Mirrors ``pipeline_hooks.make_lora_hook``: when disabled, the node is missing,
    or the patch fails, the model is returned unchanged so acceleration can never
    fail an otherwise valid task. Returns the (possibly new) model.
    """
    if not config.TE_SPEED_ENABLED:
        return model
    try:
        node_cls = _load_te_speed()
        if node_cls is None:
            return model
        model, status = node_cls().patch(
            model,
            attention=config.TE_SPEED_ATTENTION,
            step_cache=config.TE_SPEED_MODE,
            reuse_threshold=config.TE_SPEED_REUSE_THRESHOLD,
            predictor_error_limit=config.TE_SPEED_ERROR_LIMIT,
            verbose=config.TE_SPEED_VERBOSE,
        )
        _LOG.info("TE-Speed applied: %s", status)
    except Exception as exc:  # pragma: no cover - exercised on the GPU host
        _LOG.warning("TE-Speed apply failed (%s); model left unpatched", exc)
    return model


def run(model, clip, vae, request, on_progress=None, on_preview=None, poll_cancel=None,
        mask_binary=True, op="inpaint"):
    """Run one inpaint / edit request.

    ``on_progress(step, total, fraction, stage, message)`` and
    ``on_preview(step, total, jpeg_bytes)`` are optional callbacks invoked from
    the sampler callback (same thread). ``poll_cancel`` is drained once per step
    so a `cancel` frame can raise the interrupt flag mid-sampling (Step 2.4).

    ``mask_binary`` thresholds a mask to 0 / 1 when ``True``. It defaults to
    ``True`` for the legacy contract, but the handlers now pass ``False`` for
    user / C# masks (revised Z19: user-explicit feather is exported as
    grayscale and must reach the sampler). The outpaint path also passes
    ``False`` so its backend-generated feathered mask stays soft.

    ``op`` is the IPC op (``inpaint`` / ``t2i`` / ``outpaint``): it is threaded
    into the batch-3 capability context so a plugin can decide whether it applies
    (the generic ``sampling_plan`` accelerator is edit-only).

    Retries at lower resolutions on CUDA OOM (Step 4).
    """
    model_loader.prepare_environment()

    import torch

    started = time.time()
    steps = max(1, int(request.get("steps") or config.DEFAULT_STEPS))
    seed = _resolve_seed(request.get("seed"))
    prompt = request.get("prompt") or ""
    image_path = request.get("image_path")
    mask_path = request.get("mask_path")
    # Step 9C.5-D: optional ordered reference images after the main image.
    additional_images = request.get("additional_images")
    denoise = _resolve_denoise(request.get("denoise"))
    output_path = _resolve_output_path(request.get("output_path"), image_path)

    # Stage 1: Python-internal transforms (LoRA / MagCache seam) right after the
    # model triple is loaded and BEFORE text encoding, so a LoRA that patches
    # `clip` (strength_clip) takes effect on the conditioning.
    model, clip = pipeline_hooks.apply_pre_sampling_hooks(model, clip, request)

    # Optional TE-Speed accelerator (default off; see config.TE_SPEED_ENABLED).
    model = apply_te_speed(model)

    # Step 6.5: an optional payload `resolution` overrides the config default;
    # absent -> the config default path (backward compatible with ipc 0.5).
    specs = _resolution_specs(request, image_path)
    # Step 8-2: the sampler preset comes from the model registry (env > models.json > default).
    sampler = models.resolve_sampler(request.get("model_id"))
    oom_types = _oom_types()
    last_error = None
    for index, spec in enumerate(specs):
        try:
            if config.FORCE_OOM and index == 0:
                raise torch.cuda.OutOfMemoryError("forced OOM (ZIV_AI_FORCE_OOM)")
            return _run_once(
                model, clip, vae, prompt, image_path, mask_path, output_path,
                spec, steps, seed, denoise, started,
                on_progress, on_preview, poll_cancel, mask_binary,
                additional_images=additional_images, sampler=sampler,
                op=op, model_id=request.get("model_id"),
            )
        except oom_types as exc:
            last_error = exc
            _LOG.warning(
                "OOM at resolution %s (attempt %d/%d); falling back: %s",
                _spec_label(spec), index + 1, len(specs), exc,
            )
            _emit(on_progress, 0, 0, 0.0, "sampling", "oom_fallback:%s" % _spec_label(spec))
            _free_vram()

    if last_error is not None:
        raise last_error
    raise RuntimeError("no resolution candidate produced output")


def run_outpaint(model, clip, vae, request, on_progress=None, on_preview=None, poll_cancel=None):
    """Run one outpaint request (Step 7 Phase 2).

    Builds a canvas + mask from the source at an anchored position, then derives
    an explicit-resolution request so the existing inpaint path does the work.
    """
    import tempfile

    image_path = request.get("image_path")
    if not image_path:
        raise ValueError("outpaint requires image_path")

    spec = _normalize_payload_resolution(request.get("resolution"), image_path)
    if not spec or spec.get("mode") != "explicit":
        raise ValueError("outpaint requires an explicit resolution (width/height)")

    target_w = outpaint.snap16(spec.get("width"))
    target_h = outpaint.snap16(spec.get("height"))
    anchor = outpaint.normalize_anchor(request.get("anchor"))

    workdir = tempfile.mkdtemp(prefix="zivai_outpaint_")
    try:
        canvas_path, mask_path = outpaint.build_outpaint(
            image_path, target_w, target_h, anchor, workdir
        )
        derived = dict(request)
        derived["image_path"] = canvas_path
        derived["mask_path"] = mask_path
        derived["resolution"] = {
            "mode": "explicit",
            "width": target_w,
            "height": target_h,
            "max_pixels": spec.get("max_pixels"),
        }
        return run(
            model, clip, vae, derived,
            on_progress=on_progress, on_preview=on_preview, poll_cancel=poll_cancel,
            mask_binary=False, op="outpaint",
        )
    finally:
        _remove_tree(workdir)


def _run_once(model, clip, vae, prompt, image_path, mask_path, output_path,
              spec, steps, seed, denoise, started,
              on_progress, on_preview, poll_cancel, mask_binary=True,
              additional_images=None, sampler=None, op="inpaint", model_id=None):
    import comfy.model_management as mm
    import comfy.sample
    from comfy_extras.nodes_model_advanced import ModelSamplingAuraFlow

    sampler = sampler or {}

    # Stage 2: encode the prompt + optional reference image (clip is patched).
    # cfg==1.0 discards the negative conditioning in the sampler
    # (comfy/samplers.py:610 `math.isclose(cond_scale, 1.0) -> uncond_=None`), so skip
    # its encode entirely (optimization §10.2.2). Mirror the sampler's exact predicate
    # (math.isclose, rel_tol=1e-9) so we never skip when the sampler would still use it.
    cfg = float(sampler.get("cfg", 1.0))
    need_negative = not (config.SKIP_NEGATIVE_AT_CFG1 and math.isclose(cfg, 1.0))
    positive, negative, latent_image, mask = encode_prompt(
        clip, vae, prompt, image_path, mask_path, spec=spec, mask_binary=mask_binary,
        additional_images=additional_images, need_negative=need_negative,
    )

    # batch 3: an optional capability plugin may replace the model / schedule / sampler
    # for a maskless image edit. Runs after `encode_prompt` because the plan needs the
    # latent (schedule depends on its token count) and the mask (edit-only predicate).
    # Disabled / declining plugins return None, leaving every step below unchanged.
    plan = plugin_sampling.resolve_plan(plugin_sampling.build_context(
        op=op, model=model, clip=clip, vae=vae, latent=latent_image, mask=mask,
        prompt=prompt, image_path=image_path, mask_path=mask_path, steps=steps,
        denoise=denoise, seed=seed, cfg=cfg, sampler_preset=sampler, model_id=model_id,
    ))
    plan_sigmas = plugin_sampling.plan_sigmas(plan)
    if plan is not None:
        model = plugin_sampling.plan_model(plan, model)
        steps = plugin_sampling.plan_steps(plan, steps)

    # Step 8-2: the schedule model + shift are data (models.json sampler block); AuraFlow
    # remains the only supported schedule type. Patch a clone so the resident model is
    # never mutated. A plan that ships its own sigmas defaults to skipping the shift.
    if plugin_sampling.applies_shift(plan, plan_sigmas) and sampler.get("type", "auraflow") == "auraflow":
        shift = float(sampler.get("shift", config.AURAFLOW_SHIFT))
        model = ModelSamplingAuraFlow().patch_aura(model, shift)[0]

    previewer = preview_module.get_previewer(model)
    preview_every = max(1, int(config.PREVIEW_EVERY))

    # First inference triggers ComfyUI's lazy `load_models_gpu()`; announce the
    # sampling stage before sampling so that cost shows up client-side.
    _emit(on_progress, 0, steps, 0.0, "sampling", "moving_to_gpu")

    def callback(step, x0, x, total_steps):
        if poll_cancel is not None:
            poll_cancel()
        mm.throw_exception_if_processing_interrupted()
        total = total_steps or steps
        _emit(
            on_progress,
            step + 1,
            total,
            min(1.0, (step + 1) / float(total)),
            "sampling",
            "sampling",
        )
        if on_preview is not None and previewer is not None and step % preview_every == 0:
            jpeg = preview_module.encode_jpeg(previewer, x0)
            if jpeg:
                on_preview(step, total, jpeg)

    noise = comfy.sample.prepare_noise(latent_image, seed)
    vram_probe.stage("sample BEG (first call loads weights)")

    # batch 3: the plugin installs the LoRA side-branch (a MODEL clone / its hooks) during
    # `sampling_plan`; its cleanup must run even on OOM because `run()` retries `_run_once`
    # per resolution fallback and must not leak / double-apply hooks.
    try:
        samples = plugin_sampling.sample(
            plan=plan, sigmas=plan_sigmas, model=model, noise=noise, positive=positive,
            negative=negative, latent=latent_image, mask=mask, seed=seed, callback=callback,
            steps=steps, denoise=denoise, cfg=cfg, sampler=sampler, legacy_sample=sample,
        )
    finally:
        plugin_sampling.cleanup(plan)
    vram_probe.stage("sample END")

    # A cancel that lands after the last sampling step still aborts here; the
    # VAE decode itself is not interruptible (it is short, see contract §3.3).
    if poll_cancel is not None:
        poll_cancel()
    mm.throw_exception_if_processing_interrupted()
    _emit(on_progress, steps, steps, 1.0, "vae_decode", "vae_decode")
    decoded = vae_decode(vae, samples)
    vram_probe.stage("vae_decode END")
    image, height, width = to_pil(decoded[0])

    save_png(image, output_path)

    return {
        "output_path": output_path,
        "seed": seed,
        "width": width,
        "height": height,
        "resolution": spec.get("value", spec.get("width")),
        "duration_ms": int(round((time.time() - started) * 1000)),
    }


def encode_prompt(clip, vae, prompt, image_path, mask_path, resolution=None, mode=None, spec=None,
                  mask_binary=True, additional_images=None, need_negative=True):
    """Pipeline stage: conditioning + latents from the prompt / reference.

    ``spec`` (Step 6.5) is a normalized resolution dict; when omitted the legacy
    ``resolution`` / ``mode`` arguments are used (backward compatible).
    ``mask_binary`` is passed through to ``_encode``: ``True`` thresholds the
    mask to 0 / 1; ``False`` (handlers for user / C# masks, outpaint) keeps the
    soft 0..1 ramp.
    ``additional_images`` (Step 9C.5-D) are the ordered reference images after the main.
    ``need_negative`` (optimization §10.2.2) skips the negative-prompt encode when
    the sampler will discard it (cfg==1.0); the positive conditioning is reused.
    """
    return _encode(
        clip, vae, prompt, image_path, mask_path, resolution, mode, spec, mask_binary,
        additional_images=additional_images, need_negative=need_negative,
    )


def sample(model, positive, negative, latent, noise, steps, denoise, mask, seed, callback,
           sampler_name=None, scheduler=None, cfg=1.0):
    """Pipeline stage: one sampler pass via ComfyUI's official ``comfy.sample.sample``.

    Step 8-2: ``sampler_name`` / ``scheduler`` / ``cfg`` come from the model registry
    (falling back to the config defaults when absent).
    """
    import comfy.sample

    return comfy.sample.sample(
        model,
        noise,
        steps,
        cfg,
        sampler_name or config.SAMPLER_NAME,
        scheduler or config.SCHEDULER_NAME,
        positive,
        negative,
        latent,
        denoise=denoise,
        noise_mask=mask,
        callback=callback,
        disable_pbar=True,
        seed=seed,
    )


def _oom_types():
    import comfy.model_management as mm

    types = []
    exc = getattr(mm, "OOM_EXCEPTION", None)
    if isinstance(exc, tuple):
        types.extend(exc)
    elif isinstance(exc, type):
        types.append(exc)
    try:
        import torch

        types.append(torch.cuda.OutOfMemoryError)
    except Exception:
        pass
    return tuple(set(types)) or (RuntimeError,)


def _free_vram():
    try:
        import comfy.model_management as mm

        mm.soft_empty_cache(force=True)
    except Exception:
        pass
    try:
        import torch

        if torch.cuda.is_available():
            torch.cuda.empty_cache()
    except Exception:
        pass
    try:
        gc.collect()
    except Exception:
        pass


def _encode(clip, vae, prompt, image_path, mask_path, resolution, mode=None, spec=None,
            mask_binary=True, additional_images=None, need_negative=True):
    """Encode prompt + optional source image into conditioning / latents.

    Returns ``(positive, negative, latent_samples, denoise_mask)``. With a mask
    the target latent is the encoded source (proper inpaint); without one it is
    an empty latent and the source rides along as reference latents (edit).

    ``need_negative`` (optimization §10.2.2): when ``False`` (cfg==1.0, the sampler
    discards uncond) the negative-prompt encode is skipped and the positive
    conditioning is reused for both slots.

    ``spec`` (Step 6.5) is a normalized dict ``{"mode": "side"|"area"|"explicit",
    "value": int}`` (or ``{"mode": "explicit", "width", "height"}``); when it is
    omitted, the legacy ``resolution`` / ``mode`` arguments are used.

    ``additional_images`` (Step 9C.5-D) are the ordered reference images after the
    main image; the first pipeline image stays the main (``<image1>``).
    """
    import comfy.model_management as mm
    import comfy.utils
    import node_helpers
    import torch

    if mode is None:
        mode = config.RESOLUTION_MODE
    if spec is None:
        default_resolution = config.RESOLUTION_SIDE if mode == "side" else config.MAX_RESOLUTION
        spec = {"mode": mode, "value": int(resolution or default_resolution)}

    # Step 9C.5-D: main first, then references. `reference_paths` owns the ordering
    # rule ([main] + extras) so the payload order is defined in exactly one place.
    extras = multi_image.normalize_additional_images(additional_images)
    # t2i has no source image, so references only apply when a main image exists.
    ordered_paths = multi_image.reference_paths(image_path, extras) if image_path else []
    source = _load_image_tensor(ordered_paths[0]) if ordered_paths else None
    mask = _load_mask_tensor(mask_path, binary=mask_binary) if mask_path else None

    references = []
    images_vl = []
    if source is not None:
        samples = source[:1].movedim(-1, 1)  # [1,3,H,W]
        width, height = _target_size_from_spec(samples.shape[3], samples.shape[2], spec)
        if (width, height) == (samples.shape[3], samples.shape[2]):
            resized = source[:1]
        else:
            resized = comfy.utils.common_upscale(
                samples, width, height, "lanczos", "disabled"
            ).movedim(1, -1)
        images_vl.append(resized[:, :, :, :3])
        references.append(vae.encode(resized))

    # Additional references (Step 9C.5-D): each is resized with the same per-image
    # aspect / spec policy as the main image (official-node interpretation, D6) and
    # appended to both the vision list and the reference latents. Main stays [0].
    for extra_path in ordered_paths[1:]:
        extra = _load_image_tensor(extra_path)
        extra_samples = extra[:1].movedim(-1, 1)  # [1,3,H,W]
        width, height = _target_size_from_spec(
            extra_samples.shape[3], extra_samples.shape[2], spec
        )
        if (width, height) == (extra_samples.shape[3], extra_samples.shape[2]):
            extra_resized = extra[:1]
        else:
            extra_resized = comfy.utils.common_upscale(
                extra_samples, width, height, "lanczos", "disabled"
            ).movedim(1, -1)
        images_vl.append(extra_resized[:, :, :, :3])
        references.append(vae.encode(extra_resized))

    keep_vision = len(references) == 0
    positive = clip.encode_from_tokens_scheduled(
        clip.tokenize(prompt, images=images_vl, keep_vision=keep_vision, prevent_empty_text=True)
    )
    if need_negative:
        negative = clip.encode_from_tokens_scheduled(
            clip.tokenize("", images=images_vl, keep_vision=keep_vision, prevent_empty_text=True)
        )
    else:
        # cfg==1.0: the sampler drops uncond, so skip its encode (vision tower included)
        # and reuse the positive conditioning. With references, conditioning_set_values()
        # returns a fresh list per call (no shared mutation); with none, the same object is
        # passed for both slots — harmless because the sampler ignores uncond at cfg==1.0.
        negative = positive
    if references:
        positive = node_helpers.conditioning_set_values(
            positive, {"reference_latents": references}, append=True
        )
        negative = node_helpers.conditioning_set_values(
            negative, {"reference_latents": references}, append=True
        )

    if mask is not None and references:
        latent_samples = references[0]
    else:
        if references:
            latent_h = references[0].shape[2]
            latent_w = references[0].shape[3]
        else:
            width, height = _size_for_no_source(spec)
            latent_h, latent_w = height // 16, width // 16
        latent_samples = torch.zeros(
            [1, 64, latent_h, latent_w], device=mm.intermediate_device()
        )

    if mask is not None:
        # Soft masks (crop-outpaint / feathered) resample bilinearly (official VAEEncodeForInpaint);
        # a hard 0/1 mask stays nearest, so binary hand masks are unchanged (Z19).
        mask = _resize_mask(
            mask,
            latent_samples.shape[3] * 16,
            latent_samples.shape[2] * 16,
            mode="nearest" if _mask_is_binary(mask) else "bilinear",
        )
    return positive, negative, latent_samples, mask
