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
import math
import os
import random
import time
from datetime import datetime

import config
import model_loader
import pipeline_hooks
import preview as preview_module

_LOG = logging.getLogger("zivai.server")


def run(model, clip, vae, request, on_progress=None, on_preview=None, poll_cancel=None):
    """Run one inpaint / edit request.

    ``on_progress(step, total, fraction, stage, message)`` and
    ``on_preview(step, total, jpeg_bytes)`` are optional callbacks invoked from
    the sampler callback (same thread). ``poll_cancel`` is drained once per step
    so a `cancel` frame can raise the interrupt flag mid-sampling (Step 2.4).

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
                on_progress, on_preview, poll_cancel,
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


def _run_once(model, clip, vae, prompt, image_path, mask_path, output_path,
              spec, steps, seed, denoise, started,
              on_progress, on_preview, poll_cancel):
    import comfy.model_management as mm
    import comfy.sample
    from comfy_extras.nodes_model_advanced import ModelSamplingAuraFlow

    # Qwen-Image-2.1 uses the AuraFlow flow schedule (shift=3.1); patch a clone
    # so the engine's resident model is never mutated.
    model = ModelSamplingAuraFlow().patch_aura(model, config.AURAFLOW_SHIFT)[0]

    # Stage 2: encode the prompt + optional reference image (clip is patched).
    positive, negative, latent_image, mask = encode_prompt(
        clip, vae, prompt, image_path, mask_path, spec=spec
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


def encode_prompt(clip, vae, prompt, image_path, mask_path, resolution=None, mode=None, spec=None):
    """Pipeline stage: conditioning + latents from the prompt / reference.

    ``spec`` (Step 6.5) is a normalized resolution dict; when omitted the legacy
    ``resolution`` / ``mode`` arguments are used (backward compatible).
    """
    return _encode(clip, vae, prompt, image_path, mask_path, resolution, mode, spec)


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


def _target_size(width, height, value, mode):
    """Map an input size to (target_width, target_height) for a resolution mode.

    ``side`` keeps the aspect ratio and makes the long edge equal ``value``;
    ``area`` keeps the aspect ratio and makes the pixel area equal ``value**2``
    (the Step 4 formula). Both snap to a multiple of 32 (min 32).
    """
    ratio = width / height
    if mode == "side":
        if ratio >= 1:
            target_w, target_h = value, value / ratio
        else:
            target_w, target_h = value * ratio, value
    else:
        target_w = (value * value * ratio) ** 0.5
        target_h = (value * value / ratio) ** 0.5
    width = max(32, round(target_w / 32) * 32)
    height = max(32, round(target_h / 32) * 32)
    return width, height


def _resolution_candidates(mode=None):
    if mode is None:
        mode = config.RESOLUTION_MODE
    if mode == "side":
        upper = int(config.RESOLUTION_SIDE)
        values = [upper] + [int(v) for v in config.RESOLUTION_SIDE_FALLBACK]
    else:
        upper = int(config.MAX_RESOLUTION)
        values = [upper] + [int(v) for v in config.RESOLUTION_FALLBACK]
    seen = set()
    ordered = []
    for value in values:
        if value > 0 and value <= upper and value not in seen:
            seen.add(value)
            ordered.append(value)
    return ordered or [upper]


def _resolution_specs(request, image_path):
    """Normalized resolution candidates for one run (Step 6.5).

    A payload ``resolution`` wins and yields a single spec; otherwise the config
    default path yields its OOM fallback ladder (backward compatible).
    """
    spec = _normalize_payload_resolution(request.get("resolution"), image_path)
    if spec is not None:
        return [spec]
    mode = config.RESOLUTION_MODE
    return [{"mode": mode, "value": int(v)} for v in _resolution_candidates(mode)]


def _normalize_payload_resolution(payload, image_path):
    """Turn ``submit.payload.resolution`` into a spec, or None to use the default.

    Supported modes: ``side`` / ``area`` / ``scale`` (input long edge × scale) /
    ``explicit`` (width × height). Values above ``max_pixels`` are clamped.
    """
    if not isinstance(payload, dict):
        return None

    mode = str(payload.get("mode") or "").strip().lower()
    max_pixels = _positive_int(payload.get("max_pixels"))

    if mode == "side":
        side = _positive_int(payload.get("side"))
        if side is None:
            return None
        return {"mode": "side", "value": _clamp_side(side, max_pixels)}

    if mode == "area":
        area = _positive_int(payload.get("area"))
        if area is None:
            return None
        return {"mode": "area", "value": _clamp_area(area, max_pixels)}

    if mode == "scale":
        try:
            scale = float(payload.get("scale"))
        except (TypeError, ValueError):
            return None
        if scale <= 0:
            return None
        source_side = _source_long_edge(image_path)
        if source_side is None:
            _LOG.warning("resolution scale requested but input size is unknown; using default")
            return None
        side = _clamp_side(int(round(source_side * scale)), max_pixels)
        _LOG.info("resolution scale=%.3f on source long edge %d -> side=%d", scale, source_side, side)
        return {"mode": "side", "value": side}

    if mode == "explicit":
        width = _positive_int(payload.get("width"))
        height = _positive_int(payload.get("height"))
        if width is None or height is None:
            return None
        width, height = _clamp_explicit(width, height, max_pixels)
        return {"mode": "explicit", "width": width, "height": height}

    if mode:
        _LOG.warning("unknown resolution mode %r; using config default", mode)
    return None


def _positive_int(value):
    try:
        number = int(value)
    except (TypeError, ValueError):
        return None
    return number if number > 0 else None


def _clamp_side(side, max_pixels):
    side = max(32, int(side))
    if max_pixels:
        limit = max(32, int(math.isqrt(int(max_pixels))))
        if side > limit:
            _LOG.warning(
                "resolution side %d exceeds max_pixels %d; clamped to %d",
                side, max_pixels, limit,
            )
            side = limit
    return side


def _clamp_area(area, max_pixels):
    area = max(32 * 32, int(area))
    if max_pixels and area > max_pixels:
        _LOG.warning("resolution area %d exceeds max_pixels %d; clamped", area, max_pixels)
        area = int(max_pixels)
    return area


def _clamp_explicit(width, height, max_pixels):
    width = max(32, int(width))
    height = max(32, int(height))
    if max_pixels and width * height > max_pixels:
        factor = (float(max_pixels) / float(width * height)) ** 0.5
        clamped_w = max(32, int(width * factor))
        clamped_h = max(32, int(height * factor))
        _LOG.warning(
            "resolution %dx%d exceeds max_pixels %d; clamped to %dx%d",
            width, height, max_pixels, clamped_w, clamped_h,
        )
        width, height = clamped_w, clamped_h
    return width, height


def _source_long_edge(path):
    if not path:
        return None
    try:
        from PIL import Image

        with Image.open(path) as image:
            return max(image.size)
    except Exception:
        return None


def _snap16(value):
    return max(32, int(value) // 16 * 16)


def _target_size_from_spec(width, height, spec):
    """Map a source size to a target size for a normalized resolution spec."""
    mode = spec.get("mode")
    if mode == "explicit":
        return _snap16(spec.get("width")), _snap16(spec.get("height"))
    value = int(spec.get("value"))
    return _target_size(width, height, value, "area" if mode == "area" else "side")


def _size_for_no_source(spec):
    """Target (width, height) when there is no source image (t2i)."""
    mode = spec.get("mode")
    if mode == "explicit":
        return _snap16(spec.get("width")), _snap16(spec.get("height"))
    value = int(spec.get("value"))
    if mode == "area":
        side = max(32, round((value ** 0.5) / 32) * 32)
    else:
        side = max(32, round(value / 32) * 32)
    return side, side


def _spec_label(spec):
    mode = spec.get("mode")
    if mode == "explicit":
        return "%dx%d" % (int(spec.get("width")), int(spec.get("height")))
    return "%s:%s" % (mode, spec.get("value"))


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


def _encode(clip, vae, prompt, image_path, mask_path, resolution, mode=None, spec=None):
    """Encode prompt + optional source image into conditioning / latents.

    Returns ``(positive, negative, latent_samples, denoise_mask)``. With a mask
    the target latent is the encoded source (proper inpaint); without one it is
    an empty latent and the source rides along as reference latents (edit).

    ``spec`` (Step 6.5) is a normalized dict ``{"mode": "side"|"area"|"explicit",
    "value": int}`` (or ``{"mode": "explicit", "width", "height"}``); when it is
    omitted, the legacy ``resolution`` / ``mode`` arguments are used.
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

    source = _load_image_tensor(image_path) if image_path else None
    mask = _load_mask_tensor(mask_path) if mask_path else None

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


def _load_mask_tensor(path):
    import numpy as np
    import torch
    from PIL import Image

    image = Image.open(path).convert("L")
    array = np.asarray(image).astype(np.float32) / 255.0
    array = (array >= 0.5).astype(np.float32)  # Z19: force binary 0 / 1
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
