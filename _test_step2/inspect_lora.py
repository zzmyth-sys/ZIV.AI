"""Static inspection of the Lightning LoRA safetensors (Step 4 follow-up).

Reads metadata / key names / shapes and the tiny ``.alpha`` scalars via torch
(safetensors bfloat16 is not understood by numpy). Read-only.
"""

import json
import os

import torch
from safetensors import safe_open

LORA = r"C:\AI\ComfyUI_PIC\ComfyUI\models\loras\qwen_image2\Qwen-Image-Lightning-8steps-V2.0-bf16.safetensors"

print("path =", LORA)
print("bytes =", os.path.getsize(LORA))

with safe_open(LORA, framework="pt") as f:
    meta = f.metadata()
    keys = list(f.keys())
    print("metadata =", json.dumps(meta, ensure_ascii=False) if meta else None)
    print("key_count =", len(keys))

    alpha_keys = [k for k in keys if k.endswith(".alpha")]
    down_keys = [k for k in keys if k.endswith(".lora_down.weight")]
    up_keys = [k for k in keys if k.endswith(".lora_up.weight")]
    print("alpha_count =", len(alpha_keys))
    print("lora_down_count =", len(down_keys))
    print("lora_up_count =", len(up_keys))
    print("other_count =", len(keys) - len(alpha_keys) - len(down_keys) - len(up_keys))

    values = []
    for k in alpha_keys:
        t = f.get_tensor(k)
        values.append((k, float(t.reshape(-1)[0].float())))

    uniq = {}
    for k, v in values:
        uniq[v] = uniq.get(v, 0) + 1
    print("alpha_unique_values =", json.dumps(uniq, ensure_ascii=False))
    print("alpha_min =", min(v for _, v in values), "alpha_max =", max(v for _, v in values))

    print("--- sample alpha entries (first 10) ---")
    for k, v in values[:10]:
        print("    %-70s alpha=%s" % (k, v))

    print("--- sample rank/shape (first 8 module pairs) ---")
    seen = set()
    count = 0
    for k in down_keys:
        stem = k[: -len(".lora_down.weight")]
        if stem in seen:
            continue
        seen.add(stem)
        d = f.get_tensor(k)
        u = f.get_tensor(stem + ".lora_up.weight")
        a = f.get_tensor(stem + ".alpha")
        print("    %-62s down=%s up=%s alpha=%s"
              % (stem, tuple(d.shape), tuple(u.shape), float(a.reshape(-1)[0].float())))
        count += 1
        if count >= 8:
            break

    # rank summary
    ranks = {}
    for k in down_keys:
        d = f.get_tensor(k)
        r = int(d.shape[0])
        ranks[r] = ranks.get(r, 0) + 1
    print("down_rank_distribution =", json.dumps(ranks, ensure_ascii=False))

print("RESULT=DONE")
