"""CPU-only tests for the A1 disconnect path (no GPU / no comfy).

Covers the tri-state pipe poll, the main read loop's reaction to a closed pipe,
the cancel poller's interrupt-on-close, and the shutdown VRAM release.

Run: ``python -m unittest test_disconnect`` from ``python/server``.
"""

import types
import unittest

import handlers
import ipc
import main


class _StubStream:
    """Stream stand-in without ``fileno``; FrameIO then leaves its handle unset."""


class _StubFrameIO:
    """Minimal FrameIO double driven by a scripted list of poll states."""

    def __init__(self, states, frame=None):
        self._states = list(states)
        self._frame = frame

    def poll_state(self):
        return self._states.pop(0)

    def read_frame(self):
        return self._frame

    def write_json(self, obj):
        raise AssertionError("unexpected write_json: %r" % (obj,))


def _peek(ok, available=0):
    def func(handle, buffer, size, read, available_out, left):
        available_out._obj.value = available
        return ok

    return func


class PipeStateTests(unittest.TestCase):
    def _frame_io(self):
        frame_io = ipc.FrameIO(_StubStream())
        frame_io._handle = 0x1234
        return frame_io

    def _patch_peek(self, func):
        saved = ipc._PEEK_NAMED_PIPE
        self.addCleanup(lambda: setattr(ipc, "_PEEK_NAMED_PIPE", saved))
        ipc._PEEK_NAMED_PIPE = func

    def test_peek_failure_is_closed(self):
        self._patch_peek(_peek(False))
        self.assertEqual(ipc.PIPE_CLOSED, self._frame_io().poll_state())

    def test_no_bytes_is_no_data(self):
        self._patch_peek(_peek(True, 0))
        self.assertEqual(ipc.PIPE_NO_DATA, self._frame_io().poll_state())

    def test_header_available_is_has_frame(self):
        self._patch_peek(_peek(True, 4))
        self.assertEqual(ipc.PIPE_HAS_FRAME, self._frame_io().poll_state())

    def test_missing_handle_is_no_data(self):
        frame_io = ipc.FrameIO(_StubStream())
        self.assertIsNone(frame_io._handle)
        self.assertEqual(ipc.PIPE_NO_DATA, frame_io.poll_state())


class NextMessageTests(unittest.TestCase):
    def test_closed_raises_eof(self):
        with self.assertRaises(EOFError):
            main._next_message(_StubFrameIO([ipc.PIPE_CLOSED]), pollable=True)

    def test_no_data_returns_none(self):
        self.assertIsNone(main._next_message(_StubFrameIO([ipc.PIPE_NO_DATA]), pollable=True))

    def test_has_frame_is_decoded(self):
        frame = (ipc.FRAME_JSON, ipc.encode_json({"type": "ping", "request_id": "r1"}))
        stub = _StubFrameIO([ipc.PIPE_HAS_FRAME], frame=frame)
        self.assertEqual({"type": "ping", "request_id": "r1"}, main._next_message(stub, pollable=True))

    def test_blocking_path_reads_frame(self):
        frame = (ipc.FRAME_JSON, ipc.encode_json({"type": "ping"}))
        self.assertEqual({"type": "ping"}, main._next_message(_StubFrameIO([], frame=frame), pollable=False))

    def test_blocking_path_eof_raises(self):
        with self.assertRaises(EOFError):
            main._next_message(_StubFrameIO([], frame=None), pollable=False)


class CancelPollerTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        saved = handlers._interrupt_processing
        self.addCleanup(lambda: setattr(handlers, "_interrupt_processing", saved))
        handlers._interrupt_processing = lambda: self.calls.append(1)

    def test_closed_interrupts_the_task(self):
        handlers._make_cancel_poller(_StubFrameIO([ipc.PIPE_CLOSED]))()
        self.assertEqual([1], self.calls)

    def test_no_data_does_not_interrupt(self):
        handlers._make_cancel_poller(_StubFrameIO([ipc.PIPE_NO_DATA]))()
        self.assertEqual([], self.calls)

    def test_cancel_frame_interrupts_the_task(self):
        frame = (ipc.FRAME_JSON, ipc.encode_json({"type": "cancel", "task_id": "t"}))
        stub = _StubFrameIO([ipc.PIPE_HAS_FRAME, ipc.PIPE_NO_DATA], frame=frame)
        handlers._make_cancel_poller(stub)()
        self.assertEqual([1], self.calls)


class CleanupTests(unittest.TestCase):
    def _patch_engine(self, engine):
        saved = main.handlers._ENGINE
        self.addCleanup(lambda: setattr(main.handlers, "_ENGINE", saved))
        main.handlers._ENGINE = engine

    def test_cleanup_releases_the_model(self):
        calls = []
        self._patch_engine(types.SimpleNamespace(unload=lambda: calls.append(1)))

        main._cleanup()

        self.assertEqual([1], calls)

    def test_cleanup_guards_a_release_failure(self):
        def boom():
            raise RuntimeError("unload boom")

        self._patch_engine(types.SimpleNamespace(unload=boom))

        with self.assertLogs("zivai.server", level="ERROR"):
            main._cleanup()  # must not raise


if __name__ == "__main__":
    unittest.main()
