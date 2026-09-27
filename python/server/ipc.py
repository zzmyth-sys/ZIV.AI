import ctypes
import json
import math
import os
import struct
import threading
from ctypes import wintypes

FRAME_JSON = 0x01
FRAME_BINARY = 0x02

MAX_FRAME_BYTES = 256 * 1024 * 1024

# `FrameIO.poll_state()` results (A1): distinguish "nothing yet" from "peer gone".
PIPE_NO_DATA = 0
PIPE_HAS_FRAME = 1
PIPE_CLOSED = 2


def sanitize(value):
    if isinstance(value, float):
        if math.isnan(value) or math.isinf(value):
            return None
        return value
    if isinstance(value, dict):
        return {str(key): sanitize(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [sanitize(item) for item in value]
    return value


def encode_json(obj):
    return json.dumps(sanitize(obj), ensure_ascii=False, allow_nan=False).encode("utf-8")


def decode_json(payload):
    return json.loads(payload.decode("utf-8"))


def _read_exact(stream, size):
    chunks = []
    remaining = size
    while remaining > 0:
        chunk = stream.read(remaining)
        if not chunk:
            if remaining == size:
                return None
            raise EOFError("pipe closed while reading %d bytes" % size)
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks) if len(chunks) > 1 else chunks[0]


def _read_exact_fd(fd, size):
    chunks = []
    remaining = size
    while remaining > 0:
        chunk = os.read(fd, remaining)
        if not chunk:
            if remaining == size:
                return None
            raise EOFError("pipe closed while reading %d bytes" % size)
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks) if len(chunks) > 1 else chunks[0]


_PEEK_NAMED_PIPE = None


def _peek_named_pipe():
    global _PEEK_NAMED_PIPE
    if _PEEK_NAMED_PIPE is None:
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        func = kernel32.PeekNamedPipe
        func.restype = wintypes.BOOL
        func.argtypes = [
            wintypes.HANDLE,
            wintypes.LPVOID,
            wintypes.DWORD,
            wintypes.LPDWORD,
            wintypes.LPDWORD,
            wintypes.LPDWORD,
        ]
        _PEEK_NAMED_PIPE = func
    return _PEEK_NAMED_PIPE


def _os_handle(fd):
    import msvcrt

    try:
        return msvcrt.get_osfhandle(fd)
    except (OSError, ValueError):
        return None


def _write_all(stream, data):
    view = memoryview(data)
    while view:
        written = stream.write(view)
        if written is None:
            written = len(view)
        if written <= 0:
            raise BrokenPipeError("pipe write returned %d bytes" % written)
        view = view[written:]


class FrameIO:
    """Length-prefixed frame reader/writer.

    Reads go through `os.read` on the raw file descriptor: a plain `FileIO` is
    guarded by a single lock, so a blocking read on the main loop would block a
    worker thread's write (deadlock). Raw `os.read` does not take that lock,
    which is what lets `cancel` arrive while the sampler runs. Writes stay on
    the stream object (serialized by `_write_lock`).
    """

    def __init__(self, stream):
        self._stream = stream
        try:
            self._fd = stream.fileno()
        except (AttributeError, OSError, ValueError):
            self._fd = None
        self._handle = _os_handle(self._fd) if self._fd is not None else None
        self._write_lock = threading.Lock()

    @property
    def pollable(self):
        """True when `poll_state()` can probe the pipe (Windows handle)."""
        return self._handle is not None

    def poll_state(self):
        """Non-blocking pipe poll: ``PIPE_NO_DATA`` / ``PIPE_HAS_FRAME`` / ``PIPE_CLOSED`` (A1).

        ``PeekNamedPipe`` reports both "no bytes yet" and "peer gone". Only the
        first is a reason to keep waiting, so a failed peek (broken pipe,
        disconnected, or an unexpected error) is reported as ``PIPE_CLOSED``.
        That is deliberately fail-closed: the old boolean could not tell the two
        apart, so a vanished client left the read loop spinning as "no data"
        forever and never released the loaded model.
        """
        if self._handle is None:
            return PIPE_NO_DATA
        available = wintypes.DWORD(0)
        ok = _peek_named_pipe()(
            wintypes.HANDLE(self._handle), None, 0, None, ctypes.byref(available), None
        )
        if not ok:
            return PIPE_CLOSED
        return PIPE_HAS_FRAME if available.value >= 4 else PIPE_NO_DATA

    def _read(self, size):
        if self._fd is not None:
            return _read_exact_fd(self._fd, size)
        return _read_exact(self._stream, size)

    def read_frame(self):
        header = self._read(4)
        if header is None:
            return None
        (length,) = struct.unpack("<i", header)
        if length < 1 or length > MAX_FRAME_BYTES:
            raise ValueError("invalid frame length %d" % length)
        body = self._read(length)
        if body is None:
            raise EOFError("pipe closed while reading frame body")
        return body[0], body[1:]

    def write_frame(self, frame_type, payload):
        with self._write_lock:
            self._write_frame_locked(frame_type, payload)

    def _write_frame_locked(self, frame_type, payload):
        if not 0 <= frame_type <= 0xFF:
            raise ValueError("frame_type out of range: %r" % (frame_type,))
        if len(payload) > MAX_FRAME_BYTES - 1:
            raise ValueError("frame payload too large: %d bytes" % len(payload))
        header = struct.pack("<iB", len(payload) + 1, frame_type)
        _write_all(self._stream, header)
        if payload:
            _write_all(self._stream, payload)
        flush = getattr(self._stream, "flush", None)
        if flush is not None:
            try:
                flush()
            except OSError:
                pass

    def write_json(self, obj):
        self.write_frame(FRAME_JSON, encode_json(obj))

    def build_preview_payload(self, task_id, jpeg_bytes):
        """Payload for a 0x02 preview frame: [4B id][id][4B JPEG len][JPEG]."""
        task = (task_id or "").encode("utf-8")
        return (
            struct.pack("<i", len(task))
            + task
            + struct.pack("<i", len(jpeg_bytes))
            + jpeg_bytes
        )

    def write_preview(self, task_id, step, total, jpeg_bytes):
        """Atomically send the `preview` control frame + its 0x02 binary frame."""
        control = encode_json(
            {"type": "preview", "task_id": task_id, "step": step, "total": total}
        )
        payload = self.build_preview_payload(task_id, jpeg_bytes)
        with self._write_lock:
            self._write_frame_locked(FRAME_JSON, control)
            self._write_frame_locked(FRAME_BINARY, payload)

    def close(self):
        try:
            self._stream.close()
        except OSError:
            pass
