"""TE-Speed Qwen Image 2.1 A/B experiment (read-only; no product code change).

Runs the existing ``python/server`` pipeline under identical inputs:

  A  baseline           -- the loaded MODEL straight into ``pipeline.run``
  B  accelerated        -- TE-Speed predictor patch applied to the loaded MODEL

The patch is applied **locally in this script** (import-by-file + ``.patch``),
so ``pipeline.py`` / ``config.py`` are NOT modified and no ``config.TE_SPEED_*``
flag is required. The custom node is loaded from
``ComfyUI/custom_nodes/TE-Speed-QwenImage21``.

Fixed: source image, prompt, steps, seed, resolution. Warmup run is discarded
(so the first measured run is warm). Records wall time, ``pipeline.run``
duration, torch peak alloc/reserved, and an ``nvidia-smi`` peaked sample.

Outputs (all under _test_step2/, gitignored):
  te_speed_A.png / te_speed_B.png     first A / first B output
  te_speed_A_r<k>.png / te_speed_B_r<k>.png   every repeat
  te_speed_ab_result.json             full metrics

Usage (embedded python):
  & "D:\\devlop\\ZIV.AI\\Comfyui\\python_embeded\\python.exe" ^
      "D:\\devlop\\ZIV.AI\\_test_step2\\te_speed_ab.py"
"""

import json
import os
import subprocess
import sys
import threading
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SERVER = os.path.join(REPO, "python", "server")
COMFY_ROOT = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"

# --- knobs (env-overridable; set BEFORE importing config) ---
os.environ.setdefault("ZIV_AI_RESOLUTION_MODE", "side")
os.environ.setdefault("ZIV_AI_RESOLUTION_SIDE", "1024")
# TE-Speed patch options (node defaults unless overridden).
TE_MODE = os.environ.get("TE_SPEED_AB_MODE", "te_predictor")     # te_predictor | speed
TE_THRESHOLD = float(os.environ.get("TE_SPEED_AB_THRESHOLD", "0.06"))
TE_ATTENTION = os.environ.get("TE_SPEED_AB_ATTENTION", "kitchen_int8")
A_RUNS = int(os.environ.get("TE_SPEED_AB_A_RUNS", "2"))
B_RUNS = int(os.environ.get("TE_SPEED_AB_B_RUNS", "3"))
WARMUP = int(os.environ.get("TE_SPEED_AB_WARMUP", "1"))

sys.path.insert(0, SERVER)

import numpy as np  # noqa: E402

import config  # noqa: E402
import model_loader  # noqa: E402
import pipeline  # noqa: E402

SRC = os.path.join(HERE, "user_input.png")
PROMPT = "Replace the background with a bright blue sky with white clouds."
STEPS = 30
SEED = 42
DENOISE = 1.0


def log(*args):
    print(*args, flush=True)


# ---------------------------------------------------------------- TE-Speed
_TE_CLS = None


def _load_te_speed():
    """Load the TE-Speed custom node as a package (import-by-file; name collision safe)."""
    global _TE_CLS
    if _TE_CLS is not None:
        return _TE_CLS
    import importlib.util

    pkg_dir = os.path.join(COMFY_ROOT, "custom_nodes", "TE-Speed-QwenImage21")
    init_py = os.path.join(pkg_dir, "__init__.py")
    if not os.path.isfile(init_py):
        raise RuntimeError("TE-Speed custom node not found: %s" % init_py)
    name = "te_speed_qwen_image21"
    spec = importlib.util.spec_from_file_location(
        name, init_py, submodule_search_locations=[pkg_dir]
    )
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    _TE_CLS = mod.NODE_CLASS_MAPPINGS["TESpeedQwenImage21"]
    return _TE_CLS


def patch_te_speed(model):
    """Apply TE-Speed to ``model`` and return ``(patched_model, status)``.

    A fresh patch per run so no predictor cache leaks across runs. Raises on
    failure (this is an experiment script; we want the error visible).
    """
    cls = _load_te_speed()
    return cls().patch(
        model,
        attention=TE_ATTENTION,
        step_cache=TE_MODE,
        reuse_threshold=TE_THRESHOLD,
    )


# ------------------------------------------------------------ smi sampler
def _smi_used_mb():
    out = subprocess.check_output(
        ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
        stderr=subprocess.DEVNULL, timeout=5,
    )
    return float(out.decode("utf-8", "ignore").strip().splitlines()[0].strip())


