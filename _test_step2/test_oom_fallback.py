"""OOM fallback test for pipeline.run (Step 4).

Usage:
    python -s test_oom_fallback.py real    # MAX_RESOLUTION=2048, natural OOM
    python -s test_oom_fallback.py force   # MAX_RESOLUTION=2048 + forced OOM

Prints the chosen resolution and the oom_fallback progress events, and checks
the output is structured (not noise). Does not modify pipeline.py/config.py.
"""

import os
import sys
import time

HERE = r"D:\devlop\ZIV.AI\_test_step2"
SERVER = r"D:\devlop\ZIV.AI\python\server"

MODE = sys.argv[1] if len(sys.argv) > 1 else "force"

os.environ["ZIV_AI_MAX_RESOLUTION"] = "2048"
os.environ.setdefault("ZIV_AI_RESOLUTION_FALLBACK", "1024,768,640")
if MODE == "force":
    os.environ["ZIV_AI_FORCE_OOM"] = "1"

sys.path.insert(0, SERVER)

import config  # noqa: E402
import engine as engine_module  # noqa: E402
import pipeline  # noqa: E402
import resolution  # noqa: E402

INPUT = os.path.join(HERE, "user_input_1024.png")
OUTPUT = os.path.join(HERE, "user_output_oom_fallback.png")
PROMPT = "把背景替换为古代中式茶肆，保留画面主体不变"


def structure_ratio(path):
    import numpy as np
    from PIL import Image

    array = np.asarray(Image.open(path).convert("L")).astype(np.float32) / 255.0
    var = float(array.var())
    if var <= 1e-9:
        return 0.0
    height, width = array.shape
    bh, bw = height // 8, width // 8
    blocks = array[: bh * 8, : bw * 8].reshape(bh, 8, bw, 8).mean(axis=(1, 3))
    return float(blocks.var() / var)


def main():
    print("=== OOM fallback test (mode=%s) ===" % MODE)
    print("MAX_RESOLUTION=%d FORCE_OOM=%s FALLBACK=%s"
          % (config.MAX_RESOLUTION, config.FORCE_OOM, config.RESOLUTION_FALLBACK))
    print("candidates =", resolution._resolution_candidates())

    engine = engine_module.ModelEngine()
    t = time.time()
    engine.ensure_loaded()
    model, clip, vae = engine.components
    print("loaded in %.1fs" % (time.time() - t))

    fallbacks = []
    stages = []

    def on_progress(step, total, fraction, stage, message):
        if message and message.startswith("oom_fallback"):
            fallbacks.append(message)
        stages.append((stage, message))

    result = pipeline.run(
        model, clip, vae,
        {"image_path": INPUT, "prompt": PROMPT, "steps": 20, "seed": 42,
         "denoise": 1.0, "output_path": OUTPUT},
        on_progress=on_progress,
    )
    ratio = structure_ratio(result["output_path"])
    print("chosen_resolution = %s" % result.get("resolution"))
    print("output = %s size=%dx%d" % (result["output_path"], result["width"], result["height"]))
    print("oom_fallback_events =", fallbacks)
    print("block_ratio = %.3f  structured=%s" % (ratio, ratio >= 0.80))
    ok = bool(fallbacks) and ratio >= 0.80
    print("RESULT=%s" % ("OK" if ok else "FAIL"))


if __name__ == "__main__":
    main()
