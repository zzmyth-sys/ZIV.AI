"""Inference pipeline for the ZIV.AI backend (Step 2.3).

Mirrors the verified script `_test_step2/t5_e2e_inpaint.py`: Qwen-Image-2.1
text encoding -> ``CFGGuider(cfg=1.0)`` -> ``euler`` sampler with the Flux
schedule -> VAE decode -> PNG. An optional binary mask (Z19) is applied as the
sampler ``denoise_mask`` and reference latents inject the source image for
image editing. Output is always a new file (Z24); the source is never written.

Heavy imports (torch / comfy) stay inside the functions so a cold ``ping``
never touches the heavy stack.
"""

import os
import random
import time
from datetime import datetime

import config
import model_loader
import preview as preview_module


def run(model, clip, vae, request, on_progress=None, on_preview=None, poll_cancel=None):
    """Run one inpaint / edit request.

    ``on_progress(step, total, fraction, stage, message)`` and
    ``on_preview(step, total, jpeg_bytes)`` are optional callbacks invoked from
    the sampler callback (same thread). ``poll_cancel`` is drained once per step
    so a `cancel` frame can raise the interrupt flag mid-sampling (Step 2.4).
    """
    model_loader.prepare_environment()

    import comfy.model_management as mm
    import comfy.sample
    import comfy.samplers
    import torch
    from comfy_extras.nodes_flux import get_schedule

    started = time.time()
    steps = max(1, int(request.get("steps") or config.DEFAULT_STEPS))
    seed = _resolve_seed(request.get("seed"))
    prompt = request.get("prompt") or ""
    image_path = request.get("image_path")
    mask_path = request.get("mask_path")
    output_path = _resolve_output_path(request.get("output_path"), image_path)

    positive, negative, latent_image, mask = _encode(
        clip, vae, prompt, image_path, mask_path
    )

    samples_latent = latent_image
    latent_h = samples_latent.shape[2]
    latent_w = samples_latent.shape[3]
    seq_len = (latent_h * 16 * latent_w * 16) // 256
    sigmas = get_schedule(steps, seq_len).to(dtype=torch.float32)

    guider = comfy.samplers.CFGGuider(model)
    guider.set_conds(positive, negative)
    guider.set_cfg(1.0)
    sampler = comfy.samplers.sampler_object("euler")
    noise = comfy.sample.prepare_noise(samples_latent, seed)

    previewer = preview_module.get_previewer(model)
    preview_every = max(1, int(config.PREVIEW_EVERY))

    # First inference triggers ComfyUI's lazy `load_models_gpu()`; announce the
    # sampling stage before `guider.sample` so that cost shows up client-side.
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

    samples = guider.sample(
        noise,
        samples_latent,
        sampler,
        sigmas,
        denoise_mask=mask,
        callback=callback,
        disable_pbar=True,
        seed=seed,
    )

    # A cancel that lands after the last sampling step still aborts here; the
    # VAE decode itself is not interruptible (it is short, see contract §3.3).
    if poll_cancel is not None:
        poll_cancel()
    mm.throw_exception_if_processing_interrupted()
    _emit(on_progress, steps, steps, 1.0, "vae_decode", "vae_decode")
    decoded = vae.decode(samples)
    image, height, width = _to_pil(decoded[0])

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    image.save(output_path)

    return {
        "output_path": output_path,
        "seed": seed,
        "width": width,
        "height": height,
        "duration_ms": int(round((time.time() - started) * 1000)),
    }


def _encode(clip, vae, prompt, image_path, mask_path):
    """Encode prompt + optional source image into conditioning / latents.

    Returns ``(positive, negative, latent_samples, denoise_mask)``. With a mask
    the target latent is the encoded source (proper inpaint); without one it is
    an empty latent and the source rides along as reference latents (edit).
    """
    import comfy.model_management as mm
    import comfy.utils
    import node_helpers
    import torch

    source = _load_image_tensor(image_path) if image_path else None
    mask = _load_mask_tensor(mask_path) if mask_path else None

    references = []
    images_vl = []
    latent_pixels = config.DEFAULT_RESOLUTION
    if source is not None:
        samples = source[:1].movedim(-1, 1)  # [1,3,H,W]
        ratio = samples.shape[3] / samples.shape[2]
        width = round((latent_pixels * latent_pixels * ratio) ** 0.5 / 32) * 32
        height = round((latent_pixels * latent_pixels / ratio) ** 0.5 / 32) * 32
        width, height = max(32, width), max(32, height)
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
            latent_h = latent_w = latent_pixels // 16
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
