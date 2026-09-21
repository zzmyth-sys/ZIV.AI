import os, sys, time, gc, traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
TE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors"
VAE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\vae\qwen_image_2.1_vae_bf16.safetensors"

sys.path.insert(0, COMFY)
os.chdir(COMFY)

import torch
import comfy.sd, comfy.utils, comfy.sample, comfy.samplers
import comfy.model_management as mm
from comfy_extras.nodes_flux import get_schedule

RES = 256
STEPS = 4

def mb(x):
    return round(x / 1024 / 1024, 1)

def nv_used():
    try:
        import subprocess
        o = subprocess.check_output(["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"], text=True)
        return o.strip().splitlines()[0]
    except Exception:
        return "?"

def run_once(i):
    t0 = time.time()
    model = comfy.sd.load_diffusion_model(DIT)
    clip = comfy.sd.load_clip([TE], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    vae = comfy.sd.VAE(comfy.utils.load_torch_file(VAE))
    t_load = time.time() - t0

    positive = clip.encode_from_tokens_scheduled(clip.tokenize("a cat", keep_vision=True, prevent_empty_text=True))
    negative = clip.encode_from_tokens_scheduled(clip.tokenize("", keep_vision=True, prevent_empty_text=True))
    latent = {"samples": torch.zeros([1, 64, RES // 16, RES // 16], device=mm.intermediate_device())}

    seq_len = (RES * RES) // 256
    sigmas = torch.tensor(get_schedule(STEPS, seq_len), dtype=torch.float32)
    guider = comfy.samplers.CFGGuider(model)
    guider.set_conds(positive, negative)
    guider.set_cfg(1.0)
    sampler = comfy.samplers.sampler_object("euler")
    noise = comfy.sample.prepare_noise(latent["samples"], 42)

    t1 = time.time()
    samples = guider.sample(noise, latent["samples"], sampler, sigmas, disable_pbar=True, seed=42)
    decoded = vae.decode(samples)
    t_sample = time.time() - t1

    alloc = mb(torch.cuda.memory_allocated())
    reserved = mb(torch.cuda.memory_reserved())
    nv = nv_used()
    print("ITER %d load_s=%.2f sample_s=%.2f alloc_MB=%s reserved_MB=%s nvidia_used_MB=%s" % (i, t_load, t_sample, alloc, reserved, nv))

    del model, clip, vae, samples, decoded, positive, negative, guider, sampler, noise, sigmas, latent
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.empty_cache()
        torch.cuda.ipc_collect()
    alloc2 = mb(torch.cuda.memory_allocated())
    reserved2 = mb(torch.cuda.memory_reserved())
    nv2 = nv_used()
    print("AFTER_FREE %d alloc_MB=%s reserved_MB=%s nvidia_used_MB=%s" % (i, alloc2, reserved2, nv2))
    return {"iter": i, "load_s": round(t_load, 2), "sample_s": round(t_sample, 2),
            "alloc": alloc, "reserved": reserved, "nv": nv, "alloc2": alloc2, "reserved2": reserved2, "nv2": nv2}

try:
    print("BASELINE nvidia_used_MB=%s alloc_MB=%s" % (nv_used(), mb(torch.cuda.memory_allocated())))
    results = []
    for i in range(1, 4):
        results.append(run_once(i))
    print("SUMMARY")
    for r in results:
        print(r)
except Exception as e:
    print("RESULT=FAIL err=%r" % e)
    traceback.print_exc()
