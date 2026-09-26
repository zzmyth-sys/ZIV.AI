"""CPU-only unit tests for the model lifecycle engine (A3 / A4).

No torch / comfy / GPU: the loaders and the VRAM release are replaced with
fakes, so the tests exercise only the locking / state-machine logic.

Run: ``python -m unittest test_engine`` from ``python/server``.
"""

import threading
import unittest
from datetime import datetime, timedelta, timezone

import engine
import idle_watcher

_PATHS = {
    "id": "test-model",
    "display_name": "test-model",
    "dit_path": "dit",
    "te_path": "te",
    "vae_path": "vae",
}


class EngineLifecycleTests(unittest.TestCase):
    def setUp(self):
        self._saved = {
            "release": engine._release_vram,
            "load_dit": engine.model_loader.load_dit,
            "load_te": engine.model_loader.load_text_encoder,
            "load_vae": engine.model_loader.load_vae,
            "resolve": engine.models.resolve_paths,
        }
        self.addCleanup(self._restore)

        engine.models.resolve_paths = lambda model_id=None: dict(_PATHS)
        engine.model_loader.load_dit = self._ok_loader("DIT")
        engine.model_loader.load_text_encoder = self._ok_loader("CLIP")
        engine.model_loader.load_vae = self._ok_loader("VAE")

        self.released = []
        engine._release_vram = lambda: self.released.append(1)

    def _restore(self):
        engine._release_vram = self._saved["release"]
        engine.model_loader.load_dit = self._saved["load_dit"]
        engine.model_loader.load_text_encoder = self._saved["load_te"]
        engine.model_loader.load_vae = self._saved["load_vae"]
        engine.models.resolve_paths = self._saved["resolve"]

    @staticmethod
    def _ok_loader(value):
        def loader(path=None, on_loaded=None):
            return value, 0.0

        return loader

    @staticmethod
    def _loaded_engine():
        eng = engine.ModelEngine()
        eng._state = engine.STATE_LOADED
        eng._dit = "DIT"
        eng._clip = "CLIP"
        eng._vae = "VAE"
        eng._model_id = _PATHS["id"]
        eng._model_paths = dict(_PATHS)
        eng._last_used_at = datetime.now(timezone.utc).isoformat()
        return eng

    # ---- A3: unload must not clear a busy engine -------------------------

    def test_unload_refuses_while_busy(self):
        eng = self._loaded_engine()

        self.assertFalse(eng.unload(is_busy=lambda: True))

        self.assertEqual(engine.STATE_LOADED, eng.status)
        self.assertEqual(("DIT", "CLIP", "VAE"), eng.components)
        self.assertEqual([], self.released, "vram must not be released on a refused unload")

    def test_unload_without_predicate_releases(self):
        eng = self._loaded_engine()

        self.assertTrue(eng.unload())

        self.assertEqual(engine.STATE_NOT_LOADED, eng.status)
        self.assertEqual((None, None, None), eng.components)
        self.assertEqual(1, len(self.released))

    def test_release_blocks_a_concurrent_load_until_finished(self):
        """INV2: a submit cannot obtain a half-released engine."""
        eng = self._loaded_engine()

        release_started = threading.Event()
        hold_release = threading.Event()

        def slow_release():
            self.released.append(1)
            release_started.set()
            hold_release.wait(10.0)

        engine._release_vram = slow_release
        engine.model_loader.load_dit = self._ok_loader("NEW_DIT")
        engine.model_loader.load_text_encoder = self._ok_loader("NEW_CLIP")
        engine.model_loader.load_vae = self._ok_loader("NEW_VAE")

        unload_done = threading.Event()
        threading.Thread(
            target=lambda: (eng.unload(is_busy=lambda: False), unload_done.set()),
            daemon=True,
        ).start()

        self.assertTrue(release_started.wait(5.0), "the unload should reach the vram release")

        submit_done = threading.Event()
        submit_result = {}

        def submit():
            try:
                submit_result["timings"] = eng.ensure_loaded()
            except Exception as exc:  # pragma: no cover - surfaced by the assertion below
                submit_result["error"] = exc
            finally:
                submit_done.set()

        threading.Thread(target=submit, daemon=True).start()

        # While the release is in flight the lazy load must be blocked: no
        # components may be handed out from a half-released engine.
        self.assertFalse(submit_done.wait(0.3), "ensure_loaded must wait for the release")

        hold_release.set()
        self.assertTrue(unload_done.wait(5.0))
        self.assertTrue(submit_done.wait(5.0))
        self.assertNotIn("error", submit_result)

        self.assertEqual(engine.STATE_LOADED, eng.status)
        self.assertEqual(("NEW_DIT", "NEW_CLIP", "NEW_VAE"), eng.components)

    def test_idle_watcher_skips_unload_while_busy(self):
        eng = self._loaded_engine()
        eng._last_used_at = (datetime.now(timezone.utc) - timedelta(seconds=100_000)).isoformat()
        watcher = idle_watcher.IdleWatcher(eng, is_busy=lambda: True, interval_s=999, idle_seconds=1)

        watcher._tick()

        self.assertEqual(engine.STATE_LOADED, eng.status)
        self.assertEqual([], self.released)

    def test_idle_watcher_unloads_when_idle(self):
        eng = self._loaded_engine()
        eng._last_used_at = (datetime.now(timezone.utc) - timedelta(seconds=100_000)).isoformat()
        watcher = idle_watcher.IdleWatcher(eng, is_busy=lambda: False, interval_s=999, idle_seconds=1)

        watcher._tick()

        self.assertEqual(engine.STATE_NOT_LOADED, eng.status)
        self.assertEqual(1, len(self.released))

    # ---- A4: failed load must release and re-raise -----------------------

    def test_failed_load_releases_vram_and_reraises(self):
        def boom(path=None, on_loaded=None):
            raise RuntimeError("load boom")

        engine.model_loader.load_text_encoder = boom
        eng = engine.ModelEngine()

        with self.assertRaises(RuntimeError):
            eng.ensure_loaded()

        self.assertEqual(1, len(self.released), "a failed load must release the partial weights")
        self.assertEqual(engine.STATE_NOT_LOADED, eng.status)
        self.assertEqual((None, None, None), eng.components)
        self.assertIsNone(eng._model_id)
        self.assertIsNone(eng._model_paths)

    def test_release_error_does_not_mask_the_load_error(self):
        def boom(path=None, on_loaded=None):
            raise RuntimeError("load boom")

        def release_boom():
            raise ValueError("release boom")

        engine.model_loader.load_vae = boom
        engine._release_vram = release_boom
        eng = engine.ModelEngine()

        with self.assertLogs("zivai.server", level="ERROR"):
            with self.assertRaisesRegex(RuntimeError, "load boom"):
                eng.ensure_loaded()

        self.assertEqual(engine.STATE_NOT_LOADED, eng.status)


if __name__ == "__main__":
    unittest.main()