class SmiPeak(threading.Thread):
    """Poll nvidia-smi in the background and keep the max used MiB."""

    def __init__(self, interval=0.15):
        super().__init__(daemon=True)
        self.interval = interval
        self.peak = 0.0
        self._stop = threading.Event()

    def run(self):
        while not self._stop.is_set():
            try:
                self.peak = max(self.peak, _smi_used_mb())
            except Exception:
                pass
            self._stop.wait(self.interval)

    def stop(self):
        self._stop.set()
        self.join(timeout=2.0)
        return self.peak


# ------------------------------------------------------------------- runs
def run_once(model, clip, vae, tag, out_path):
    import torch

    torch.cuda.reset_peak_memory_stats()
    smi = SmiPeak()
    smi.start()
    request = {
        "prompt": PROMPT,
        "image_path": SRC,
        "output_path": out_path,
        "steps": STEPS,
        "seed": SEED,
        "denoise": DENOISE,
    }
    t0 = time.time()
    try:
        result = pipeline.run(model, clip, vae, request)
        wall = time.time() - t0
        smi_peak = smi.stop()
        return {
            "tag": tag,
            "ok": True,
            "output": result.get("output_path"),
            "width": result.get("width"),
            "height": result.get("height"),
            "wall_s": round(wall, 2),
            "pipeline_ms": result.get("duration_ms"),
            "peak_alloc_MiB": round(torch.cuda.max_memory_allocated() / 1024 / 1024, 1),
            "peak_reserved_MiB": round(torch.cuda.max_memory_reserved() / 1024 / 1024, 1),
            "smi_peak_MiB": round(smi_peak, 1),
        }
    except Exception as exc:  # noqa: BLE001
        smi.stop()
        traceback.print_exc()
        return {"tag": tag, "ok": False, "error": repr(exc), "wall_s": round(time.time() - t0, 2)}


def main():
    log("== config ==")
    log("  mode=%s threshold=%s attention=%s steps=%d seed=%d" % (
        TE_MODE, TE_THRESHOLD, TE_ATTENTION, STEPS, SEED))
    log("  A_runs=%d B_runs=%d warmup=%d" % (A_RUNS, B_RUNS, WARMUP))
    log("  src=%s" % SRC)

    log("== loading models ==")
    model, td = model_loader.load_dit()
    log("  load_dit %.1fs" % td)
    clip, tc = model_loader.load_text_encoder()
    log("  load_te %.1fs" % tc)
    vae, tv = model_loader.load_vae()
    log("  load_vae %.1fs" % tv)

    # Probe TE-Speed once (status string) without running inference.
    probe_status = None
    try:
        _, probe_status = patch_te_speed(model)
        log("== TE-Speed probe status: %s" % probe_status)
    except Exception as exc:  # noqa: BLE001
        log("== TE-Speed probe FAILED: %r" % exc)
        traceback.print_exc()

    runs = []
    for i in range(WARMUP):
        log("== warmup %d (A, discarded) ==" % (i + 1))
        run_once(model, clip, vae, "warmup", os.path.join(HERE, "te_speed_warmup.png"))

    for i in range(1, A_RUNS + 1):
        out = os.path.join(HERE, "te_speed_A.png" if i == 1 else "te_speed_A_r%d.png" % i)
        log("== A run %d ==" % i)
        r = run_once(model, clip, vae, "A%d" % i, out)
        log("  %s" % json.dumps(r))
        runs.append(r)

    for i in range(1, B_RUNS + 1):
        out = os.path.join(HERE, "te_speed_B.png" if i == 1 else "te_speed_B_r%d.png" % i)
        log("== B run %d (TE-Speed) ==" % i)
        try:
            patched, status = patch_te_speed(model)
            log("  patch status: %s" % status)
        except Exception as exc:  # noqa: BLE001
            log("  patch FAILED: %r" % exc)
            traceback.print_exc()
            break
        r = run_once(patched, clip, vae, "B%d" % i, out)
        log("  %s" % json.dumps(r))
        runs.append(r)

    summary = {"config": {
        "mode": TE_MODE, "threshold": TE_THRESHOLD, "attention": TE_ATTENTION,
        "steps": STEPS, "seed": SEED, "a_runs": A_RUNS, "b_runs": B_RUNS,
    }, "probe_status": probe_status, "runs": runs}
    result_path = os.path.join(HERE, "te_speed_ab_result.json")
    with open(result_path, "w", encoding="utf-8") as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=2)

    log("== SUMMARY ==")
    log(json.dumps(summary, ensure_ascii=False, indent=2))
    log("wrote %s" % result_path)


if __name__ == "__main__":
    main()
