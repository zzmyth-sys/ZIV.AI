"""ZIV.AI repro: Viggle 6-step + /换背景 (multi) + side 1536 -> VRAM / offset.

Runs the PUBLISHED backend code (same hashes as repo) through the real pipeline with
heavy instrumentation:
  - VRAM probe on every stage (ZIV_AI_VRAM_PROBE=1)
  - encoded latent + reference-latent shapes
  - which sampler path the before_sample seam chose (custom sigmas vs legacy 40-step)
  - viggle enable state

Not a unit test; a one-shot diagnostic. User-authorized GPU run.
"""

import os
import sys

HOME = os.environ.get("REPRO_HOME", r"D:\devlop\ZIV.AI")
SERVER = os.path.join(HOME, "python", "server")

os.environ["ZIV_AI_COMFY_ROOT"] = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
os.environ["ZIV_AI_PLUGINS_REGISTRY"] = os.path.join(HOME, "Template", "plugins.json")
os.environ["ZIV_AI_PLUGINS_BASE_DIR"] = HOME
os.environ["ZIV_AI_PLUGIN_QWEN21_VIGGLE_6STEP"] = os.environ.get("REPRO_VIGGLE", "1")
os.environ["ZIV_AI_VRAM_PROBE"] = os.environ.get("REPRO_VRAM_PROBE", "0")
os.environ.pop("ZIV_AI_RESOLUTION_MODE", None)
os.environ.pop("ZIV_AI_MAX_RESOLUTION", None)
os.environ["ZIV_AI_RESOLUTION_SIDE"] = os.environ.get("REPRO_SIDE", "1536")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, SERVER)


def _find_by_size(directory, size):
    import glob

    for path in glob.glob(os.path.join(directory, "*")):
        try:
            if os.path.isfile(path) and os.path.getsize(path) == size:
                return path
        except OSError:
            continue
    return None


IMG1 = _find_by_size(r"D:\temp", 3868008)                       # 3264x4096 person (剪贴板图片 (1).jpg)
IMG2 = r"D:\temp\7a5ba952803440059787efe2b0daaa92.png"          # 832x1024 scene
TAG = os.environ.get("REPRO_TAG", "viggle_multi")
OUT = os.environ.get("REPRO_OUT", r"D:\temp\repro_%s.png" % TAG)

import time

_T0 = time.time()

PROMPT = (
    "Replace the background of <image1> with the scene from <image2>: a lush green forest. "
    "Use <image2> only as the new scene; keep <image1>'s person (facial identity, hair, "
    "body shape, pose, clothing) exactly unchanged and blend the edges naturally."
)


def log(msg):
    import time

    print("[repro %7.2fs] %s" % (time.time() - _T0, msg), flush=True)


def main():
    import logging

    import config

    config.setup_logging("INFO")
    logging.getLogger("zivai.server").setLevel(logging.INFO)

    log("python=%s" % sys.executable)
    log("COMFY_ROOT=%s ok=%s" % (config.COMFY_ROOT, os.path.isdir(config.COMFY_ROOT)))
    log("RESOLUTION_MODE=%s SIDE=%s" % (config.RESOLUTION_MODE, config.RESOLUTION_SIDE))
    log("DIT=%s ok=%s" % (config.DIT_MODEL_PATH, os.path.isfile(config.DIT_MODEL_PATH)))
    log("IMG1 exists=%s IMG2 exists=%s" % (os.path.isfile(IMG1), os.path.isfile(IMG2)))

    import model_loader
    import pipeline
    import plugin_sampling
    import seams

    if os.environ.get("REPRO_BARE") == "1":
        # Emulate the user's fast A/B harness: import comfy, but do NOT apply ZIV's runtime
        # defaults (no DynamicVRAM bootstrap, no forced smart-memory-off, no SageAttention).
        model_loader._enable_dynamic_vram = lambda: None
        model_loader._apply_runtime_defaults = lambda: None

    # ---- instrumentation ----
    import seaminstr  # noqa: F401  (kept separate so this file stays small)
    seaminstr.install(pipeline, seams, plugin_sampling)

    log("loading DiT/TE/VAE ...")
    model, dt = model_loader.load_dit()
    clip, ct = model_loader.load_text_encoder()
    vae, vt = model_loader.load_vae()
    log("loaded dit=%.1fs te=%.1fs vae=%.1fs" % (dt, ct, vt))

    request = {
        "prompt": PROMPT,
        "image_path": IMG1,
        "mask_path": None,
        "additional_images": [IMG2] if os.environ.get("REPRO_MULTI", "1") == "1" else [],
        "denoise": 1.0,
        "seed": 42,
        "steps": int(os.environ.get("REPRO_STEPS", "40")),
        "resolution": {"mode": "side", "side": int(os.environ.get("REPRO_SIDE", "1536")), "max_pixels": 4700000},
        "model_id": "qwen-image-2.1",
        "output_path": OUT,
    }

    def on_progress(step, total, frac, stage, message):
        log("progress %s/%s frac=%.2f stage=%s msg=%s" % (step, total, frac, stage, message))

    log("running pipeline.run (op=inpaint, viggle ON, multi, side=1536) ...")
    for rep in range(int(os.environ.get("REPRO_REPEAT", "1"))):
        t = time.time()
        result = pipeline.run(model, clip, vae, request, on_progress=on_progress, op="inpaint")
        log("RESULT[%d] wall=%.1fs %s" % (rep, time.time() - t, result))


if __name__ == "__main__":
    main()
