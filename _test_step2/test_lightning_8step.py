"""ZIV.AI Lightning LoRA (8-step distilled) direct test.

Replicates the test_1024.py load/sample path and adds a Lightning LoRA via
``comfy.sd.load_lora_for_models``. One strength per process invocation so peak
VRAM is clean and GPU work stays serial (Z18).

Usage:
    python test_lightning_8step.py <strength_model> [strength_clip]

Does NOT modify pipeline.py / config.py / handlers.py.
"""

import logging
import os
import sys
import threading
import time
import traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
TE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3vl_8b_int8_convrot.safetensors"
VAE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\vae\qwen_image_2.1_vae_bf16.safetensors"
LORA = r"C:\AI\ComfyUI_PIC\ComfyUI\models\loras\qwen_image2\Qwen-Image-Lightning-8steps-V2.0-bf16.safetensors"
HERE = r"D:\devlop\ZIV.AI\_test_step2"
INPUT = os.path.join(HERE, "user_input_1024.png")

PROMPT = "把背景替换为古代中式茶肆，画面内应有木质桌椅、悬挂的灯笼、古朴的装饰，保留画面中的人物、衣着与前景陈设不变，去掉右上角水印"
STEPS = 8
CFG = 1.0
SAMPLER = "euler"
SCHEDULER = "simple"
SHIFT = 3.1
SEED = 42

strength_model = float(sys.argv[1]) if len(sys.argv) > 1 else 0.9
strength_clip = float(sys.argv[2]) if len(sys.argv) > 2 else 0.0
tag = ("%g" % strength_model).replace(".", "p")
OUT = os.path.join(HERE, "lightning_8step_strength%s.png" % tag)

os.chdir(COMFY)
sys.path.insert(0, COMFY)

# Capture LoRA loader warnings ("lora key not loaded", etc.).
_lora_logs = []


class _Capture(logging.Handler):
    def emit(self, record):
        if record.levelno >= logging.WARNING:
            _lora_logs.append(record.getMessage())


logging.getLogger().addHandler(_Capture())
logging.getLogger().setLevel(logging.INFO)

import numpy as np  # noqa: E402
import torch  # noqa: E402
from PIL import Image  # noqa: E402

import comfy.model_management as mm  # noqa: E402
import comfy.sample  # noqa: E402
import comfy.sd  # noqa: E402
import comfy.utils  # noqa: E402
import node_helpers  # noqa: E402
from comfy_extras.nodes_model_advanced import ModelSamplingAuraFlow  # noqa: E402


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


class SmiPoller(threading.Thread):
    def __init__(self, interval=0.5):
        super().__init__(daemon=True)
        self.interval = interval
        self.peak = 0
        self._stop = threading.Event()

    def run(self):
        import subprocess
        while not self._stop.is_set():
            try:
                out = subprocess.run(
                    ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
                    capture_output=True, text=True, timeout=5,
                ).stdout.strip().splitlines()
                for line in out:
                    try:
                        self.peak = max(self.peak, int(line.strip()))
                    except ValueError:
                        pass
            except Exception:
                pass
            self._stop.wait(self.interval)

    def stop(self):
        self._stop.set()


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
    print("=== ZIV.AI Lightning LoRA 8-step test ===")
    print("params: steps=%d cfg=%s sampler=%s scheduler=%s shift=%s seed=%d"
          % (STEPS, CFG, SAMPLER, SCHEDULER, SHIFT, SEED))
    print("lora   : strength_model=%s strength_clip=%s" % (strength_model, strength_clip))
    print("output :", OUT)

    t = time.time()
    model = comfy.sd.load_diffusion_model(DIT)
    clip = comfy.sd.load_clip([TE], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    vae = comfy.sd.VAE(comfy.utils.load_torch_file(VAE))
    load_s = time.time() - t
    print("model_load_s = %.3f  cond_stage_model=%s" % (load_s, type(clip.cond_stage_model).__name__))

    model = ModelSamplingAuraFlow().patch_aura(model, SHIFT)[0]

    # Load LoRA (step 3 of the task).
    t = time.time()
    lora = comfy.utils.load_torch_file(LORA, safe_load=True)
    print("lora_key_count = %d" % len(lora))
    model, clip = comfy.sd.load_lora_for_models(model, clip, lora, strength_model, strength_clip)
    lora_s = time.time() - t
    print("lora_load_s = %.3f" % lora_s)

    ms = model.get_model_object("model_sampling")
    print("patched model_sampling=%s shift=%s" % (type(ms).__name__, getattr(ms, "shift", None)))

    print("MEM_ALLOC_AFTER_LOAD_MB = %.1f" % (torch.cuda.memory_allocated() / 1048576))
    torch.cuda.reset_peak_memory_stats()

    poller = SmiPoller()
    poller.start()

    source = load_image_tensor(INPUT)
    positive, negative, latent = encode_edit(clip, vae, PROMPT, source, 1024, 640)
    print("edit latent_shape", tuple(latent.shape))

    samples, timing = run_sample(model, positive, negative, latent)
    t0 = time.time()
    image = to_pil(vae.decode(samples)[0])
    decode_s = time.time() - t0

    poller.stop()
    poller.join(timeout=2)

    image.save(OUT)

    print("--- result ---")
    print("output=%s size=%s bytes=%d" % (OUT, image.size, os.path.getsize(OUT)))
    print("model_load_s      = %.3f" % load_s)
    print("lora_load_s       = %.3f" % lora_s)
    print("moving_to_gpu_s   = %.3f" % timing["moving_to_gpu"])
    print("sampling_s        = %.3f" % timing["sampling"])
    print("sample_total_s    = %.3f" % timing["sample_total"])
    print("vae_decode_s      = %.3f" % decode_s)
    print("peak_alloc_MB     = %.1f" % (torch.cuda.max_memory_allocated() / 1048576))
    print("peak_reserved_MB  = %.1f" % (torch.cuda.max_memory_reserved() / 1048576))
    print("peak_nvidia_smi_MB= %d" % poller.peak)
    print("--- captured logging warnings (%d) ---" % len(_lora_logs))
    for msg in _lora_logs[:40]:
        print("    ", msg)

    stats = np.asarray(image).astype(np.float32)
    print("image_mean = %.4f  image_std = %.4f" % (stats.mean() / 255.0, stats.std() / 255.0))
    print("RESULT=DONE")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print("RESULT=FAIL err=%r" % (exc,))
        traceback.print_exc()
