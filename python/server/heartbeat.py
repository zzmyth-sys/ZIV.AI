"""Backend -> client heartbeat for the ZIV.AI backend (Step 3).

A daemon thread emits a `heartbeat` control frame every
`config.HEARTBEAT_INTERVAL_S` seconds carrying the current NVML VRAM figure and
the in-flight task id (or ``null``). C# treats missed heartbeats as a lost
backend (see `contracts/ipc-protocol.md` §3.2).

Writes share `FrameIO`'s write lock with the main loop; the main loop never
blocks on a read (it polls with `PeekNamedPipe`), so this thread cannot be
starved by the fd lock described in the Step 2.4 devlog.
"""

import logging
import threading

import config

_LOG = logging.getLogger("zivai.server")


class HeartbeatSender(threading.Thread):
    def __init__(self, frame_io, vram_provider, task_id_provider, interval_s=None, disabled=None):
        super().__init__(name="zivai-heartbeat", daemon=True)
        self._frame_io = frame_io
        self._vram_provider = vram_provider
        self._task_id_provider = task_id_provider
        self._interval = config.HEARTBEAT_INTERVAL_S if interval_s is None else interval_s
        self._disabled = config.HEARTBEAT_DISABLED if disabled is None else disabled
        self._stop = threading.Event()

    def stop(self):
        self._stop.set()

    def run(self):
        if self._disabled:
            _LOG.info("heartbeat disabled by configuration")
            return
        while not self._stop.wait(self._interval):
            try:
                self._frame_io.write_json(
                    {
                        "type": "heartbeat",
                        "vram_used_mb": float(self._vram_provider()),
                        "current_task_id": self._task_id_provider(),
                    }
                )
            except Exception as exc:
                # A dead pipe ends the heartbeat; the main loop will exit too.
                _LOG.info("heartbeat stopped: %s", exc)
                return
