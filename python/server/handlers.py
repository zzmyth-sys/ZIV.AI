import ctypes
import logging
import subprocess
import threading
import uuid

import config
import engine as engine_module
import ipc
import loras
import model_loader
import pipeline
import pipeline_hooks

_LOG = logging.getLogger("zivai.server")

INTERRUPT_EXCEPTION_NAME = "InterruptProcessingException"

MODEL_STATUS_NOT_LOADED = engine_module.STATE_NOT_LOADED

_ENGINE = engine_module.ModelEngine()

# Single in-flight task state (Z18 serial). `cancel` is delivered by draining
# the pipe from inside the sampling loop (`_make_cancel_poller`), so the read
# loop only sees a `cancel` frame when no task is in flight.
_ACTIVE_LOCK = threading.Lock()
_ACTIVE_TASK_ID = None


class _NvmlMemory(ctypes.Structure):
    _fields_ = [
        ("total", ctypes.c_ulonglong),
        ("free", ctypes.c_ulonglong),
        ("used", ctypes.c_ulonglong),
    ]


def _vram_via_nvml():
    try:
        library = ctypes.WinDLL("nvml.dll")
    except (OSError, AttributeError):
        return None
    try:
        if library.nvmlInit_v2() != 0:
            return None
        try:
            handle = ctypes.c_void_p()
            library.nvmlDeviceGetHandleByIndex_v2.argtypes = [
                ctypes.c_uint,
                ctypes.POINTER(ctypes.c_void_p),
            ]
            if library.nvmlDeviceGetHandleByIndex_v2(0, ctypes.byref(handle)) != 0:
                return None
            memory = _NvmlMemory()
            library.nvmlDeviceGetMemoryInfo.argtypes = [
                ctypes.c_void_p,
                ctypes.POINTER(_NvmlMemory),
            ]
            if library.nvmlDeviceGetMemoryInfo(handle, ctypes.byref(memory)) != 0:
                return None
            return round(memory.used / (1024.0 * 1024.0), 1)
        finally:
            library.nvmlShutdown()
    except (OSError, AttributeError):
        return None


def _vram_via_nvidia_smi():
    try:
        raw = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            stderr=subprocess.DEVNULL,
            timeout=5,
        )
    except Exception:
        return None
    try:
        first = raw.decode("utf-8", "ignore").strip().splitlines()[0].strip()
        return float(first)
    except Exception:
        return None


def get_vram_used_mb():
    value = _vram_via_nvml()
    if value is None:
        value = _vram_via_nvidia_smi()
    return 0.0 if value is None else float(value)


def _models():
    return _ENGINE.models


def _status_response(message, message_type):
    return {
        "type": message_type,
        "request_id": message.get("request_id"),
        "status": "ok",
        "version": config.BACKEND_VERSION,
        "protocol_version": config.PROTOCOL_VERSION,
        "model_status": _ENGINE.status,
        "vram_used_mb": get_vram_used_mb(),
        "idle_unload_seconds": config.IDLE_UNLOAD_SECONDS,
        "models": _models(),
    }


def handle_ping(message):
    return _status_response(message, "pong")


def handle_submit(message, frame_io):
    """Run a task synchronously; `cancel` is polled from the sampler callback.

    Pipe I/O stays single-threaded (a `FileIO` lock would deadlock a worker
    thread's write against the main loop's blocking read), so cancellation is
    delivered through `poll_cancel` in the sampling loop instead.
    """
    task_id = message.get("task_id") or uuid.uuid4().hex
    payload = message.get("payload") or {}
    op = str(message.get("op") or "inpaint").strip().lower()
    _set_active(task_id)
    try:
        _run_submit(frame_io, task_id, payload, op)
    finally:
        _ENGINE.touch()
        _clear_active(task_id)
    return "continue"


def handle_cancel(message, frame_io):
    """Fallback cancel handled outside sampling (e.g. a late/queued frame).

    While a task is running its poller consumes the `cancel` frame directly;
    this only fires when no task is in flight, which is a no-op.
    """
    task_id = message.get("task_id")
    active = _active_task_id()
    if active is None or (task_id and task_id != active):
        return "continue"
    _interrupt_processing()
    return "continue"


