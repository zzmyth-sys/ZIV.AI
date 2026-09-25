"""WD A/B experiment - stage 2: run the diffusion edit for every batch/variant.

Fixed: same source image / seed / steps / resolution across variants; only the
prompt (A1 all-tags / A2 cleaned-tags / B LLM-rewrite / C no-tags) differs.
Outputs `_test_step2/wd_ab_<batch>_<variant>.png` and `wd_ab_result.json`.

Usage (embedded python):
  & "D:\\devlop\\ZIV.AI\\Comfyui\\python_embeded\\python.exe" _test_step2\\wd_ab.py
"""

import json
import os
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SERVER = os.path.join(REPO, "python", "server")
SRC_DIR = os.path.join(HERE, "wd_ab_src")

os.environ.setdefault("ZIV_AI_RESOLUTION_MODE", "side")
os.environ.setdefault("ZIV_AI_RESOLUTION_SIDE", "1024")

sys.path.insert(0, SERVER)

import model_loader  # noqa: E402
import pipeline  # noqa: E402

STEPS = 30
SEED = 42
DENOISE = 1.0
VARIANTS = {
    "batch1": ["A1", "A2", "B", "C"],
    "batch1b": ["A1", "A2", "B"],
    "batch2": ["A1", "B"],
}


def log(*args):
    print(*args, flush=True)


def run_once(model, clip, vae, prompt, image_path, additional, out_path):
    request = {
        "prompt": prompt,
        "image_path": image_path,
        "output_path": out_path,
        "steps": STEPS,
        "seed": SEED,
        "denoise": DENOISE,
    }
    if additional:
        request["additional_images"] = additional
    t0 = time.time()
    try:
        result = pipeline.run(model, clip, vae, request)
        return {
            "ok": True,
            "output": result.get("output_path"),
            "width": result.get("width"),
            "height": result.get("height"),
            "wall_s": round(time.time() - t0, 2),
            "pipeline_ms": result.get("duration_ms"),
        }
    except Exception as exc:  # noqa: BLE001
        traceback.print_exc()
        return {"ok": False, "error": repr(exc), "wall_s": round(time.time() - t0, 2)}


def main():
    with open(os.path.join(HERE, "wd_ab_prompts.json"), encoding="utf-8") as handle:
        prompts = json.load(handle)

    plan = []
    for batch, variants in VARIANTS.items():
        data = prompts[batch]
        subject = os.path.join(SRC_DIR, data["subject_image"])
        additional = [os.path.join(SRC_DIR, data["reference"])] if data.get("reference") else []
        for variant in variants:
            plan.append({
                "batch": batch,
                "variant": variant,
                "prompt": data[variant],
                "image": subject,
                "additional": additional,
                "out": os.path.join(HERE, "wd_ab_%s_%s.png" % (batch, variant)),
            })

    log("== plan ==")
    for item in plan:
        log("  %-8s %-3s ref=%s prompt=%d chars" % (
            item["batch"], item["variant"], bool(item["additional"]), len(item["prompt"])))

    log("== loading models ==")
    model, _ = model_loader.load_dit()
    clip, _ = model_loader.load_text_encoder()
    vae, _ = model_loader.load_vae()

    log("== warmup (discarded) ==")
    run_once(model, clip, vae, plan[0]["prompt"], plan[0]["image"], plan[0]["additional"],
             os.path.join(HERE, "wd_ab_warmup.png"))

    runs = []
    for item in plan:
        log("== %s / %s ==" % (item["batch"], item["variant"]))
        r = run_once(model, clip, vae, item["prompt"], item["image"], item["additional"], item["out"])
        r.update({"batch": item["batch"], "variant": item["variant"], "prompt": item["prompt"]})
        log("  %s" % json.dumps(r, ensure_ascii=False))
        runs.append(r)

    summary = {"steps": STEPS, "seed": SEED, "denoise": DENOISE, "runs": runs}
    with open(os.path.join(HERE, "wd_ab_result.json"), "w", encoding="utf-8") as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=2)
    log("wrote", os.path.join(HERE, "wd_ab_result.json"))


if __name__ == "__main__":
    main()
