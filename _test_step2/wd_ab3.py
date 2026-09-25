"""WD A/B round 3 - stage 1: run the diffusion edit for A (short) vs B (WD tags).

One triple-image request per variant: main = person (fig3), additional = [outfit
(fig1), scene (fig2)] in IMAGE_ORDER. Same seed / steps / resolution for A and B;
only the prompt differs. 2 GPU inferences total (plus one discarded warmup).

Outputs `_test_step2/wd_ab3_A.png`, `wd_ab3_B.png`, `wd_ab3_result.json`.
"""

import json
import os
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SERVER = os.path.join(REPO, "python", "server")
SRC_DIR = os.path.join(HERE, "wd_ab3_src")

os.environ.setdefault("ZIV_AI_RESOLUTION_MODE", "side")
os.environ.setdefault("ZIV_AI_RESOLUTION_SIDE", "1024")

sys.path.insert(0, SERVER)

import model_loader  # noqa: E402
import pipeline  # noqa: E402

STEPS = 30
SEED = 42
DENOISE = 1.0
VARIANTS = ["A", "B"]


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
    key = sys.argv[1] if len(sys.argv) > 1 else "round3"
    with open(os.path.join(HERE, "wd_ab3_prompts.json"), encoding="utf-8") as handle:
        prompts = json.load(handle)[key]
    tag = prompts["tag"]

    subject = os.path.join(SRC_DIR, prompts["subject_image"])
    additional = [os.path.join(SRC_DIR, name) for name in prompts["additional"]]

    plan = []
    for variant in VARIANTS:
        plan.append({
            "variant": variant,
            "prompt": prompts[variant],
            "image": subject,
            "additional": additional,
            "out": os.path.join(HERE, "%s_%s.png" % (tag, variant)),
        })

    log("== plan ==")
    log("  key  : %s (tag %s)" % (key, tag))
    log("  order: %s" % prompts["image_order"])
    log("  main : %s" % prompts["subject_image"])
    log("  extra: %s" % prompts["additional"])
    for item in plan:
        log("  %-2s prompt=%d chars" % (item["variant"], len(item["prompt"])))

    log("== loading models ==")
    model, _ = model_loader.load_dit()
    clip, _ = model_loader.load_text_encoder()
    vae, _ = model_loader.load_vae()

    log("== warmup (discarded) ==")
    run_once(model, clip, vae, plan[0]["prompt"], plan[0]["image"], plan[0]["additional"],
             os.path.join(HERE, "%s_warmup.png" % tag))

    runs = []
    for item in plan:
        log("== %s ==" % item["variant"])
        r = run_once(model, clip, vae, item["prompt"], item["image"], item["additional"], item["out"])
        r.update({"variant": item["variant"], "prompt": item["prompt"]})
        log("  %s" % json.dumps(r, ensure_ascii=False))
        runs.append(r)

    summary = {"steps": STEPS, "seed": SEED, "denoise": DENOISE, "runs": runs}
    with open(os.path.join(HERE, "%s_result.json" % tag), "w", encoding="utf-8") as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=2)
    log("wrote", os.path.join(HERE, "%s_result.json" % tag))


if __name__ == "__main__":
    main()
