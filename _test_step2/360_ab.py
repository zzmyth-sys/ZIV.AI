"""WD scene-tag A/B for /360 - stage 1: run the two inferences (GPU).

Exactly TWO ``pipeline.run`` calls (A, then B) - no discarded warmup - per the
task brief. Same seed / steps / explicit 1920x1080 (16:9) resolution; only the
prompt differs (B = A + scene tags).

The explicit resolution is passed straight to the pipeline, bypassing the
CommandParser (an Edit-handler command has no explicit-resolution channel today).
The main image is encoded at the explicit target size as a reference latent.

Writes `360_ab_A.png`, `360_ab_B.png`, `360_ab_result.json`.
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
WIDTH = 1920
HEIGHT = 1080
MAX_PIXELS = 4700000
STEPS = 30
SEED = 42
DENOISE = 1.0
VARIANTS = ["A", "B"]

sys.path.insert(0, SERVER)

import model_loader  # noqa: E402
import pipeline  # noqa: E402


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


def run_once(model, clip, vae, prompt, out_path):
    import torch

    torch.cuda.reset_peak_memory_stats()
    request = {
        "prompt": prompt,
        "image_path": SRC,
        "output_path": out_path,
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
    t0 = time.time()
    try:
        result = pipeline.run(model, clip, vae, request)
        peak_alloc = round(torch.cuda.max_memory_allocated() / (1024.0 ** 2), 1)
        return {
            "ok": True,
            "output": result.get("output_path"),
            "width": result.get("width"),
            "height": result.get("height"),
            "wall_s": round(time.time() - t0, 2),
            "pipeline_ms": result.get("duration_ms"),
            "peak_alloc_mb": peak_alloc,
            "smi_used_mb": smi_used_mb(),
        }
    except Exception as exc:  # noqa: BLE001
        traceback.print_exc()
        return {"ok": False, "error": repr(exc), "wall_s": round(time.time() - t0, 2)}


def main():
    with open(os.path.join(HERE, "360_ab_prompts.json"), encoding="utf-8") as handle:
        prompts = json.load(handle)

    log("== plan ==")
    log("  source : %s" % os.path.relpath(SRC, HERE))
    log("  target : %dx%d (explicit, max_pixels=%d)" % (WIDTH, HEIGHT, MAX_PIXELS))
    log("  steps/seed/denoise: %d / %d / %.1f" % (STEPS, SEED, DENOISE))
    log("  scene  : %s" % ", ".join(prompts["scene_tags"]))
    log("  smi_before_mb: %s" % smi_used_mb())

    log("== loading models ==")
    model, _ = model_loader.load_dit()
    clip, _ = model_loader.load_text_encoder()
    vae, _ = model_loader.load_vae()

    runs = []
    for variant in VARIANTS:
        out_path = os.path.join(HERE, "360_ab_%s.png" % variant)
        log("== %s == (%d chars)" % (variant, len(prompts[variant])))
        r = run_once(model, clip, vae, prompts[variant], out_path)
        r.update({"variant": variant, "prompt": prompts[variant], "output": out_path})
        log("  %s" % json.dumps(r, ensure_ascii=False))
        runs.append(r)

    summary = {
        "source": os.path.relpath(SRC, HERE),
        "width": WIDTH,
        "height": HEIGHT,
        "steps": STEPS,
        "seed": SEED,
        "denoise": DENOISE,
        "scene_tags": prompts["scene_tags"],
        "runs": runs,
    }
    with open(os.path.join(HERE, "360_ab_result.json"), "w", encoding="utf-8") as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=2)
    log("wrote", os.path.join(HERE, "360_ab_result.json"))


if __name__ == "__main__":
    main()