def _run_submit(frame_io, task_id, payload, op="inpaint"):
    try:
        frame_io.write_json({"type": "accepted", "task_id": task_id})
        # Must precede any `import comfy.model_management`: it fixes the
        # smart-memory flag at import time (see model_loader).
        model_loader.prepare_environment()
        _clear_interrupt()
        _configure_pre_sampling_hooks(payload)
        if payload.get("resolution"):
            _LOG.info("submit resolution payload: %s", payload.get("resolution"))

        try:
            # Step 8-2: the requested model id (None / unknown -> default) selects the registry entry.
            _ENGINE.ensure_loaded(
                payload.get("model_id"), _make_progress_pusher(frame_io, task_id)
            )
        except Exception as exc:
            _write_error(frame_io, task_id, "model_load_failed", exc)
            return

        frame_io.write_json(
            {
                "type": "progress",
                "task_id": task_id,
                "step": 0,
                "total": 0,
                "fraction": 1.0,
                "message": "model_loaded",
                "stage": "sampling",
                "sub_stage": "ready",
            }
        )

        model, clip, vae = _ENGINE.components
        try:
            result = _dispatch_op(model, clip, vae, payload, op, frame_io, task_id)
        except Exception as exc:
            _write_error(frame_io, task_id, "inference_failed", exc)
            return

        frame_io.write_json(
            {
                "type": "result",
                "task_id": task_id,
                "output_path": result["output_path"],
                "duration_ms": result["duration_ms"],
                "width": result["width"],
                "height": result["height"],
                "seed": result["seed"],
            }
        )
    except BaseException as exc:
        # `InterruptProcessingException` derives from BaseException, so the
        # cancellation path must be caught here (never `except Exception`).
        if not _is_interrupt(exc):
            raise
        _release_caches()
        _write_canceled(frame_io, task_id)


def _dispatch_op(model, clip, vae, payload, op, frame_io, task_id):
    """Route ``submit.op`` to the matching pipeline entry (Step 7 Phase 2).

    User / C# masks are read as **grayscale** (0-255) per the revised Z19, so the
    soft feathered ramp the editor may export reaches the sampler unchanged
    (``mask_binary=False``). Only the ``outpaint`` path builds its own backend
    mask (already passed as soft through ``run_outpaint``).
    """
    callbacks = {
        "on_progress": _make_sampling_progress(frame_io, task_id),
        "on_preview": _make_preview(frame_io, task_id),
        "poll_cancel": _make_cancel_poller(frame_io),
    }
    if op == "outpaint":
        return pipeline.run_outpaint(model, clip, vae, payload, **callbacks)
    if op in ("", "inpaint", "t2i"):
        return pipeline.run(model, clip, vae, payload, mask_binary=False, **callbacks)
    _LOG.warning("unknown op %r; falling back to inpaint", op)
    return pipeline.run(model, clip, vae, payload, mask_binary=False, **callbacks)


def _configure_pre_sampling_hooks(payload):
    """Reset and register this request's pre-sampling hooks (Step 4).

    Optional ``submit.payload`` fields (contract §3.4):
      - ``lora``: ``{path, strength_model, strength_clip}``
      - ``optimizations``: ``{magcache, magcache_thresh}``

    ``lora.path`` is a registry id (resolved via :mod:`loras` against
    ``Template/loras.json``) or a literal weight path (Step 8-1). The MagCache
    transform stays a reserved seam (out of scope; see DOC/OPTIMIZATION.md).
    """
    pipeline_hooks.clear_pre_sampling_hooks()
    _register_loras(payload)
    optimizations = payload.get("optimizations")
    if isinstance(optimizations, dict) and optimizations.get("magcache"):
        pipeline_hooks.register_pre_sampling_hook(_build_magcache_hook(optimizations))


def _register_loras(payload):
    """Register one pre-sampling hook per LoRA (T3.2).

    ``submit.payload.loras`` (array) wins; a legacy single ``lora`` dict is upgraded to a
    one-element list. Each entry is ``{path, strength_model, strength_clip}`` where ``path``
    is a registry id (resolved via :mod:`loras`) or a literal weight path. De-duplication
    happens on the C# side (T3.2), so every entry is registered in order.
    """
    entries = payload.get("loras")
    if not isinstance(entries, list) or not entries:
        legacy = payload.get("lora")
        entries = [legacy] if isinstance(legacy, dict) else []

    for lora in entries:
        if not isinstance(lora, dict) or not lora.get("path"):
            continue
        entry = loras.resolve(lora.get("path"))
        lora_path = loras.resolve_path(lora.get("path"))
        if not lora_path:
            _LOG.warning("LoRA id/path could not be resolved: %s", lora.get("path"))
            continue
        pipeline_hooks.register_pre_sampling_hook(
            pipeline_hooks.make_lora_hook(
                lora_path,
                loras.resolve_strength(
                    lora.get("strength_model"), entry, "default_strength_model"
                ),
                loras.resolve_strength(
                    lora.get("strength_clip"), entry, "default_strength_clip"
                ),
            )
        )


