"""Precisely quantify Lightning LoRA key matching against Qwen-Image-2.1 DiT.

Loads the DiT on CPU, builds the comfy lora key map, and reports how many LoRA
modules match / miss, plus the resulting patch_dict size.
"""

import logging
import os
import sys

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
LORA = r"C:\AI\ComfyUI_PIC\ComfyUI\models\loras\qwen_image2\Qwen-Image-Lightning-8steps-V2.0-bf16.safetensors"

os.chdir(COMFY)
sys.path.insert(0, COMFY)

import comfy.lora  # noqa: E402
import comfy.sd  # noqa: E402
import comfy.utils  # noqa: E402

model = comfy.sd.load_diffusion_model(DIT)
key_map = comfy.lora.model_lora_keys_unet(model.model, {})
print("model_lora_key_map_count =", len(key_map))
print("has transformer_blocks.0.attn.to_q :", "transformer_blocks.0.attn.to_q" in key_map)
print("has transformer_blocks.0.attn.to_k :", "transformer_blocks.0.attn.to_k" in key_map)
print("has transformer_blocks.0.attn.add_q_proj :", "transformer_blocks.0.attn.add_q_proj" in key_map)
print("has transformer_blocks.0.img_mlp.net.0.proj :", "transformer_blocks.0.img_mlp.net.0.proj" in key_map)

lora = comfy.utils.load_torch_file(LORA, safe_load=True)

stems = set()
for k in lora:
    if k.endswith(".lora_down.weight"):
        stems.add(k[: -len(".lora_down.weight")])
    elif k.endswith(".lora_up.weight"):
        stems.add(k[: -len(".lora_up.weight")])

matched = sorted(s for s in stems if s in key_map)
missing = sorted(s for s in stems if s not in key_map)
print("lora_module_stems =", len(stems))
print("matched_stems =", len(matched))
print("missing_stems =", len(missing))
print("matched sample =", matched[:8])
print("missing sample =", missing[:8])

# Group missing by key suffix family.
fam = {}
for s in missing:
    parts = s.split(".")
    tail = ".".join(parts[3:]) if len(parts) > 3 else s
    fam[tail] = fam.get(tail, 0) + 1
print("missing_families =", fam)

missing_fam = {}
for s in matched:
    parts = s.split(".")
    tail = ".".join(parts[3:]) if len(parts) > 3 else s
    missing_fam[tail] = missing_fam.get(tail, 0) + 1
print("matched_families =", missing_fam)

patch_dict = comfy.lora.load_lora(lora, key_map, log_missing=False)
print("loaded_patch_dict_size =", len(patch_dict))
print("patch_dict_sample_keys =", list(patch_dict.keys())[:8])

print("RESULT=DONE")
