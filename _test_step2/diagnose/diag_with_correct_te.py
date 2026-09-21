"""Confirmation: Qwen-Image-2.1 DiT with the matching Qwen3-VL-8B TE.

Re-runs the same generation as diag_schedule.py but with the correct text
encoder. If images are now valid, the earlier noise is confirmed to be a
TE/DiT mismatch. No pipeline.py change.
"""

import math
import os
import sys
import time
import traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
TE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3vl_8b_int8_convrot.safetensors"
VAE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\vae\qwen_image_2.1_vae_bf16.safetensors"
HERE = r"D:\devlop\ZIV.AI\_test_step2\diagnose"
INPUT = r"D:\devlop\ZIV.AI\_test_step2\input_test_512.png"

os.makedirs(HERE, exist_ok=True)
sys.path.insert(0, COMFY)
os.chdir(COMFY)

import numpy as np
import torch
from PIL import Image

import comfy.model_management as mm
import comfy.sample
import comfy.samplers
import comfy.sd
import comfy.utils
import node_helpers
from comfy_extras.nodes_flux import get_schedule

STEPS = 20
SEED = 42
EDIT_PROMPT = "Make the background a snowy mountain landscape, keep the subject unchanged."
T2I_PROMPT = "A serene snowy mountain landscape at sunrise, highly detailed."


def to_pil(tensor):
    array = tensor.detach().cpu().float().clamp(0, 1).numpy()
    array = (array * 255.0).round().astype(np.uint8)
    return Image.fromarray(array)


def report(name, tensor, t0):
    image = to_pil(tensor)
    path = os.path.join(HERE, name + ".png")
    image.save(path)
    array = np.asarray(image).astype(np.float32) / 255.0
    print("%-22s mean=%.3f std=%.3f saved=%s elapsed=%.1fs"
          % (name, array.mean(), array.std(), path, time.time() - t0))


def load_image_tensor(path):
    array = np.asarray(Image.open(path).convert("RGB")).astype(np.float32) / 255.0
    return torch.from_numpy(array)[None, ...]


def encode_edit(clip, vae, prompt, source, resolution):
    samples = source[:1].movedim(-1, 1)
    ratio = samples.shape[3] / samples.shape[2]
    width = round(math.sqrt(resolution * resolution * ratio) / 32) * 32
    height = round(math.sqrt(resolution * resolution / ratio) / 32) * 32
    width, height = max(32, width), max(32, height)
    if (width, height) == (samples.shape[3], samples.shape[2]):
        resized = source[:1]
    else:
        resized = comfy.utils.common_upscale(samples, width, height, "lanczos", "disabled").movedim(1, -1)
    images_vl = [resized[:, :, :, :3]]
    ref = vae.encode(resized)
    positive = clip.encode_from_tokens_scheduled(
        clip.tokenize(prompt, images=images_vl, keep_vision=False, prevent_empty_text=True)
    )
    negative = clip.encode_from_tokens_scheduled(
        clip.tokenize("", images=images_vl, keep_vision=False, prevent_empty_text=True)
    )
    positive = node_helpers.conditioning_set_values(positive, {"reference_latents": [ref]}, append=True)
    negative = node_helpers.conditioning_set_values(negative, {"reference_latents": [ref]}, append=True)
    latent = torch.zeros([1, 64, ref.shape[2], ref.shape[3]], device=mm.intermediate_device())
    return positive, negative, latent


def main():
    print("=== confirmation with Qwen3-VL-8B TE ===")
    t = time.time()
    model = comfy.sd.load_diffusion_model(DIT)
    clip = comfy.sd.load_clip([TE], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    vae = comfy.sd.VAE(comfy.utils.load_torch_file(VAE))
    print("loaded in %.1fs" % (time.time() - t))
    print("cond_stage_model =", type(clip.cond_stage_model).__name__)
    ms = model.get_model_object("model_sampling")
    print("model_sampling = %s shift=%s" % (type(ms).__name__, getattr(ms, "shift", None)))

    # --- T2I, official sample, cfg 1.0 / 4.0 ---
    pos_t = clip.encode_from_tokens_scheduled(clip.tokenize(T2I_PROMPT, prevent_empty_text=True))
    neg_t = clip.encode_from_tokens_scheduled(clip.tokenize("", prevent_empty_text=True))
    latent_t2i = torch.zeros([1, 64, 32, 32], device=mm.intermediate_device())
    for cfg in (1.0, 4.0):
        t0 = time.time()
        out = vae.decode(comfy.sample.sample(
            model, comfy.sample.prepare_noise(latent_t2i, SEED), STEPS, cfg, "euler", "simple",
            pos_t, neg_t, latent_t2i, denoise=1.0, disable_pbar=True, seed=SEED,
        ))[0]
        report("T2I_cfg%.1f" % cfg, out, t0)

    # --- Edit (reference), official sample, cfg 1.0 / 4.0 ---
    source = load_image_tensor(INPUT)
    positive, negative, latent = encode_edit(clip, vae, EDIT_PROMPT, source, 512)
    seq_len = (latent.shape[2] * 16 * latent.shape[3] * 16) // 256
    for cfg in (1.0, 4.0):
        t0 = time.time()
        out = vae.decode(comfy.sample.sample(
            model, comfy.sample.prepare_noise(latent, SEED), STEPS, cfg, "euler", "simple",
            positive, negative, latent, denoise=1.0, disable_pbar=True, seed=SEED,
        ))[0]
        report("EDIT_cfg%.1f" % cfg, out, t0)

    # --- Edit using the original pipeline schedule (get_schedule) for contrast ---
    t0 = time.time()
    sigmas = get_schedule(STEPS, seq_len).to(dtype=torch.float32)
    guider = comfy.samplers.CFGGuider(model)
    guider.set_conds(positive, negative)
    guider.set_cfg(4.0)
    sampler = comfy.samplers.sampler_object("euler")
    out = vae.decode(guider.sample(
        comfy.sample.prepare_noise(latent, SEED), latent, sampler, sigmas,
        disable_pbar=True, seed=SEED,
    ))[0]
    report("EDIT_get_schedule_cfg4", out, t0)

    if torch.cuda.is_available():
        print("peak_alloc_MB", round(torch.cuda.max_memory_allocated() / 1048576, 1))
    print("RESULT=DONE")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print("RESULT=FAIL err=%r" % (exc,))
        traceback.print_exc()