def _build_magcache_hook(optimizations):
    def hook(model, clip, params):
        _LOG.info(
            "pre-sampling MagCache hook reserved (thresh=%s, not implemented)",
            optimizations.get("magcache_thresh"),
        )
        return model, clip

    return hook


def _make_sampling_progress(frame_io, task_id):
    def push(step, total, fraction, stage, message_text):
        frame_io.write_json(
            {
                "type": "progress",
                "task_id": task_id,
                "step": step,
                "total": total,
                "fraction": fraction,
                "message": message_text,
                "stage": stage,
                "sub_stage": None,
            }
        )

    return push


def _make_preview(frame_io, task_id):
    def push(step, total, jpeg_bytes):
        frame_io.write_preview(task_id, step, total, jpeg_bytes)

    return push


def _make_cancel_poller(frame_io):
    """Drain any pending control frames from inside the sampling loop.

    Only `cancel` triggers work (sets the interrupt flag); `ping` is answered so
    the pipe stays clean. `shutdown` during a task is ignored.
    """

    def poll():
        while frame_io.has_pending_frame():
            frame = frame_io.read_frame()
            if frame is None:
                break
            frame_type, payload = frame
            if frame_type != ipc.FRAME_JSON:
                continue
            try:
                message = ipc.decode_json(payload)
            except ValueError:
                continue
            if not isinstance(message, dict):
                continue
            message_type = message.get("type")
            if message_type == "cancel":
                _interrupt_processing()
            elif message_type == "ping":
                frame_io.write_json(handle_ping(message))

    return poll


def _is_interrupt(exc):
    return type(exc).__name__ == INTERRUPT_EXCEPTION_NAME


def _interrupt_processing():
    try:
        import comfy.model_management as mm

        mm.interrupt_current_processing(True)
    except Exception:
        pass


def _clear_interrupt():
    try:
        import comfy.model_management as mm

        mm.interrupt_current_processing(False)
    except Exception:
        pass


def _release_caches():
    """Release a canceled run's activation memory; keep the model resident."""
    try:
        import comfy.model_management as mm

        mm.soft_empty_cache(force=True)
    except Exception:
        pass
    try:
        import torch

        if torch.cuda.is_available():
            torch.cuda.empty_cache()
    except Exception:
        pass


def _set_active(task_id):
    global _ACTIVE_TASK_ID
    with _ACTIVE_LOCK:
        _ACTIVE_TASK_ID = task_id


def _clear_active(task_id):
    global _ACTIVE_TASK_ID
    with _ACTIVE_LOCK:
        if _ACTIVE_TASK_ID == task_id:
            _ACTIVE_TASK_ID = None


def _active_task_id():
    with _ACTIVE_LOCK:
        return _ACTIVE_TASK_ID


def active_task_id():
    """Public accessor for the heartbeat / idle watcher."""
    return _active_task_id()


def is_busy():
    """True while a task is in flight (Z18: do not unload concurrently)."""
    return _active_task_id() is not None


def _write_canceled(frame_io, task_id):
    frame_io.write_json({"type": "canceled", "task_id": task_id})


def _write_error(frame_io, task_id, code, exc):
    frame_io.write_json(
        {
            "type": "error",
            "task_id": task_id,
            "code": code,
            "message": "%s: %s" % (type(exc).__name__, exc),
        }
    )


def _make_progress_pusher(frame_io, task_id):
    def push(stage, sub_stage, fraction, message):
        frame_io.write_json(
            {
                "type": "progress",
                "task_id": task_id,
                "step": 0,
                "total": 0,
                "fraction": fraction,
                "message": message,
                "stage": stage,
                "sub_stage": sub_stage,
            }
        )

    return push


def handle_shutdown(message):
    return {"type": "shutdown_ack"}
