"""Mask-feather experiment for ZIV.AI (read-only; no product code change).

Question: does Qwen-Image-2.1 accept a *grayscale* (soft) sampler noise mask, or
does it behave the same as a binary mask? The product edit path thresholds user
masks at 0.5 (`pipeline._load_mask_tensor(binary=True)`), so to probe the model we
call the existing `pipeline.run(..., mask_binary=False)` -- the same flag the
outpaint path already uses (not a code change).

Three masks, same region / image / prompt / seed / steps:
  A  binary   0 / 255
  B  gaussian sigma = 10
  C  gaussian sigma = 25

Outputs:
  _test_step2/mask_feather_{A,B,C}.png        (edited images)
  _test_step2/mask_feather_mask_{A,B,C}.png   (the masks actually fed in)

The result markdown is written by hand after inspecting the images.
"""

import json
import os
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER = r"D:\devlop\ZIV.AI\python\server"

# Resolution / sampling knobs via env only (config reads env at import).
os.environ.setdefault("ZIV_AI_RESOLUTION_MODE", "side")
os.environ.setdefault("ZIV_AI_RESOLUTION_SIDE", "1024")

sys.path.insert(0, SERVER)

import numpy as np
from PIL import Image, ImageFilter

import config  # noqa: E402
import model_loader  # noqa: E402
import pipeline  # noqa: E402
from resolution import _target_size_from_spec  # noqa: E402

SRC = os.path.join(HERE, "user_input.png")
OUT = {k: os.path.join(HERE, "mask_feather_%s.png" % k) for k in ("A", "B", "C")}
MASK = {k: os.path.join(HERE, "mask_feather_mask_%s.png" % k) for k in ("A", "B", "C")}
PROMPT = "Replace the masked region with a bright blue sky with white clouds."
STEPS = 25
SEED = 42
SIGMA = {"A": 0, "B": 10, "C": 25}


def log(*args):
    print(*args, flush=True)


def build_masks():
    with Image.open(SRC) as image:
        src_w, src_h = image.size
    spec = {"mode": config.RESOLUTION_MODE, "value": int(config.RESOLUTION_SIDE)}
    target_w, target_h = _target_size_from_spec(src_w, src_h, spec)
    log("source=%dx%d target=%dx%d spec=%s" % (src_w, src_h, target_w, target_h, spec))

    base = Image.new("L", (target_w, target_h), 0)
    x0, y0 = target_w // 4, target_h // 4
    x1, y1 = target_w - x0, target_h - y0
    px = base.load()
    for y in range(y0, y1):
        for x in range(x0, x1):
            px[x, y] = 255
    base.save(MASK["A"])

    for key in ("B", "C"):
        base.filter(ImageFilter.GaussianBlur(SIGMA[key])).save(MASK[key])

    for key in ("A", "B", "C"):
        arr = np.asarray(Image.open(MASK[key]).convert("L"))
        log("MASK %s sigma=%d min=%d max=%d unique=%d" % (
            key, SIGMA[key], int(arr.min()), int(arr.max()), len(np.unique(arr))))
    return target_w, target_h


def gpu_mib():
    try:
        import torch

        return round(torch.cuda.memory_allocated() / 1024 / 1024, 1)
    except Exception:
        return -1.0


def main():
    started = time.time()
    log("== loading models ==")
    model, td = model_loader.load_dit()
    log("load_dit %.1fs gpu_alloc_MiB=%s" % (td, gpu_mib()))
    clip, tc = model_loader.load_text_encoder()
    log("load_te %.1fs gpu_alloc_MiB=%s" % (tc, gpu_mib()))
    vae, tv = model_loader.load_vae()
    log("load_vae %.1fs gpu_alloc_MiB=%s" % (tv, gpu_mib()))

    build_masks()

    import torch

    results = {}
    for key in ("A", "B", "C"):
        # Prove the soft values actually reach the sampler (binary=False path).
        loaded = pipeline._load_mask_tensor(MASK[key], binary=False)
        log("LOADED %s binary=False shape=%s min=%.3f max=%.3f" % (
            key, tuple(loaded.shape), float(loaded.min()), float(loaded.max())))

        torch.cuda.reset_peak_memory_stats()
        request = {
            "prompt": PROMPT,
            "image_path": SRC,
            "mask_path": MASK[key],
            "output_path": OUT[key],
            "steps": STEPS,
            "seed": SEED,
            "denoise": 1.0,
        }
        log("== run %s mask=%s ==" % (key, MASK[key]))
        t = time.time()
        try:
            result = pipeline.run(model, clip, vae, request, mask_binary=False)
            elapsed = time.time() - t
            peak_alloc = round(torch.cuda.max_memory_allocated() / 1024 / 1024, 1)
            peak_reserved = round(torch.cuda.max_memory_reserved() / 1024 / 1024, 1)
            results[key] = {
                "ok": True,
                "output": result.get("output_path"),
                "width": result.get("width"),
                "height": result.get("height"),
                "duration_s": round(elapsed, 2),
                "pipeline_duration_ms": result.get("duration_ms"),
                "peak_alloc_MiB": peak_alloc,
                "peak_reserved_MiB": peak_reserved,
                "gpu_alloc_after_MiB": gpu_mib(),
            }
            log("RESULT %s OK out=%s %sx%s dur=%.1fs peak_alloc=%s peak_reserved=%s" % (
                key, result.get("output_path"), result.get("width"), result.get("height"),
                elapsed, peak_alloc, peak_reserved))
        except Exception as exc:  # noqa: BLE001
            results[key] = {"ok": False, "error": repr(exc)}
            log("RESULT %s FAIL %r" % (key, exc))
            traceback.print_exc()

    log("== SUMMARY ==")
    log(json.dumps(results, ensure_ascii=False, indent=2))
    log("TOTAL_s %.1f" % (time.time() - started))


if __name__ == "__main__":
    main()
