"""ZIV.AI 1024-scale end-to-end test (Step 4 follow-up).

Uses the community sampling recipe (euler / simple / 40 steps / CFG 1.0 /
ModelSamplingAuraFlow shift=3.1) via the official comfy.sample.sample path.
Does NOT modify pipeline.py or config.py.

Outputs:
  user_output_1024.png     edit of user_input_1024.png (1024x640, 16:10)
  user_output_1024_sq.png  square 1024x1024 T2I with the same recipe
"""

import os
import sys
import time
import traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
TE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3vl_8b_int8_convrot.safetensors"
VAE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\vae\qwen_image_2.1_vae_bf16.safetensors"
HERE = r"D:\devlop\ZIV.AI\_test_step2"
INPUT = os.path.join(HERE, "user_input_1024.png")
OUT_EDIT = os.path.join(HERE, "user_output_1024.png")
OUT_SQ = os.path.join(HERE, "user_output_1024_sq.png")

PROMPT = "把背景替换为古代中式茶肆，画面内应有木质桌椅、悬挂的灯笼、古朴的装饰，保留画面中的人物、衣着与前景陈设不变，去掉右上角水印"
STEPS = 40
CFG = 1.0
SAMPLER = "euler"
SCHEDULER = "simple"
SHIFT = 3.1
SEED = 42

os.chdir(COMFY)
sys.path.insert(0, COMFY)

import numpy as np
import torch
from PIL import Image

import comfy.model_management as mm
import comfy.sample
import comfy.sd
import comfy.utils
import node_helpers
from comfy_extras.nodes_model_advanced import ModelSamplingAuraFlow


def to_pil(tensor):
    array = tensor.detach().cpu().float().clamp(0, 1).numpy()
    array = (array * 255.0).round().astype(np.uint8)
    return Image.fromarray(array)


def load_image_tensor(path):
    array = np.asarray(Image.open(path).convert("RGB")).astype(np.float32) / 255.0
    return torch.from_numpy(array)[None, ...]


def encode_edit(clip, vae, prompt, source, width, height):
    samples = source[:1].movedim(-1, 1)
    resized = comfy.utils.common_upscale(samples, width, height, "lanczos", "disabled").movedim(1, -1)
    ref = vae.encode(resized)
    positive = clip.encode_from_tokens_scheduled(
        clip.tokenize(prompt, images=[resized[:, :, :, :3]], keep_vision=False, prevent_empty_text=True)
    )
    negative = clip.encode_from_tokens_scheduled(
        clip.tokenize("", images=[resized[:, :, :, :3]], keep_vision=False, prevent_empty_text=True)
    )
    positive = node_helpers.conditioning_set_values(positive, {"reference_latents": [ref]}, append=True)
    negative = node_helpers.conditioning_set_values(negative, {"reference_latents": [ref]}, append=True)
    latent = torch.zeros([1, 64, height // 16, width // 16], device=mm.intermediate_device())
    return positive, negative, latent


def run_sample(model, positive, negative, latent):
    timing = {"moving_to_gpu": None, "sampling": None}
    state = {"t0": time.time(), "t_first": None}

    def callback(step, x0, x, total_steps):
        if state["t_first"] is None:
            state["t_first"] = time.time()
            timing["moving_to_gpu"] = state["t_first"] - state["t0"]

    t0 = time.time()
    noise = comfy.sample.prepare_noise(latent, SEED)
    samples = comfy.sample.sample(
        model, noise, STEPS, CFG, SAMPLER, SCHEDULER,
        positive, negative, latent,
        denoise=1.0, disable_pbar=True, seed=SEED, callback=callback,
    )
    end = time.time()
    timing["sampling"] = end - (state["t_first"] or end)
    timing["sample_total"] = end - t0
    return samples, timing


def main():
    print("=== ZIV.AI 1024 e2e test ===")
    print("params: steps=%d cfg=%s sampler=%s scheduler=%s shift=%s seed=%d"
          % (STEPS, CFG, SAMPLER, SCHEDULER, SHIFT, SEED))

    t = time.time()
    model = comfy.sd.load_diffusion_model(DIT)
    clip = comfy.sd.load_clip([TE], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    vae = comfy.sd.VAE(comfy.utils.load_torch_file(VAE))
    load_s = time.time() - t
    print("model_load_s = %.3f  cond_stage_model=%s" % (load_s, type(clip.cond_stage_model).__name__))

    model = ModelSamplingAuraFlow().patch_aura(model, SHIFT)[0]
    ms = model.get_model_object("model_sampling")
    print("patched model_sampling=%s shift=%s" % (type(ms).__name__, getattr(ms, "shift", None)))

    torch.cuda.reset_peak_memory_stats()

    # ---- Edit 1024x640 ----
    source = load_image_tensor(INPUT)
    positive, negative, latent = encode_edit(clip, vae, PROMPT, source, 1024, 640)
    print("edit latent_shape", tuple(latent.shape))
    samples, timing = run_sample(model, positive, negative, latent)
    t0 = time.time()
    image = to_pil(vae.decode(samples)[0])
    decode_s = time.time() - t0
    image.save(OUT_EDIT)
    print("EDIT  output=%s size=%s bytes=%d" % (OUT_EDIT, image.size, os.path.getsize(OUT_EDIT)))

    # ---- Square 1024x1024 T2I ----
    pos_t = clip.encode_from_tokens_scheduled(clip.tokenize(PROMPT, prevent_empty_text=True))
    neg_t = clip.encode_from_tokens_scheduled(clip.tokenize("", prevent_empty_text=True))
    latent_sq = torch.zeros([1, 64, 64, 64], device=mm.intermediate_device())
    samples_sq, timing_sq = run_sample(model, pos_t, neg_t, latent_sq)
    t0 = time.time()
    image_sq = to_pil(vae.decode(samples_sq)[0])
    decode_sq = time.time() - t0
    image_sq.save(OUT_SQ)
    print("SQ    output=%s size=%s bytes=%d" % (OUT_SQ, image_sq.size, os.path.getsize(OUT_SQ)))

    print("--- timing (edit) ---")
    print("  model_load_s     = %.3f" % load_s)
    print("  moving_to_gpu_s  = %.3f" % timing["moving_to_gpu"])
    print("  sampling_s       = %.3f" % timing["sampling"])
    print("  sample_total_s   = %.3f" % timing["sample_total"])
    print("  vae_decode_s     = %.3f" % decode_s)
    print("--- timing (square t2i) ---")
    print("  moving_to_gpu_s  = %.3f" % timing_sq["moving_to_gpu"])
    print("  sampling_s       = %.3f" % timing_sq["sampling"])
    print("  vae_decode_s     = %.3f" % decode_sq)
    print("peak_alloc_MB = %.1f" % (torch.cuda.max_memory_allocated() / 1048576))
    print("RESULT=DONE")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print("RESULT=FAIL err=%r" % (exc,))
        traceback.print_exc()
