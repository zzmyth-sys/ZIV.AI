"""180-degree equirectangular half-panorama validation (single inference, GPU).

One ``pipeline.run`` call. Explicit 2048x1024 (2:1; both are multiples of 16, so
no snap) - the standard ratio for a 180-degree equirectangular half panorama.

The prompt asks for the FRONT hemisphere only (180 horizontal x 180 vertical);
it must not say "360 degrees" or "seamless full sphere". The scene clause is
filled automatically from the deterministic WD scene filter (same data path as
the /360 A/B), never hand-tuned per image.

Writes `panorama_180.png` and `panorama_180_result.json`.
"""

import json
import os
import subprocess
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SERVER = os.path.join(REPO, "python", "server")

SRC = os.path.join(HERE, "WD", "360.jpg")
TAGS_JSON = os.path.join(HERE, "360_ab_tags.json")
OUT = os.path.join(HERE, "panorama_180.png")
RESULT = os.path.join(HERE, "panorama_180_result.json")
WIDTH = 2048
HEIGHT = 1024
MAX_PIXELS = 4700000
STEPS = 30
SEED = 42
DENOISE = 1.0


def scene_clause():
    with open(TAGS_JSON, encoding="utf-8") as handle:
        scene = json.load(handle)["scene"]
    return ", ".join(scene)


def build_prompt(scene):
    return (
        "Generate a 180-degree equirectangular panorama (front hemisphere only, "
        "2:1 aspect ratio) from the input perspective image. "
        "The scene is %s. "
        "Use a true equirectangular projection covering 180 degrees horizontal "
        "and 180 degrees vertical. The left and right edges should be at the "
        "90-degree-left and 90-degree-right extremes of the front hemisphere."
        % scene
    )


def log(*args):
    print(*args, flush=True)


def smi_used_mb():
    try:
        raw = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            stderr=subprocess.DEVNULL, timeout=5,
        )
        return int(raw.decode("utf-8", "ignore").strip().splitlines()[0].strip())
    except Exception:
        return None


def edge_metrics(path):
    import numpy as np
    from PIL import Image

    with Image.open(path) as image:
        arr = np.asarray(image.convert("RGB")).astype(np.int16)
    lr = float(np.mean(np.abs(arr[:, 0] - arr[:, -1])))
    tb = float(np.mean(np.abs(arr[0, :] - arr[-1, :])))
    return round(lr, 3), round(tb, 3)


def main():
    scene = scene_clause()
    prompt = build_prompt(scene)

    log("== plan ==")
    log("  source : %s" % os.path.relpath(SRC, HERE))
    log("  target : %dx%d (explicit, max_pixels=%d)" % (WIDTH, HEIGHT, MAX_PIXELS))
    log("  steps/seed/denoise: %d / %d / %.1f" % (STEPS, SEED, DENOISE))
    log("  scene  : %s" % scene)
    log("  smi_before_mb: %s" % smi_used_mb())
    log("  prompt : %s" % prompt)

    sys.path.insert(0, SERVER)
    import model_loader  # noqa: E402
    import pipeline  # noqa: E402

    log("== loading models ==")
    model, _ = model_loader.load_dit()
    clip, _ = model_loader.load_text_encoder()
    vae, _ = model_loader.load_vae()

    import torch

    torch.cuda.reset_peak_memory_stats()
    request = {
        "prompt": prompt,
        "image_path": SRC,
        "output_path": OUT,
        "steps": STEPS,
        "seed": SEED,
        "denoise": DENOISE,
        "resolution": {
            "mode": "explicit",
            "width": WIDTH,
            "height": HEIGHT,
            "max_pixels": MAX_PIXELS,
        },
    }

    log("== run ==")
    t0 = time.time()
    try:
        result = pipeline.run(model, clip, vae, request)
    except Exception as exc:  # noqa: BLE001
        traceback.print_exc()
        log(json.dumps({"ok": False, "error": repr(exc)}))
        sys.exit(1)

    summary = {
        "ok": True,
        "source": os.path.relpath(SRC, HERE),
        "output": OUT,
        "width": result.get("width"),
        "height": result.get("height"),
        "scene": scene,
        "prompt": prompt,
        "steps": STEPS,
        "seed": SEED,
        "denoise": DENOISE,
        "wall_s": round(time.time() - t0, 2),
        "pipeline_ms": result.get("duration_ms"),
        "peak_alloc_mb": round(torch.cuda.max_memory_allocated() / (1024.0 ** 2), 1),
        "smi_used_mb": smi_used_mb(),
    }
    lr, tb = edge_metrics(OUT)
    summary["seam_lr"] = lr
    summary["seam_tb"] = tb

    with open(RESULT, "w", encoding="utf-8") as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=2)

    log("  %s" % json.dumps(summary, ensure_ascii=False))
    log("wrote", OUT)
    log("wrote", RESULT)


if __name__ == "__main__":
    main()
