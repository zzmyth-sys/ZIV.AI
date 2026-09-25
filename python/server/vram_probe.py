"""Opt-in VRAM probe (env ``ZIV_AI_VRAM_PROBE=1``).

Diagnostic only, no effect unless the env var is set. It prints/logs the VRAM
story around ComfyUI's first-generation model load so we can see *how* the
weights land on the GPU:

- ComfyUI ``load_models_gpu`` (the function that actually moves the ModelPatcher
  weights onto the compute device and frees/offloads the rest),
- each ``model_loader.load_*`` call,
- the first sampler pass + decode.

Numbers come from both ``torch.cuda`` (allocator: allocated / reserved) and
``nvidia-smi`` (whole-card used, includes non-torch processes).
"""

import functools
import logging
import os
import sys
import time

_ENABLED = os.environ.get("ZIV_AI_VRAM_PROBE") == "1"
_INSTALLED = False
_LOG = logging.getLogger("zivai.server")


def _emit(tag):
    if not _ENABLED:
        return
    parts = ["[vram] %-34s" % tag]
    try:
        import torch

        if torch.cuda.is_available():
            parts.append(
                "slot=%.0fMB resv=%.0fMB"
                % (torch.cuda.memory_allocated() / 2 ** 20, torch.cuda.memory_reserved() / 2 ** 20)
            )
    except Exception:
        pass
    parts.append("smi=%sMB" % _smi_used_mb())
    line = " ".join(parts)
    print(line, file=sys.stderr, flush=True)
    _LOG.info("%s", line)


def _smi_used_mb():
    try:
        import subprocess

        raw = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            stderr=subprocess.DEVNULL,
            timeout=5,
        )
        return int(raw.decode("utf-8", "ignore").strip().splitlines()[0].strip())
    except Exception:
        return "?"


def install():
    """Monkey-patch ComfyUI's device-memory manager once (no-op when disabled)."""
    global _INSTALLED
    if not _ENABLED or _INSTALLED:
        return
    _INSTALLED = True
    try:
        import comfy.model_management as mm
    except Exception as exc:  # noqa: BLE001
        _LOG.warning("[vram] probe install failed: %s", exc)
        return

    _emit("install (after comfy import)")

    original = mm.load_models_gpu

    @functools.wraps(original)
    def load_models_gpu(models, *args, **kwargs):
        loaded = [_model_name(m) for m in (models or [])]
        _emit("load_models_gpu BEG n=%d %s" % (len(loaded), loaded))
        started = time.time()
        result = original(models, *args, **kwargs)
        _LOG.info("[vram] load_models_gpu END %.2fs", time.time() - started)
        _emit("load_models_gpu END")
        return result

    mm.load_models_gpu = load_models_gpu

    original_free = mm.free_memory

    @functools.wraps(original_free)
    def free_memory(*args, **kwargs):
        required = args[0] if args else kwargs.get("memory_required", 0) or 0
        _emit("free_memory BEG need=%.0fMB" % (required / 2 ** 20))
        result = original_free(*args, **kwargs)
        _emit("free_memory END")
        return result

    mm.free_memory = free_memory


def _model_name(patcher):
    inner = getattr(patcher, "model", patcher)
    return type(inner).__name__ if inner is not None else "?"


def stage(tag):
    """Public helper so model_loader / pipeline can emit their own checkpoints."""
    _emit(tag)
