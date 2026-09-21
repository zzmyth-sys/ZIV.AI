"""ZIV.AI noise root-cause diagnosis (Step 4 follow-up).

Loads the Qwen-Image-2.1 triple once, then runs the same edit across sampling
configurations to find which one stops producing noise. No pipeline.py change.

Variants:
  ROUNDTRIP : vae.encode -> vae.decode (no sampling)   [VAE sanity]
  V0        : current pipeline path (nodes_flux.get_schedule + CFGGuider)
  V1        : model_sampling-derived sigmas + CFGGuider
  V2s/V2n   : official comfy.sample.sample (scheduler simple / normal)
  V3t2i     : official comfy.sample.sample, no reference image (T2I)
"""

import math
import os
import sys
import time
import traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
TE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors"
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
PROMPT = "Make the background a snowy mountain landscape, keep the subject unchanged."


def to_pil(tensor):
    array = tensor.detach().cpu().float().clamp(0, 1).numpy()
    array = (array * 255.0).round().astype(np.uint8)
    return Image.fromarray(array)


def report(name, tensor, t0):
    image = to_pil(tensor)
    path = os.path.join(HERE, name + ".png")
    image.save(path)
    array = np.asarray(image).astype(np.float32) / 255.0
    print(
        "%-9s size=%s mean=%.3f std=%.3f saved=%s elapsed=%.1fs"
        % (name, image.size, array.mean(), array.std(), path, time.time() - t0)
    )


def load_image_tensor(path):
    image = Image.open(path).convert("RGB")
    array = np.asarray(image).astype(np.float32) / 255.0
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
    images_vl = [images_vl[0][:, :, :, :3]]
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
    print("=== ZIV.AI noise root-cause diagnosis ===")
    t = time.time()
    model = comfy.sd.load_diffusion_model(DIT)
    clip = comfy.sd.load_clip([TE], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    vae = comfy.sd.VAE(comfy.utils.load_torch_file(VAE))
    print("loaded in %.1fs" % (time.time() - t))

    ms = model.get_model_object("model_sampling")
    print("model_sampling=%s shift=%s multiplier=%s"
          % (type(ms).__name__, getattr(ms, "shift", None), getattr(ms, "multiplier", None)))

    source = load_image_tensor(INPUT)
    positive, negative, latent = encode_edit(clip, vae, PROMPT, source, 512)
    print("latent_shape", tuple(latent.shape), "seq_len", (latent.shape[2] * 16 * latent.shape[3] * 16) // 256)

    # ROUNDTRIP: VAE sanity (encode source, decode back)
    t0 = time.time()
    rt_latent = vae.encode(source)
    report("ROUNDTRIP", vae.decode(rt_latent)[0], t0)

    seq_len = (latent.shape[2] * 16 * latent.shape[3] * 16) // 256
    noise = comfy.sample.prepare_noise(latent, SEED)

    # V0: current pipeline path
    t0 = time.time()
    sigmas0 = get_schedule(STEPS, seq_len).to(dtype=torch.float32)
    guider = comfy.samplers.CFGGuider(model)
    guider.set_conds(positive, negative)
    guider.set_cfg(1.0)
    sampler = comfy.samplers.sampler_object("euler")
    out0 = vae.decode(guider.sample(noise.clone(), latent, sampler, sigmas0, disable_pbar=True, seed=SEED))[0]
    report("V0_get_schedule", out0, t0)

    # V1: model_sampling-derived sigmas + CFGGuider
    t0 = time.time()
    sigmas1 = comfy.samplers.calculate_sigmas(ms, "simple", STEPS)
    guider = comfy.samplers.CFGGuider(model)
    guider.set_conds(positive, negative)
    guider.set_cfg(1.0)
    out1 = vae.decode(guider.sample(noise.clone(), latent, sampler, sigmas1, disable_pbar=True, seed=SEED))[0]
    report("V1_model_sampling", out1, t0)

    # V2: official comfy.sample.sample (scheduler simple / normal)
    for sched in ("simple", "normal"):
        t0 = time.time()
        out2 = vae.decode(comfy.sample.sample(
            model, noise.clone(), STEPS, 1.0, "euler", sched,
            positive, negative, latent, denoise=1.0, disable_pbar=True, seed=SEED,
        ))[0]
        report("V2_sample_%s" % sched, out2, t0)

    # V3: official T2I (no reference image)
    t0 = time.time()
    pos_t = clip.encode_from_tokens_scheduled(clip.tokenize(PROMPT, prevent_empty_text=True))
    neg_t = clip.encode_from_tokens_scheduled(clip.tokenize("", prevent_empty_text=True))
    latent_t2i = torch.zeros([1, 64, 32, 32], device=mm.intermediate_device())
    out3 = vae.decode(comfy.sample.sample(
        model, comfy.sample.prepare_noise(latent_t2i, SEED), STEPS, 1.0, "euler", "simple",
        pos_t, neg_t, latent_t2i, denoise=1.0, disable_pbar=True, seed=SEED,
    ))[0]
    report("V3_t2i_official", out3, t0)

    if torch.cuda.is_available():
        print("peak_alloc_MB", round(torch.cuda.max_memory_allocated() / 1048576, 1))
    print("RESULT=DONE")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print("RESULT=FAIL err=%r" % (exc,))
        traceback.print_exc()
