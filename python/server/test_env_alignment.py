"""CPU-only tests for official-env alignment (``config.apply_official_env``).

Mirrors the DISABLE_* env-gate style. No GPU / no torch import.

从 ``python/server`` 运行：``python -m unittest test_env_alignment``。
"""

import os
import subprocess
import sys
import unittest
from unittest import mock

import config

_VARS = ("PYTORCH_CUDA_ALLOC_CONF", "MIMALLOC_PURGE_DELAY", "CUDA_VISIBLE_DEVICES")
_DEFAULTS = dict(
    ALIGN_CUDA_MALLOC_ASYNC=True, ALIGN_MIMALLOC_PURGE=True, ALIGN_CUDA_VISIBLE_DEVICES=False
)


class ApplyOfficialEnvTests(unittest.TestCase):
    def _apply(self, pre=None, **cfg):
        base = dict(_DEFAULTS)
        base.update(cfg)
        pre = pre or {}
        with mock.patch.dict(os.environ, {}, clear=False):
            for key in _VARS:
                os.environ.pop(key, None)
            os.environ.update(pre)
            with mock.patch.multiple(config, **base):
                config.apply_official_env()
            return {key: os.environ.get(key) for key in _VARS}

    def test_defaults_align_cuda_malloc_and_mimalloc(self):
        out = self._apply()
        self.assertEqual(out["PYTORCH_CUDA_ALLOC_CONF"], "backend:cudaMallocAsync")
        self.assertEqual(out["MIMALLOC_PURGE_DELAY"], "0")
        self.assertIsNone(out["CUDA_VISIBLE_DEVICES"])  # hardware-bound -> default off

    def test_preserves_existing_alloc_conf(self):
        out = self._apply(pre={"PYTORCH_CUDA_ALLOC_CONF": "expandable_segments:True"})
        self.assertEqual(out["PYTORCH_CUDA_ALLOC_CONF"], "expandable_segments:True,backend:cudaMallocAsync")

    def test_idempotent(self):
        with mock.patch.dict(os.environ, {}, clear=False):
            for key in _VARS:
                os.environ.pop(key, None)
            with mock.patch.multiple(config, **_DEFAULTS):
                config.apply_official_env()
                config.apply_official_env()
            self.assertEqual(os.environ["PYTORCH_CUDA_ALLOC_CONF"], "backend:cudaMallocAsync")

    def test_gates_off(self):
        out = self._apply(ALIGN_CUDA_MALLOC_ASYNC=False, ALIGN_MIMALLOC_PURGE=False)
        self.assertIsNone(out["PYTORCH_CUDA_ALLOC_CONF"])
        self.assertIsNone(out["MIMALLOC_PURGE_DELAY"])

    def test_cuda_visible_devices_gate_on(self):
        out = self._apply(ALIGN_CUDA_VISIBLE_DEVICES=True)
        self.assertEqual(out["CUDA_VISIBLE_DEVICES"], "0")


class ConfigEnvParsingTests(unittest.TestCase):
    """Gate defaults come from env at import: cuda_malloc/mimalloc ON, cuda_visible OFF."""

    def _read(self, env_overrides):
        env = os.environ.copy()
        for key in ("ZIV_AI_CUDA_MALLOC_ASYNC", "ZIV_AI_MIMALLOC_PURGE_DELAY", "ZIV_AI_CUDA_VISIBLE_DEVICES"):
            env.pop(key, None)
        env.update(env_overrides)
        server_dir = os.path.dirname(os.path.abspath(config.__file__))
        code = (
            "import sys\n"
            "sys.path.insert(0, %r)\n"
            "import config\n"
            "print(config.ALIGN_CUDA_MALLOC_ASYNC, config.ALIGN_MIMALLOC_PURGE,"
            " config.ALIGN_CUDA_VISIBLE_DEVICES)\n"
        ) % (server_dir,)
        result = subprocess.run(
            [sys.executable, "-c", code], capture_output=True, text=True, timeout=120, env=env
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        return result.stdout.strip()

    def test_defaults(self):
        self.assertEqual(self._read({}), "True True False")

    def test_overrides(self):
        self.assertEqual(
            self._read({"ZIV_AI_CUDA_MALLOC_ASYNC": "0", "ZIV_AI_CUDA_VISIBLE_DEVICES": "1"}),
            "False True True",
        )


if __name__ == "__main__":
    unittest.main()
