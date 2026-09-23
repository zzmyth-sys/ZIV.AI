"""Multi-image GPU end-to-end check for Step 9C.5-D (Qwen-Image-2.1).

Drives the production pipeline (``engine`` + ``pipeline.run`` with
``additional_images``) across the D2 scenarios and reports output structure plus
that the source image is untouched (Z24). GPU-only; run only when the GPU is idle
(Z30).

Usage:
    python test_multi_image_e2e.py [side]
"""

import hashlib
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
REF2 = os.path.join(HERE, "7db0c7464842484a96bc6413ee40af8a.png")

SCENARIOS = [
    ("2img_image2", [REF1],
     "Match the overall visual style of <image2> while keeping the main subject and composition of <image1> unchanged."),
    ("3img_image3", [REF1, REF2],
     "Blend the subjects of <image2> and <image3> into the scene of <image1> naturally."),
    ("3img_no_marker", [REF1, REF2],
     "Combine all provided reference images into one coherent composition, keeping the main subject recognizable."),
    ("3img_image1", [REF1, REF2],
     "Use <image1> as the main subject and restyle it to match <image2>."),
]


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def structure_ratio(path):
    import numpy as np
    from PIL import Image

    array = np.asarray(Image.open(path).convert("L")).astype(np.float32) / 255.0
    variance = float(array.var())
    if variance <= 1e-9:
        return 0.0
    height, width = array.shape
    bh, bw = height // 8, width // 8
    blocks = array[: bh * 8, : bw * 8].reshape(bh, 8, bw, 8).mean(axis=(1, 3))
    return float(blocks.var() / variance)


def main():
    print("SIDE=%d" % SIDE)
    for label, path in (("MAIN", MAIN), ("REF1", REF1), ("REF2", REF2)):
        if not os.path.exists(path):
            print("MISSING %s = %s" % (label, path))
            return 1
        print("%s = %s" % (label, path))

    before = sha256(MAIN)

    engine = engine_module.ModelEngine()
    started = time.time()
    engine.ensure_loaded()
    model, clip, vae = engine.components
    print("model loaded in %.1fs" % (time.time() - started))

    for name, refs, prompt in SCENARIOS:
        out = os.path.join(HERE, "multi_out_%s.png" % name)
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
        run_started = time.time()
        try:
            result = pipeline.run(model, clip, vae, request)
        except Exception as exc:  # noqa: BLE001
            print("SCENARIO %s RESULT=FAIL err=%r" % (name, exc))
            pipeline._free_vram()
            continue
        ratio = structure_ratio(result["output_path"])
        print("SCENARIO %s OK out=%s %dx%d t=%.1fs block_ratio=%.3f structured=%s"
              % (name, result["output_path"], result["width"], result["height"],
                 time.time() - run_started, ratio, ratio >= 0.80))
        pipeline._free_vram()

    after = sha256(MAIN)
    print("SOURCE_UNCHANGED=%s" % (before == after))
    return 0


if __name__ == "__main__":
    sys.exit(main())
