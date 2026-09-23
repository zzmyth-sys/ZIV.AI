"""Multi-image run with several references (Step 9C.5-D). GPU-only; idle (Z30).

Main + 3 reference images (the UI maximum: 1 main + up to 3 extras). Reports the
output and peak VRAM. Usage: python test_multi_image_many.py [side]
"""

import os
import subprocess
import sys
import threading
import time

HERE = r"D:\devlop\ZIV.AI\_test_step2"
SERVER = r"D:\devlop\ZIV.AI\python\server"

SIDE = int(sys.argv[1]) if len(sys.argv) > 1 else 1024

sys.path.insert(0, SERVER)

import engine as engine_module  # noqa: E402
import pipeline  # noqa: E402

MAIN = os.path.join(HERE, "user_input_1024.png")
REFS = [
    os.path.join(HERE, "711a8bca293b4a0984c1478886d72bf9.png"),
    os.path.join(HERE, "7db0c7464842484a96bc6413ee40af8a.png"),
    os.path.join(HERE, "88e64706511f4b2496bd348f558f1733.png"),
]
PROMPT = ("Keep the subject and composition of <image1>; take the color palette "
          "from <image2>, the lighting from <image3>, and the texture detail from "
          "<image4>.")


def nvidia_used():
    try:
        raw = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            stderr=subprocess.DEVNULL, timeout=5,
        )
        return int(raw.decode("utf-8", "ignore").strip().splitlines()[0])
    except Exception:
        return 0


def main():
    import torch

    engine = engine_module.ModelEngine()
    engine.ensure_loaded()
    model, clip, vae = engine.components

    peak = {"v": 0}
    stop = {"v": False}

    def sampler():
        while not stop["v"]:
            peak["v"] = max(peak["v"], nvidia_used())
            time.sleep(1)

    threading.Thread(target=sampler, daemon=True).start()
    torch.cuda.reset_peak_memory_stats()

    out = os.path.join(HERE, "multi_many_%d.png" % SIDE)
    request = {
        "image_path": MAIN,
        "additional_images": REFS,
        "prompt": PROMPT,
        "steps": 25,
        "seed": 42,
        "denoise": 1.0,
        "output_path": out,
        "resolution": {"mode": "side", "side": SIDE},
    }
    started = time.time()
    result = pipeline.run(model, clip, vae, request)
    stop["v"] = True

    print("images = 1 main + %d refs" % len(REFS))
    print("OK out=%s %dx%d t=%.1fs" % (result["output_path"], result["width"],
                                        result["height"], time.time() - started))
    print("nvidia_smi_peak_MiB=%d" % peak["v"])
    print("torch_peak_alloc_MB=%.1f" % (torch.cuda.max_memory_allocated() / 1024 / 1024))
    return 0


if __name__ == "__main__":
    sys.exit(main())
