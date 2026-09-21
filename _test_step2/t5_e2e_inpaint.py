import os, sys, time, math, traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
TE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors"
VAE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\vae\qwen_image_2.1_vae_bf16.safetensors"
HERE = r"D:\devlop\ZIV.AI\_test_step2"

RES = int(sys.argv[1]) if len(sys.argv) > 1 else 512
STEPS = int(sys.argv[2]) if len(sys.argv) > 2 else 20
MODE = sys.argv[3] if len(sys.argv) > 3 else "edit"  # edit | t2i

sys.path.insert(0, COMFY)
os.chdir(COMFY)

import torch
from PIL import Image
import numpy as np
import comfy.sd
import comfy.utils
import comfy.sample
import comfy.samplers
import comfy.model_management as mm
import node_helpers
from comfy_extras.nodes_flux import get_schedule

def mb(x):
    return round(x / 1024 / 1024, 1)

def gpu_used():
    try:
        return round(torch.cuda.memory_allocated() / 1024 / 1024, 1)
    except Exception:
        return -1

def make_test_image(path, size):
    img = Image.new("RGB", (size, size), (30, 60, 120))
    px = img.load()
    for y in range(size):
        for x in range(size):
            if (x - size // 3) ** 2 + (y - size // 2) ** 2 < (size // 5) ** 2:
                px[x, y] = (220, 180, 60)
    img.save(path)
    return path

def pil_to_tensor(img):
    arr = np.asarray(img.convert("RGB")).astype(np.float32) / 255.0
    return torch.from_numpy(arr)[None, ...]  # [1,H,W,3]

def tensor_to_pil(t):
    a = t.detach().cpu().float().clamp(0, 1).numpy()
    a = (a * 255.0).round().astype(np.uint8)
    return Image.fromarray(a)

def encode_qwen21(clip, vae, prompt, negative_prompt, images=None, resolution=1024):
    ref_latents = []
    images_vl = []
    images = images or {}
    latent_w = latent_h = resolution or 1024
    for name in sorted(images, key=lambda n: int(n.rsplit("_", 1)[-1])):
        image = images[name]
        if image is None:
            continue
        samples = image[:1].movedim(-1, 1)  # [1,3,H,W]
        if resolution > 0:
            ratio = samples.shape[3] / samples.shape[2]
            width = round(math.sqrt(resolution * resolution * ratio) / 32) * 32
            height = round(math.sqrt(resolution * resolution / ratio) / 32) * 32
        else:
            width, height = round(samples.shape[3] / 32) * 32, round(samples.shape[2] / 32) * 32
        width, height = max(32, width), max(32, height)
        if (width, height) == (samples.shape[3], samples.shape[2]):
            s = image[:1]
        else:
            s = comfy.utils.common_upscale(samples, width, height, "lanczos", "disabled").movedim(1, -1)
        if not images_vl:
            latent_w, latent_h = width, height
        rgb = s[:, :, :, :3]
        if s.shape[-1] > 3:
            rgb = rgb * s[:, :, :, 3:] + (1.0 - s[:, :, :, 3:])
        images_vl.append(rgb)
        if vae is not None:
            ref_latents.append(vae.encode(s))
    keep_vision = len(ref_latents) == 0
    positive = clip.encode_from_tokens_scheduled(clip.tokenize(prompt, images=images_vl, keep_vision=keep_vision, prevent_empty_text=True))
    negative = clip.encode_from_tokens_scheduled(clip.tokenize(negative_prompt, images=images_vl, keep_vision=keep_vision, prevent_empty_text=True))
    if len(ref_latents) > 0:
        positive = node_helpers.conditioning_set_values(positive, {"reference_latents": ref_latents}, append=True)
        negative = node_helpers.conditioning_set_values(negative, {"reference_latents": ref_latents}, append=True)
    latent = torch.zeros([1, 64, latent_h // 16, latent_w // 16], device=mm.intermediate_device())
    return positive, negative, {"samples": latent}

t_all = time.time()
phases = {}

def phase(name, t0):
    phases[name] = round(time.time() - t0, 2)
    print("PHASE %s = %.2fs (gpu_alloc_MB=%s)" % (name, phases[name], gpu_used()))

try:
    print("MODE=%s RES=%d STEPS=%d" % (MODE, RES, STEPS))
    t = time.time(); model = comfy.sd.load_diffusion_model(DIT); phase("load_dit", t)
    t = time.time(); clip = comfy.sd.load_clip([TE], clip_type=comfy.sd.CLIPType.QWEN_IMAGE); phase("load_te", t)
    t = time.time(); vsd = comfy.utils.load_torch_file(VAE); vae = comfy.sd.VAE(vsd); phase("load_vae", t)

    if MODE == "edit":
        src_path = make_test_image(os.path.join(HERE, "input_test_%d.png" % RES), RES)
        src = Image.open(src_path)
        ref = pil_to_tensor(src)
        images = {"image_1": ref}
        prompt = "Make the background a snowy mountain landscape, keep the subject unchanged."
        negative = ""
        resolution = RES
    else:
        images = {}
        prompt = "A serene snowy mountain landscape at sunrise, highly detailed."
        negative = ""
        resolution = RES

    t = time.time()
    positive, negative, latent = encode_qwen21(clip, vae, prompt, negative, images=images, resolution=resolution)
    phase("encode", t)
    print("latent_shape", tuple(latent["samples"].shape))

    seq_len = (latent["samples"].shape[2] * latent["samples"].shape[3] * 256) // 256  # h*w (latent*16)
    seq_len = (latent["samples"].shape[2] * 16 * latent["samples"].shape[3] * 16) // 256
    sigmas = get_schedule(STEPS, seq_len)
    sigmas = torch.tensor(sigmas, dtype=torch.float32)
    print("sigmas_len", len(sigmas), "seq_len", seq_len)

    guider = comfy.samplers.CFGGuider(model)
    guider.set_conds(positive, negative)
    guider.set_cfg(1.0)
    sampler = comfy.samplers.sampler_object("euler")
    seed = 42
    noise = comfy.sample.prepare_noise(latent["samples"], seed)

    steps_seen = []
    def cb(step, x0, x, total_steps):
        steps_seen.append((step, total_steps))
        print("CALLBACK step=%d/%d" % (step, total_steps))
        return {}

    t = time.time()
    samples = guider.sample(noise, latent["samples"], sampler, sigmas, disable_pbar=True, seed=seed, callback=cb)
    phase("sample", t)
    print("samples_shape", tuple(samples.shape))
    print("callbacks_seen", len(steps_seen))

    t = time.time()
    decoded = vae.decode(samples)
    phase("vae_decode", t)
    print("decoded_shape", tuple(decoded.shape))

    out = os.path.join(HERE, "output_e2e_%s_%d.png" % (MODE, RES))
    img = tensor_to_pil(decoded[0])
    img.save(out)
    print("RESULT=OK output=%s size=%s" % (out, img.size))
    print("PHASES", phases)
    print("TOTAL_s", round(time.time() - t_all, 2))
    if torch.cuda.is_available():
        print("peak_alloc_MB", mb(torch.cuda.max_memory_allocated()))
        print("peak_reserved_MB", mb(torch.cuda.max_memory_reserved()))
except Exception as e:
    print("RESULT=FAIL err=%r" % e)
    traceback.print_exc()
    print("PHASES", phases)
