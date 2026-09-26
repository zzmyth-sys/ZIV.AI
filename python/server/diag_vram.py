"""Opt-in VRAM / frame waveform recorder for the ZIV.AI backend (observation only).

Enabled only when ``ZIV_AI_DIAG=1``; otherwise every call is a cheap no-op, so
the recorder cannot change backend behaviour. When enabled it appends one JSONL
line per recorded event to ``<ZIV_AI_DIAG_DIR>/<ZIV_AI_DIAG_RUN_ID>.jsonl``.

The VRAM figure is taken from the caller-supplied provider, which is the same
``handlers.get_vram_used_mb`` (NVML / nvidia-smi) the heartbeat already uses — no
extra polling thread is started. ``record`` is fully defensive: any failure is
swallowed so observation can never affect the pipeline.

Line schema (one JSON object per line)::

    {"ts_ms": 1234.5, "kind": "progress", "stage": "loading_model",
     "sub_stage": "dit", "fraction": 0.0, "step": 0, "total": 0,
     "vram_mb": 8123.0, "task_id": "..."}

``kind`` is one of: ``accepted`` / ``progress`` / ``preview`` / ``result`` /
``error`` / ``heartbeat``. Load-phase boundaries are recoverable from the
progress frames alone (``stage=loading_model`` + ``sub_stage`` + ``fraction``):
dit 0.00→0.33, te 0.33→0.66, vae 0.66→1.00, then ``stage=sampling``.
"""

import atexit
import json
import os
import threading
import time

import config

_ENABLED = os.environ.get("ZIV_AI_DIAG") == "1"
_DEFAULT_DIR = os.path.join(config.REPO_ROOT, "_test_step2", "diag_vram")

_RUN_DIR = os.environ.get("ZIV_AI_DIAG_DIR") or _DEFAULT_DIR
_RUN_ID = os.environ.get("ZIV_AI_DIAG_RUN_ID") or ("run_%d" % os.getpid())

_LOCK = threading.Lock()
_FILE = None


def enabled():
    """True when recording is active (``ZIV_AI_DIAG=1``)."""
    return _ENABLED


def output_path():
    """Absolute path of this run's JSONL file (regardless of enabled state)."""
    return os.path.join(_RUN_DIR, _RUN_ID + ".jsonl")


def _handle():
    global _FILE
    if _FILE is None:
        os.makedirs(_RUN_DIR, exist_ok=True)
        _FILE = open(output_path(), "a", encoding="utf-8")
    return _FILE


def record(
    kind,
    vram_provider=None,
    stage=None,
    sub_stage=None,
    fraction=None,
    step=None,
    total=None,
    task_id=None,
):
    """Append one observation line. No-op / never raises when disabled."""
    if not _ENABLED:
        return
    try:
        vram_mb = None
        if vram_provider is not None:
            try:
                vram_mb = float(vram_provider())
            except Exception:
                vram_mb = None
        line = {
            "ts_ms": round(time.time() * 1000.0, 3),
            "kind": kind,
            "stage": stage,
            "sub_stage": sub_stage,
            "fraction": fraction,
            "step": step,
            "total": total,
            "vram_mb": vram_mb,
            "task_id": task_id,
        }
        with _LOCK:
            handle = _handle()
            handle.write(json.dumps(line, ensure_ascii=False) + "\n")
            handle.flush()
    except Exception:
        # Observation must never affect the backend.
        pass


def close():
    """Flush / close the run file (best effort)."""
    global _FILE
    with _LOCK:
        if _FILE is not None:
            try:
                _FILE.flush()
                _FILE.close()
            except Exception:
                pass
            _FILE = None


if _ENABLED:
    atexit.register(close)
