"""Inference pipeline for the ZIV.AI backend.

Qwen-Image-2.1 text encoding -> ModelSamplingAuraFlow(shift=3.1) ->
``comfy.sample.sample`` (euler / simple / cfg=1.0) -> VAE decode -> PNG.
An optional binary mask (Z19) is applied as the sampler noise mask and
reference latents inject the source image for image editing. Output is always
a new file (Z24); the source is never written.

OOM fallback (Step 4): the request is retried at MAX_RESOLUTION, then down the
RESOLUTION_FALLBACK list, freeing caches between attempts.

Heavy imports (torch / comfy) stay inside the functions so a cold ``ping``
never touches the heavy stack.
"""

import gc
import logging
import os
import random
import time
from datetime import datetime

import config
import model_loader
import multi_image
import outpaint
import pipeline_hooks
import preview as preview_module
from resolution import (
    _normalize_payload_resolution,
    _resolution_specs,
    _size_for_no_source,
    _spec_label,
    _target_size_from_spec,
)

_LOG = logging.getLogger("zivai.server")


def run(model, clip, vae, request, on_progress=None, on_preview=None, poll_cancel=None,
        mask_binary=True):
    """Run one inpaint / edit request.

    ``on_progress(step, total, fraction, stage, message)`` and
    ``on_preview(step, total, jpeg_bytes)`` are optional callbacks invoked from
    the sampler callback (same thread). ``poll_cancel`` is drained once per step
    so a `cancel` frame can raise the interrupt flag mid-sampling (Step 2.4).

    ``mask_binary`` keeps the Z19 rule for user/C# masks (0 / 255); the outpaint
    path passes ``False`` so its backend-generated feathered mask stays soft.

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

    # Step 6.5: an optional payload `resolution` overrides the config default;
    # absent -> the config default path (backward compatible with ipc 0.5).
    specs = _resolution_specs(request, image_path)
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
                additional_images=additional_images,
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
            mask_binary=False,
        )
    finally:
        _remove_tree(workdir)


def _remove_tree(path):
    import shutil

    try:
        shutil.rmtree(path, ignore_errors=True)
    except Exception:
        pass


def _run_once(model, clip, vae, prompt, image_path, mask_path, output_path,
              spec, steps, seed, denoise, started,
              on_progress, on_preview, poll_cancel, mask_binary=True,
              additional_images=None):
    import comfy.model_management as mm
    import comfy.sample
    from comfy_extras.nodes_model_advanced import ModelSamplingAuraFlow

    # Qwen-Image-2.1 uses the AuraFlow flow schedule (shift=3.1); patch a clone
    # so the engine's resident model is never mutated.
    model = ModelSamplingAuraFlow().patch_aura(model, config.AURAFLOW_SHIFT)[0]

    # Stage 2: encode the prompt + optional reference image (clip is patched).
    positive, negative, latent_image, mask = encode_prompt(
        clip, vae, prompt, image_path, mask_path, spec=spec, mask_binary=mask_binary,
        additional_images=additional_images,
    )

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
    samples = sample(model, positive, negative, latent_image, noise, steps, denoise, mask, seed, callback)

    # A cancel that lands after the last sampling step still aborts here; the
    # VAE decode itself is not interruptible (it is short, see contract §3.3).
    if poll_cancel is not None:
        poll_cancel()
    mm.throw_exception_if_processing_interrupted()
    _emit(on_progress, steps, steps, 1.0, "vae_decode", "vae_decode")
    decoded = vae_decode(vae, samples)
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
                  mask_binary=True, additional_images=None):
    """Pipeline stage: conditioning + latents from the prompt / reference.

    ``spec`` (Step 6.5) is a normalized resolution dict; when omitted the legacy
    ``resolution`` / ``mode`` arguments are used (backward compatible).
    ``mask_binary`` is passed through to ``_encode`` (outpaint uses a soft mask).
    ``additional_images`` (Step 9C.5-D) are the ordered reference images after the main.
    """
    return _encode(
        clip, vae, prompt, image_path, mask_path, resolution, mode, spec, mask_binary,
        additional_images=additional_images,
    )


def sample(model, positive, negative, latent, noise, steps, denoise, mask, seed, callback):
    """Pipeline stage: one sampler pass via ComfyUI's official ``comfy.sample.sample``."""
    import comfy.sample

    return comfy.sample.sample(
        model,
        noise,
        steps,
        1.0,
        config.SAMPLER_NAME,
        config.SCHEDULER_NAME,
        positive,
        negative,
        latent,
        denoise=denoise,
        noise_mask=mask,
        callback=callback,
        disable_pbar=True,
        seed=seed,
    )


