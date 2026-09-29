"""CPU-only tests for ``mem_guard`` (host-RAM / shared-GPU-memory guard).

纯 stdlib ``unittest``；只测阈值逻辑与结构（用注入的 snapshot，不依赖真实系统状态）。
从 ``python/server`` 运行：``python -m unittest test_mem_guard``。
"""

import subprocess
import sys
import unittest
from unittest import mock

import config
import mem_guard

GIB = 1024 ** 3


def _snap(avail_phys_gb=32.0, avail_commit_gb=32.0, rss_gb=8.0):
    return {
        "total_phys": 96 * GIB,
        "avail_phys": int(avail_phys_gb * GIB),
        "total_commit": 128 * GIB,
        "avail_commit": int(avail_commit_gb * GIB),
        "rss": int(rss_gb * GIB),
    }


class GuardLogicTests(unittest.TestCase):
    def _cfg(self, **overrides):
        base = {
            "GUARD_ENABLED": True,
            "GUARD_MIN_FREE_RAM_GB": 4.0,
            "GUARD_MIN_FREE_COMMIT_GB": 4.0,
            "GUARD_MAX_RSS_GB": 0.0,
        }
        base.update(overrides)
        return mock.patch.multiple(config, **base)

    def test_under_budget_returns_none(self):
        with self._cfg():
            self.assertIsNone(mem_guard.check(_snap(avail_phys_gb=32)))

    def test_low_free_ram_trips(self):
        with self._cfg():
            reason = mem_guard.check(_snap(avail_phys_gb=2))
        self.assertIsNotNone(reason)
        self.assertIn("physical RAM", reason)

    def test_low_free_commit_trips(self):
        with self._cfg():
            reason = mem_guard.check(_snap(avail_commit_gb=1))
        self.assertIsNotNone(reason)
        self.assertIn("commit", reason)

    def test_rss_ceiling_trips_when_configured(self):
        with self._cfg(GUARD_MAX_RSS_GB=10.0):
            reason = mem_guard.check(_snap(rss_gb=12))
        self.assertIsNotNone(reason)
        self.assertIn("RSS", reason)

    def test_rss_ignored_when_zero(self):
        with self._cfg(GUARD_MAX_RSS_GB=0.0):
            self.assertIsNone(mem_guard.check(_snap(rss_gb=999)))

    def test_disabled_master_switch(self):
        with self._cfg(GUARD_ENABLED=False):
            self.assertIsNone(mem_guard.check(_snap(avail_phys_gb=0)))

    def test_enforce_raises_over_budget(self):
        with self._cfg():
            with mock.patch.object(mem_guard, "snapshot", return_value=_snap(avail_phys_gb=1)):
                with self.assertRaises(mem_guard.HostMemoryError):
                    mem_guard.enforce()

    def test_enforce_is_noop_under_budget(self):
        with self._cfg():
            with mock.patch.object(mem_guard, "snapshot", return_value=_snap()):
                mem_guard.enforce()

    def test_host_memory_error_is_runtime_error(self):
        self.assertTrue(issubclass(mem_guard.HostMemoryError, RuntimeError))


class SnapshotTests(unittest.TestCase):
    def test_snapshot_off_windows_is_none(self):
        with mock.patch.object(mem_guard, "_kernel32", return_value=None):
            self.assertIsNone(mem_guard.snapshot())

    @unittest.skipUnless(sys.platform == "win32", "Windows-only API")
    def test_snapshot_shape_on_windows(self):
        snap = mem_guard.snapshot()
        self.assertIsNotNone(snap)
        for key in ("total_phys", "avail_phys", "total_commit", "avail_commit", "rss"):
            self.assertIn(key, snap)
        self.assertGreater(snap["total_phys"], 0)
        self.assertLessEqual(snap["avail_phys"], snap["total_phys"])


class CpuOnlyImportTests(unittest.TestCase):
    def test_import_does_not_touch_heavy_stack(self):
        import os

        server_dir = os.path.dirname(os.path.abspath(mem_guard.__file__))
        code = (
            "import sys\n"
            "sys.path.insert(0, %r)\n"
            "import mem_guard\n"
            "assert 'torch' not in sys.modules, 'imported torch'\n"
            "assert 'comfy' not in sys.modules, 'imported comfy'\n"
            "print('ok')\n"
        ) % (server_dir,)
        result = subprocess.run(
            [sys.executable, "-c", code], capture_output=True, text=True, timeout=120
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "ok")


if __name__ == "__main__":
    unittest.main()
