"""Idle model unloading for the ZIV.AI backend (Step 3 / Z21).

A daemon thread polls the engine: when the model is loaded, no task is in
flight, and the idle time exceeds the configured timeout, it unloads the model
(releasing VRAM) without touching the process or the pipe.
"""

import logging
import threading

import config

_LOG = logging.getLogger("zivai.server")


class IdleWatcher(threading.Thread):
    def __init__(self, engine, is_busy, interval_s=None, idle_seconds=None):
        super().__init__(name="zivai-idle", daemon=True)
        self._engine = engine
        self._is_busy = is_busy
        self._interval = config.IDLE_CHECK_INTERVAL_S if interval_s is None else interval_s
        self._idle_seconds = config.IDLE_UNLOAD_SECONDS if idle_seconds is None else idle_seconds
        self._stop = threading.Event()

    def stop(self):
        self._stop.set()

    def run(self):
        # `wait` returns True once stopped, so a shutdown never waits a full tick.
        while not self._stop.wait(self._interval):
            try:
                self._tick()
            except Exception:
                _LOG.exception("idle watcher tick failed")

    def _tick(self):
        if not self._engine.is_loaded or self._is_busy():
            return
        idle = self._engine.seconds_idle()
        if idle is None or idle < self._idle_seconds:
            return
        if self._engine.unload():
            _LOG.info("idle unload after %.1fs (threshold %.1fs)", idle, self._idle_seconds)