def vae_decode(vae, samples):
    """Pipeline stage: decode the sampled latent back to pixels."""
    return vae.decode(samples)


def to_pil(tensor):
    """Convert a decoded tensor to a PIL image plus (height, width)."""
    return _to_pil(tensor)


def save_png(image, output_path):
    """Pipeline stage: write the output as a new PNG file (Z24)."""
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    image.save(output_path)


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
            mask_binary=True, additional_images=None):
    """Encode prompt + optional source image into conditioning / latents.

    Returns ``(positive, negative, latent_samples, denoise_mask)``. With a mask
    the target latent is the encoded source (proper inpaint); without one it is
    an empty latent and the source rides along as reference latents (edit).

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
    negative = clip.encode_from_tokens_scheduled(
        clip.tokenize("", images=images_vl, keep_vision=keep_vision, prevent_empty_text=True)
    )
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
        mask = _resize_mask(mask, latent_samples.shape[3] * 16, latent_samples.shape[2] * 16)
    return positive, negative, latent_samples, mask


def _load_image_tensor(path):
    import numpy as np
    import torch
    from PIL import Image

    image = Image.open(path).convert("RGB")
    array = np.asarray(image).astype(np.float32) / 255.0
    return torch.from_numpy(array)[None, ...]  # [1,H,W,3]


def _load_mask_tensor(path, binary=True):
    import numpy as np
    import torch
    from PIL import Image

    image = Image.open(path).convert("L")
    array = np.asarray(image).astype(np.float32) / 255.0
    if binary:
        array = (array >= 0.5).astype(np.float32)  # Z19: user/C# masks are binary 0 / 1
    # binary=False keeps the soft 0..1 ramp of a backend-generated outpaint mask.
    return torch.from_numpy(array)[None, ...]  # [1,H,W]


def _resize_mask(mask, width, height):
    import torch

    if mask.shape[-1] == width and mask.shape[-2] == height:
        return mask
    resized = torch.nn.functional.interpolate(
        mask[:1, None], size=(height, width), mode="nearest"
    )
    return resized[0]


def _to_pil(tensor):
    import numpy as np
    from PIL import Image

    array = tensor.detach().cpu().float().clamp(0, 1).numpy()
    array = (array * 255.0).round().astype(np.uint8)
    image = Image.fromarray(array)
    return image, image.height, image.width


def _resolve_denoise(value):
    try:
        number = float(value)
    except (TypeError, ValueError):
        return 1.0
    return min(1.0, max(0.0, number))


def _resolve_seed(seed):
    try:
        value = int(seed)
    except (TypeError, ValueError):
        value = -1
    if value < 0:
        return random.randint(0, 0x7FFFFFFF)
    return value


def _resolve_output_path(requested, image_path):
    """Pick an output path that never overwrites the source (Z24 / SPEC §3.9)."""
    if requested:
        candidate = os.path.abspath(requested)
        if not image_path or candidate != os.path.abspath(image_path):
            return candidate
    if image_path:
        directory = os.path.dirname(os.path.abspath(image_path))
        stem = os.path.splitext(os.path.basename(image_path))[0]
    else:
        directory = config.OUTPUT_DIR
        os.makedirs(directory, exist_ok=True)
        stem = "zivai"

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    candidate = os.path.join(directory, "%s_ai_%s.png" % (stem, stamp))
    suffix = 1
    while os.path.exists(candidate):
        candidate = os.path.join(directory, "%s_ai_%s_%d.png" % (stem, stamp, suffix))
        suffix += 1
    return candidate


def _emit(on_progress, step, total, fraction, stage, message):
    if on_progress is not None:
        on_progress(step, total, fraction, stage, message)
