"""Diagnose why Lightning LoRA keys are "not loaded" (key-name mismatch).

Loads the DiT on CPU only (no GPU sampling) and compares the model's
state_dict keys with the LoRA keys and the comfy.lora key map.
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

print("loading DiT (cpu) ...")
model = comfy.sd.load_diffusion_model(DIT)
inner = getattr(model, "model", model)
sd_keys = list(inner.state_dict().keys()) if hasattr(inner, "state_dict") else list(model.model.diffusion_model.state_dict().keys())
print("inner_type =", type(inner).__name__)
print("model_state_key_count =", len(sd_keys))
print("--- model keys sample (first 30) ---")
for k in sd_keys[:30]:
    print("   ", k)

print("--- model keys containing 'to_q' (first 10) ---")
for k in [x for x in sd_keys if "to_q" in x][:10]:
    print("   ", k)

print("--- model keys containing 'transformer' (first 10) ---")
for k in [x for x in sd_keys if "transformer" in x][:10]:
    print("   ", k)

try:
    import comfy.lora
    key_map = comfy.lora.model_lora_keys_unet(model, {})
    print("model_lora_key_map_count =", len(key_map))
    print("--- key_map sample (first 20) ---")
    for k in list(key_map.keys())[:20]:
        print("   ", k)
    probe = "transformer_blocks.0.attn.to_q.weight"
    print("probe in key_map:", probe in key_map)
    probes = [k for k in key_map if "to_q" in k][:10]
    print("key_map 'to_q' entries (first 10):")
    for k in probes:
        print("   ", k)
except Exception as exc:
    print("model_lora_keys_unet failed:", repr(exc))

lora = comfy.utils.load_torch_file(LORA, safe_load=True)
print("lora_key_count =", len(lora))
print("--- lora keys sample (first 12) ---")
for k in list(lora.keys())[:12]:
    print("   ", k)

print("RESULT=DONE")
