import argparse
import gc
import os
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import config

# Align official main.py's process env BEFORE importing handlers (which pulls in
# comfy/torch): PYTORCH_CUDA_ALLOC_CONF must land before the first CUDA init.
config.apply_official_env()

import handlers
import heartbeat
import idle_watcher
import ipc
import model_loader

LOG = config.setup_logging("INFO")


def _connect(pipe_path, timeout_s):
    deadline = time.monotonic() + timeout_s
    delay = 0.02
    while True:
        try:
            return open(pipe_path, "r+b", buffering=0)
        except OSError:
            if time.monotonic() >= deadline:
                raise
            time.sleep(delay)
            delay = min(delay * 2, 0.25)


def _cleanup():
    # A1/C1: a client disconnect must release the loaded model (ComfyUI registry)
    # before the process exits, not just tidy the torch allocator.
    try:
        handlers._ENGINE.unload()
    except Exception:
        LOG.exception("vram release on shutdown failed")
    torch = sys.modules.get("torch")
    if torch is not None:
        try:
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
        except Exception:
            pass
    try:
        gc.collect()
    except Exception:
        pass


def _dispatch(frame_io, message):
    message_type = message.get("type")
    if message_type == "ping":
        frame_io.write_json(handlers.handle_ping(message))
        return "continue"
    if message_type == "submit":
        return handlers.handle_submit(message, frame_io)
    if message_type == "cancel":
        return handlers.handle_cancel(message, frame_io)
    if message_type == "shutdown":
        frame_io.write_json(handlers.handle_shutdown(message))
        return "shutdown"
    frame_io.write_json(
        {
            "type": "error",
            "request_id": message.get("request_id"),
            "code": "unknown_message",
            "message": "unknown message type: %r" % (message_type,),
        }
    )
    return "continue"


def _next_message(frame_io, pollable):
    """Return the next JSON control message, or ``None`` when there is none.

    With a pollable pipe the read never blocks, so the heartbeat thread's
    writes are not starved by the fd lock (Step 2.4). Without one we fall back
    to a blocking read (no heartbeat thread is started in that case).
    """
    if not pollable:
        frame = frame_io.read_frame()
        if frame is None:
            raise EOFError("pipe closed")
        return _decode_frame(frame)

    while True:
        state = frame_io.poll_state()
        if state == ipc.PIPE_CLOSED:
            raise EOFError("pipe closed")
        if state == ipc.PIPE_NO_DATA:
            return None
        frame = frame_io.read_frame()
        if frame is None:
            raise EOFError("pipe closed")
        message = _decode_frame(frame)
        if message is not None:
            return message


def _decode_frame(frame):
    frame_type, payload = frame
    if frame_type == ipc.FRAME_BINARY:
        LOG.warning("ignoring unexpected binary frame (%d bytes)", len(payload))
        return None
    if frame_type != ipc.FRAME_JSON:
        LOG.warning("ignoring unknown frame type 0x%02x", frame_type)
        return None
    try:
        message = ipc.decode_json(payload)
    except ValueError as exc:
        LOG.warning("invalid JSON control frame: %s", exc)
        return None
    if not isinstance(message, dict):
        LOG.warning("ignoring non-object control frame")
        return None
    return message


def _prewarm():
    """Background warm-up (optimization §10.2.1): move the ~4 s `import comfy/torch`
    off the first `submit` path. Pure CPU — never touches the GPU. Failures are
    logged and ignored (the first submit will retry `prepare_environment`)."""
    try:
        LOG.info("prewarm: prepare_environment begin")
        model_loader.prepare_environment()
        LOG.info("prewarm: prepare_environment done")
        if config.PREWARM_LEVEL >= 1:
            handlers._ENGINE.ensure_loaded(None, None)
            LOG.info("prewarm: model weights loaded into host RAM")
    except Exception:
        LOG.exception("prewarm failed (non-fatal; first submit will retry)")


def _start_prewarm():
    if not config.PREWARM:
        return
    threading.Thread(target=_prewarm, name="zivai-prewarm", daemon=True).start()


def run(pipe_path, connect_timeout_s):
    LOG.info("connecting to %s", pipe_path)
    stream = _connect(pipe_path, connect_timeout_s)
    LOG.info("connected to backend pipe")
    frame_io = ipc.FrameIO(stream)

    # Warm `comfy`/torch in the background while the process idles (before any submit).
    _start_prewarm()

    pollable = frame_io.pollable
    sender = heartbeat.HeartbeatSender(
        frame_io, handlers.get_vram_used_mb, handlers.active_task_id
    )
    watcher = idle_watcher.IdleWatcher(handlers._ENGINE, handlers.is_busy)
    if pollable:
        sender.start()
        watcher.start()
    else:
        LOG.warning("pipe is not pollable; heartbeat and idle watcher disabled")

    try:
        while True:
            message = _next_message(frame_io, pollable)
            if message is None:
                time.sleep(config.POLL_INTERVAL_S)
                continue
            if _dispatch(frame_io, message) == "shutdown":
                LOG.info("shutdown requested by client")
                break
    except (BrokenPipeError, EOFError, ConnectionError, OSError) as exc:
        LOG.info("pipe disconnected: %s", exc)
    finally:
        sender.stop()
        watcher.stop()
        _cleanup()
        frame_io.close()
        LOG.info("backend stopped")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description="ZIV.AI inference backend (IPC pipe client role)")
    parser.add_argument("--pipe-name", default=config.PIPE_PATH)
    parser.add_argument("--connect-timeout", type=float, default=config.CONNECT_TIMEOUT_S)
    parser.add_argument("--log-level", default="INFO")
    parser.add_argument("--log-file", default=None)
    args = parser.parse_args(argv)

    global LOG
    LOG = config.setup_logging(args.log_level, args.log_file)
    pipe_path = config.normalize_pipe_path(args.pipe_name)
    try:
        return run(pipe_path, args.connect_timeout)
    except Exception as exc:
        LOG.error("fatal: %s", exc)
        return 1


if __name__ == "__main__":
    sys.exit(main())
