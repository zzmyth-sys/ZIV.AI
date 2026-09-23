"""Controlled comparisons for Step 9C.5-D: do references / <imageN> change output?

Same seed as the scenario run, so any pixel difference is attributable to the
conditioning. Compares:
  A) main + ref1, prompt WITHOUT <image2>  vs  multi_out_2img_image2.png
  B) main only,   prompt of 3img_image3    vs  multi_out_3img_image3.png

GPU-only; run only when the GPU is idle (Z30). Usage:
    python test_multi_image_control.py [side]
"""

import os
import sys
import time

HERE = r"D:\devlop\ZIV.AI\_test_step2"
SERVER = r"D:\devlop\ZIV.AI\python\server"

SIDE = int(sys.argv[1]) if len(sys.argv) > 1 else 768

sys.path.insert(0, SERVER)

import engine as engine_module  # noqa: E402
import pipeline  # noqa: E402

MAIN = os.path.join(HERE, "user_input_1024.png")
REF1 = os.path.join(HERE, "711a8bca293b4a0984c1478886d72bf9.png")

PROMPT_3IMG = ("Blend the subjects of <image2> and <image3> into the scene of "
               "<image1> naturally.")
PROMPT_2IMG_NO_MARKER = ("Match the overall visual style of the second reference "
                         "image while keeping the main subject and composition of "
                         "the main image unchanged.")


def mean_abs_diff(a, b):
    import numpy as np
    from PIL import Image

    ia = np.asarray(Image.open(a).convert("RGB")).astype(np.float32)
    ib = np.asarray(Image.open(b).convert("RGB")).astype(np.float32)
    if ia.shape != ib.shape:
        return None
    return float(np.abs(ia - ib).mean())


def run(model, clip, vae, name, refs, prompt):
    out = os.path.join(HERE, "multi_ctrl_%s.png" % name)
    request = {
        "image_path": MAIN,
        "additional_images": refs,
        "prompt": prompt,
        "steps": 20,
        "seed": 42,
        "denoise": 1.0,
        "output_path": out,
        "resolution": {"mode": "side", "side": SIDE},
    }
    started = time.time()
    result = pipeline.run(model, clip, vae, request)
    print("CONTROL %s out=%s t=%.1fs" % (name, result["output_path"], time.time() - started))
    pipeline._free_vram()
    return result["output_path"]


def main():
    engine = engine_module.ModelEngine()
    engine.ensure_loaded()
    model, clip, vae = engine.components

    a = run(model, clip, vae, "2img_no_marker", [REF1], PROMPT_2IMG_NO_MARKER)
    b = run(model, clip, vae, "no_refs", [], PROMPT_3IMG)

    ref_a = os.path.join(HERE, "multi_out_2img_image2.png")
    ref_b = os.path.join(HERE, "multi_out_3img_image3.png")

    diff_a = mean_abs_diff(a, ref_a) if os.path.exists(ref_a) else None
    diff_b = mean_abs_diff(b, ref_b) if os.path.exists(ref_b) else None
    print("MARKER_EFFECT (2img no-marker vs <image2>) mean_abs_diff = %s" % diff_a)
    print("REFERENCE_EFFECT (no-refs vs 3img) mean_abs_diff = %s" % diff_b)
    return 0


if __name__ == "__main__":
    sys.exit(main())
