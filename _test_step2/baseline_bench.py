"""ZIV.AI Step 4 baseline benchmark.

Runs the real server pipeline three times on the same 512^2 edit (same prompt
and input image) and records per-phase timings plus peak VRAM. Output is
written to ``baseline_bench_result.txt`` for later optimization comparison
(see ``DOC/OPTIMIZATION.md``).

Usage:
    Comfyui\\python_embeded\\python.exe -s _test_step2\\baseline_bench.py
"""

import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER = os.path.join(os.path.dirname(HERE), "python", "server")
sys.path.insert(0, SERVER)

import config  # noqa: E402
import engine as engine_module  # noqa: E402
import model_loader  # noqa: E402
import pipeline  # noqa: E402

INPUT = os.path.join(HERE, "input_test_512.png")
PROMPT = "Make the background a snowy mountain landscape, keep the subject unchanged."
STEPS = 20
SEED = 42
RUNS = 3
RESULT_PATH = os.path.join(HERE, "baseline_bench_result.txt")


def nvidia_used_mb():
    try:
        raw = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            stderr=subprocess.DEVNULL,
            timeout=5,
        )
        return float(raw.decode("utf-8", "ignore").strip().splitlines()[0])
    except Exception:
        return -1.0


def structure_ratio(path):
    """Large-scale structure ratio (variance of 8x8 block means / global var).

    Valid generated images measured 0.85-0.97; pure-noise outputs 0.30-0.74.
    Used here as a lightweight "not pure noise" check (Step 4).
    """
    import numpy as np
    from PIL import Image

    array = np.asarray(Image.open(path).convert("L")).astype(np.float32) / 255.0
    var = float(array.var())
    if var <= 1e-9:
        return 0.0
    height, width = array.shape
    blocks_h, blocks_w = height // 8, width // 8
    if blocks_h == 0 or blocks_w == 0:
        return 1.0
    blocks = array[: blocks_h * 8, : blocks_w * 8].reshape(blocks_h, 8, blocks_w, 8).mean(axis=(1, 3))
    return float(blocks.var() / var)


def main():
    model_loader.prepare_environment()
    import torch

    lines = []

    def log(text):
        print(text)
        lines.append(text)

    log("ZIV.AI baseline bench: %d x 512 edit (steps=%d, seed=%d)" % (RUNS, STEPS, SEED))
    log("input = %s" % INPUT)
    log("gpu_baseline_MB = %.1f" % nvidia_used_mb())

    engine = engine_module.ModelEngine()
    t0 = time.time()
    timings = engine.ensure_loaded()
    load_s = time.time() - t0
    model, clip, vae = engine.components
    log("model_load_s = %.3f  timings = %s" % (load_s, timings))
    log("gpu_after_load_MB = %.1f" % nvidia_used_mb())

    rows = []
    for index in range(1, RUNS + 1):
        torch.cuda.reset_peak_memory_stats()
        state = {"gpu": None, "sample": None, "vae": None, "gpu_sample_MB": 0.0}
        output = os.path.join(HERE, "baseline_out_%d.png" % index)

        def on_progress(step, total, fraction, stage, message):
            now = time.time()
            if stage == "sampling" and message == "moving_to_gpu" and state["gpu"] is None:
                state["gpu"] = now
            elif stage == "sampling" and message == "sampling" and state["sample"] is None:
                state["sample"] = now
            elif stage == "vae_decode" and state["vae"] is None:
                state["vae"] = now
                state["gpu_sample_MB"] = nvidia_used_mb()

        started = time.time()
        result = pipeline.run(
            model,
            clip,
            vae,
            {
                "image_path": INPUT,
                "prompt": PROMPT,
                "steps": STEPS,
                "seed": SEED,
                "denoise": 1.0,
                "output_path": output,
            },
            on_progress=on_progress,
        )
        ended = time.time()

        moving = (state["sample"] - state["gpu"]) if state["gpu"] and state["sample"] else -1.0
        sampling = (state["vae"] - state["sample"]) if state["sample"] and state["vae"] else -1.0
        decode = (ended - state["vae"]) if state["vae"] else -1.0
        row = {
            "run": index,
            "load_s": round(load_s if index == 1 else 0.0, 3),
            "moving_to_gpu_s": round(moving, 3),
            "sampling_s": round(sampling, 3),
            "vae_decode_s": round(decode, 3),
            "total_s": round(ended - started, 3),
            "pipeline_ms": result["duration_ms"],
            "peak_alloc_MB": round(torch.cuda.max_memory_allocated() / 1048576.0, 1),
            "peak_reserved_MB": round(torch.cuda.max_memory_reserved() / 1048576.0, 1),
            "gpu_sample_MB": round(state["gpu_sample_MB"], 1),
            "gpu_after_MB": round(nvidia_used_mb(), 1),
            "output": result["output_path"],
        }
        row["block_ratio"] = round(structure_ratio(result["output_path"]), 3)
        row["structured"] = row["block_ratio"] >= 0.80
        rows.append(row)
        log(
            "run %d: moving_to_gpu=%.3fs sampling=%.3fs vae_decode=%.3fs total=%.3fs "
            "peak_alloc=%.1fMB peak_reserved=%.1fMB smi_sample=%.1fMB block_ratio=%.3f %s"
            % (
                row["run"],
                row["moving_to_gpu_s"],
                row["sampling_s"],
                row["vae_decode_s"],
                row["total_s"],
                row["peak_alloc_MB"],
                row["peak_reserved_MB"],
                row["gpu_sample_MB"],
                row["block_ratio"],
                "OK" if row["structured"] else "NOISE!",
            )
        )

    warm = [r for r in rows[1:]] or rows
    log("--- summary (steady state, excluding cold load) ---")
    log(
        "moving_to_gpu_avg=%.3fs sampling_avg=%.3fs vae_decode_avg=%.3fs total_avg=%.3fs peak_alloc_avg=%.1fMB"
        % (
            sum(r["moving_to_gpu_s"] for r in warm) / len(warm),
            sum(r["sampling_s"] for r in warm) / len(warm),
            sum(r["vae_decode_s"] for r in warm) / len(warm),
            sum(r["total_s"] for r in warm) / len(warm),
            sum(r["peak_alloc_MB"] for r in warm) / len(warm),
        )
    )
    log("gpu_after_bench_MB = %.1f" % nvidia_used_mb())
    noisy = [r["run"] for r in rows if not r["structured"]]
    if noisy:
        log("NOISE RUNS = %s (block_ratio < 0.80)" % noisy)
        log("RESULT=NOISE")
    else:
        log("RESULT=OK (all runs structured)")

    with open(RESULT_PATH, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
