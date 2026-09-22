"""Check whether the loaded LoRA actually patches weights on the int8 model.

Verifies model.patches after load_lora_for_models and inspects the patch type
(for an int8 quantized DiT, the forward path may not read the patched weight).
"""

import os
import sys

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
LORA = r"C:\AI\ComfyUI_PIC\ComfyUI\models\loras\qwen_image2\Qwen-Image-Lightning-8steps-V2.0-bf16.safetensors"

os.chdir(COMFY)
sys.path.insert(0, COMFY)

import comfy.sd  # noqa: E402
import comfy.utils  # noqa: E402

model = comfy.sd.load_diffusion_model(DIT)
lora = comfy.utils.load_torch_file(LORA, safe_load=True)

print("--- WITHOUT lora ---")
print("patches_before =", len(model.patches))

model2, _ = comfy.sd.load_lora_for_models(model, None, lora, 1.0, 0.0)
print("patches_after =", len(model2.patches))
for k in list(model2.patches.keys())[:5]:
    entries = model2.patches[k]
    print(" ", k, "->", type(entries).__name__, "len=", len(entries))
    e0 = entries[0]
    print("     patch type =", type(e0).__name__, "attrs=", [a for a in dir(e0) if not a.startswith("_")])

print("--- object_patches on inner model ---")
inner = model2.model
op = getattr(inner, "object_patches", None)
print("object_patches =", None if op is None else len(op))
wp = getattr(inner, "weight_wrapper", None)
print("weight_wrapper =", type(wp).__name__ if wp is not None else None)

# Inspect the quant metadata on a matched weight.
sd = inner.state_dict()
probe = "diffusion_model.transformer_blocks.0.attn.to_q.weight"
probe_q = "diffusion_model.transformer_blocks.0.attn.to_q.comfy_quant"
print("probe weight dtype/shape =", sd[probe].dtype, tuple(sd[probe].shape))
print("comfy_quant present =", probe_q in sd)

print("--- weight adapter registry ---")
import comfy.weight_adapter as wa  # noqa: E402
print("adapters =", [c.__name__ for c in wa.adapters])

print("RESULT=DONE")
