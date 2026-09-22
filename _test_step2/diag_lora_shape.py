"""Confirm the LoRA/model dimension mismatch (root cause of zero effect).

Compares LoRA down/up shapes with the Qwen-Image-2.1 DiT weight shapes read
directly from the safetensors headers (no model load, no GPU).
"""

import os

from safetensors import safe_open

DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
LORA = r"C:\AI\ComfyUI_PIC\ComfyUI\models\loras\qwen_image2\Qwen-Image-Lightning-8steps-V2.0-bf16.safetensors"

with safe_open(DIT, framework="pt") as fm:
    mkeys = set(fm.keys())
    with safe_open(LORA, framework="pt") as fl:
        probes = [
            "transformer_blocks.0.attn.to_q",
            "transformer_blocks.0.attn.to_k",
            "transformer_blocks.0.attn.to_v",
            "transformer_blocks.0.attn.to_out.0",
        ]
        print("%-42s %-22s %-22s %s" % ("module", "model weight", "lora_down", "lora_up"))
        for p in probes:
            mk = "%s.weight" % p
            dk = "%s.lora_down.weight" % p
            uk = "%s.lora_up.weight" % p
            mshape = tuple(fm.get_slice(mk).get_shape()) if mk in mkeys else None
            dshape = tuple(fl.get_slice(dk).get_shape()) if dk in fl.keys() else None
            ushape = tuple(fl.get_slice(uk).get_shape()) if uk in fl.keys() else None
            print("%-42s %-22s %-22s %s" % (p, mshape, dshape, ushape))

        # Also show a LoRA key that has no model counterpart.
        print()
        print("LoRA-only family example (not in model):")
        for p in ["transformer_blocks.0.attn.add_q_proj", "transformer_blocks.0.img_mlp.net.0.proj"]:
            mk = "diffusion_model.%s.weight" % p
            dk = "%s.lora_down.weight" % p
            print("  %-46s model=%s lora_down=%s" % (
                p, mk in mkeys,
                tuple(fl.get_slice(dk).get_shape()) if dk in fl.keys() else None))

print("RESULT=DONE")
