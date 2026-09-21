import os, sys, time, traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
VAE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\vae\qwen_image_2.1_vae_bf16.safetensors"

sys.path.insert(0, COMFY)
os.chdir(COMFY)

import torch
import comfy.utils
import comfy.sd

t0 = time.time()
try:
    sd = comfy.utils.load_torch_file(VAE)
    t_load = time.time() - t0
    print("state_dict_keys", len(sd))
    t1 = time.time()
    vae = comfy.sd.VAE(sd)
    print("RESULT=OK load_s=%.2f vae_init_s=%.2f" % (t_load, time.time() - t1))
    print("vae_type", type(vae).__name__)
    print("latent_format", type(getattr(vae, "latent_format", None)).__name__)
    lf = getattr(vae, "latent_format", None)
    if lf is not None:
        print("latent_channels", getattr(lf, "latent_channels", None))
        print("latent_dim", getattr(lf, "latent_dim", None))
    if torch.cuda.is_available():
        print("alloc_MB", round(torch.cuda.memory_allocated() / 1024 / 1024, 1))
except Exception as e:
    print("RESULT=FAIL elapsed_s=%.2f err=%r" % (time.time() - t0, e))
    traceback.print_exc()
