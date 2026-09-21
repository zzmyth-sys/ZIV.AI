"""Model lifecycle management for the ZIV.AI inference backend (Step 2.2 / 3).

States: `not_loaded` -> `loading` -> `loaded`. The first `submit` triggers a
lazy load (Z18 keeps GPU work serial). Idle unloading (Z21) releases the VRAM
while keeping the process and pipe alive; the next `submit` reloads lazily.
"""

import logging
import threading
import time
from datetime import datetime, timezone

import config
import model_loader

STATE_NOT_LOADED = "not_loaded"
STATE_LOADING = "loading"
STATE_LOADED = "loaded"

STAGE_LOADING_MODEL = "loading_model"

_LOG = logging.getLogger("zivai.server")


def _release_vram():
    """Drop ComfyUI-managed models and return VRAM to the driver (Z21)."""
    try:
        import comfy.model_management as mm

        # ComfyUI's registry (`current_loaded_models`) keeps the patchers alive,
        # so deleting our references alone would not free the weights.
        mm.unload_all_models()
        mm.soft_empty_cache(force=True)
    except Exception:
        pass
    try:
        import gc

        gc.collect()
    except Exception:
        pass
    try:
        import torch

        if torch.cuda.is_available():
            torch.cuda.empty_cache()
    except Exception:
        pass


class ModelEngine:
    def __init__(self):
        self._lock = threading.RLock()
        self._state = STATE_NOT_LOADED
        self._dit = None
        self._clip = None
        self._vae = None
        self._timings = {}
        self._total_seconds = None
        self._last_used_at = None
        self._notify = None

    @property
    def status(self):
        with self._lock:
            return self._state

    @property
    def is_loaded(self):
        return self.status == STATE_LOADED

    @property
    def timings(self):
        with self._lock:
            return dict(self._timings)

    @property
    def total_seconds(self):
        with self._lock:
            return self._total_seconds

    @property
    def components(self):
        """The loaded (dit, clip, vae) triple; ``(None, None, None)`` if unloaded."""
        with self._lock:
            return self._dit, self._clip, self._vae

    @property
    def models(self):
        with self._lock:
            return [
                {
                    "name": config.MODEL_NAME,
                    "loaded": self._state == STATE_LOADED,
                    "last_used_at": self._last_used_at,
                }
            ]

    def ensure_loaded(self, notify=None):
        """Lazy-load the triple once. Returns per-stage timings (seconds)."""
        with self._lock:
            if self._state == STATE_LOADED:
                self._touch()
                return dict(self._timings)

            self._state = STATE_LOADING
            self._notify = notify
            started = time.time()
            try:
                self._load_dit()
                self._load_text_encoder()
                self._load_vae()
            except BaseException:
                self._state = STATE_NOT_LOADED
                self._timings = {}
                self._dit = None
                self._clip = None
                self._vae = None
                raise
            finally:
                self._notify = None

            self._total_seconds = round(time.time() - started, 3)
            self._state = STATE_LOADED
            self._touch()
            return dict(self._timings)

    def touch(self):
        """Mark the model as used now (called when a task finishes)."""
        with self._lock:
            self._touch()

    def seconds_idle(self):
        """Seconds since the last use, or ``None`` if never used."""
        with self._lock:
            if self._last_used_at is None:
                return None
            try:
                last_used = datetime.fromisoformat(self._last_used_at)
            except ValueError:
                return None
        return (datetime.now(timezone.utc) - last_used).total_seconds()

    def unload(self):
        """Release DiT / TE / VAE and drop back to `not_loaded` (Z21).

        The process and pipe stay alive; `ensure_loaded()` reloads lazily on the
        next submit. The last-used timestamp is kept so the watch can reason
        about reload timing. Returns ``True`` when something was unloaded.
        """
        with self._lock:
            if self._state != STATE_LOADED:
                return False
            self._notify = None
            self._dit = None
            self._clip = None
            self._vae = None
            self._timings = {}
            self._total_seconds = None
            self._state = STATE_NOT_LOADED

        _release_vram()
        _LOG.info("model unloaded (Z21 idle release)")
        return True

    def _load_dit(self):
        self._emit("dit", model_loader.STAGE_FRACTIONS["dit"][0], "loading_model:dit")
        self._dit, _ = model_loader.load_dit(config.DIT_MODEL_PATH, self._on_model_loaded)

    def _load_text_encoder(self):
        self._emit("te", model_loader.STAGE_FRACTIONS["te"][0], "loading_model:te")
        self._clip, _ = model_loader.load_text_encoder(
            config.TEXT_ENCODER_PATH, self._on_model_loaded
        )

    def _load_vae(self):
        self._emit("vae", model_loader.STAGE_FRACTIONS["vae"][0], "loading_model:vae")
        self._vae, _ = model_loader.load_vae(config.VAE_PATH, self._on_model_loaded)

    def _on_model_loaded(self, sub_stage, fraction, elapsed):
        self._timings[sub_stage] = round(elapsed, 3)
        self._emit(sub_stage, fraction, "loaded:" + sub_stage)

    def _emit(self, sub_stage, fraction, message):
        notify = self._notify
        if notify is not None:
            notify(STAGE_LOADING_MODEL, sub_stage, fraction, message)

    def _touch(self):
        self._last_used_at = datetime.now(timezone.utc).isoformat()
