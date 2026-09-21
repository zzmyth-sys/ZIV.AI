import os, sys, time, traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"

sys.path.insert(0, COMFY)
os.chdir(COMFY)

import torch
print("torch", torch.__version__, "cuda_avail", torch.cuda.is_available())
if torch.cuda.is_available():
    print("gpu", torch.cuda.get_device_name(0))

import comfy.sd
import comfy.model_management as mm
print("torch_device", mm.get_torch_device())

t0 = time.time()
try:
    model = comfy.sd.load_diffusion_model(DIT)
    dt = time.time() - t0
    print("RESULT=OK elapsed_s=%.2f" % dt)
    print("model_type", type(model).__name__)
    inner = getattr(model, "model", None)
    print("inner_model_type", type(inner).__name__ if inner is not None else None)
    dm = getattr(inner, "diffusion_model", None)
    print("diffusion_model", type(dm).__name__ if dm is not None else None)
    if dm is not None:
        try:
            n = sum(p.numel() for p in dm.parameters())
            print("params", n)
        except Exception as e:
            print("params_err", repr(e))
    if torch.cuda.is_available():
        print("alloc_MB", round(torch.cuda.memory_allocated() / 1024 / 1024, 1))
        print("reserved_MB", round(torch.cuda.memory_reserved() / 1024 / 1024, 1))
    print("model_options_keys", list(getattr(model, "model_options", {}).keys()))
    print("model_type_attr", getattr(model, "model_type", None))
except Exception as e:
    print("RESULT=FAIL elapsed_s=%.2f err=%r" % (time.time() - t0, e))
    traceback.print_exc()
