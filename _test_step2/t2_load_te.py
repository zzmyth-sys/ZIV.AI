import os, sys, time, traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
TE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors"

sys.path.insert(0, COMFY)
os.chdir(COMFY)

import torch
import comfy.sd
print("CLIPType.QWEN_IMAGE =", comfy.sd.CLIPType.QWEN_IMAGE)

t0 = time.time()
try:
    clip = comfy.sd.load_clip([TE], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    dt = time.time() - t0
    print("RESULT=OK elapsed_s=%.2f" % dt)
    print("clip_type", type(clip).__name__)
    csm = getattr(clip, "cond_stage_model", None)
    print("cond_stage_model", type(csm).__name__ if csm is not None else None)
    # tokenizer / model
    for attr in ("tokenizer", "model"):
        v = getattr(csm, attr, None) if csm is not None else None
        print("csm.%s" % attr, type(v).__name__ if v is not None else None)
    if torch.cuda.is_available():
        print("alloc_MB", round(torch.cuda.memory_allocated() / 1024 / 1024, 1))
        print("reserved_MB", round(torch.cuda.memory_reserved() / 1024 / 1024, 1))
except Exception as e:
    print("RESULT=FAIL elapsed_s=%.2f err=%r" % (time.time() - t0, e))
    traceback.print_exc()
