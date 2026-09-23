"""Peak-VRAM probe for the ZIV.AI multi-image pipeline (Step 9C.5-D).

Runs ONE 3-image scenario at the same resolution as the official ComfyUI flow
(side=1024) and reports peak VRAM (nvidia-smi + torch). GPU-only; run when idle
(Z30). Usage: python test_multi_image_peak.py [side]
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
REF1 = os.path.join(HERE, "711a8bca293b4a0984c1478886d72bf9.png")
REF2 = os.path.join(HERE, "7db0c7464842484a96bc6413ee40af8a.png")
PROMPT = ("Blend the subjects of <image2> and <image3> into the scene of "
          "<image1> naturally.")


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
    started = time.time()
    engine.ensure_loaded()
    model, clip, vae = engine.components
    print("loaded in %.1fs" % (time.time() - started))

    peak = {"v": 0}
    stop = {"v": False}

    def sampler():
        while not stop["v"]:
            peak["v"] = max(peak["v"], nvidia_used())
            time.sleep(1)

    thread = threading.Thread(target=sampler, daemon=True)
    thread.start()

    torch.cuda.reset_peak_memory_stats()
    out = os.path.join(HERE, "multi_peak_%d.png" % SIDE)
    request = {
        "image_path": MAIN,
        "additional_images": [REF1, REF2],
        "prompt": PROMPT,
        "steps": 25,
        "seed": 42,
        "denoise": 1.0,
        "output_path": out,
        "resolution": {"mode": "side", "side": SIDE},
    }
    run_started = time.time()
    result = pipeline.run(model, clip, vae, request)
    elapsed = time.time() - run_started
    stop["v"] = True

    print("OK out=%s %dx%d t=%.1fs" % (result["output_path"], result["width"],
                                        result["height"], elapsed))
    print("nvidia_smi_peak_MiB=%d" % peak["v"])
    print("torch_peak_alloc_MB=%.1f" % (torch.cuda.max_memory_allocated() / 1024 / 1024))
    print("torch_peak_reserved_MB=%.1f" % (torch.cuda.max_memory_reserved() / 1024 / 1024))
    return 0


if __name__ == "__main__":
    sys.exit(main())
