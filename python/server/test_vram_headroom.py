"""CPU-only tests for the DynamicVRAM headroom injection (方案 1).

覆盖 ``config`` 的 env 解析与 ``model_loader`` 的两个纯计算函数
（``_simple_vram_headroom`` / ``_device_vram_headroom``）。只测「0 = 不注入」与
「非 0 = 注入正确字节数」，不 import torch / comfy，不初始化 GPU。

从 ``python/server`` 运行：``python -m unittest test_vram_headroom``。
"""

import os
import subprocess
import sys
import types
import unittest
from unittest import mock

import config
import model_loader

MIB = 1024 ** 2
GIB = 1024 ** 3

SERVER_DIR = os.path.dirname(os.path.abspath(__file__))


def _fake_ca(reserve_vram=None, vram_headroom=0):
    return types.SimpleNamespace(
        args=types.SimpleNamespace(reserve_vram=reserve_vram, vram_headroom=vram_headroom)
    )


class DeviceHeadroomTests(unittest.TestCase):
    """``_device_vram_headroom`` → ``control.init_devices`` 的每设备余量。"""

    def test_zero_keeps_cli_default(self):
        with mock.patch.object(config, "VRAM_HEADROOM_MB", 0):
            self.assertEqual(model_loader._device_vram_headroom(_fake_ca()), 0)
            self.assertEqual(
                model_loader._device_vram_headroom(_fake_ca(vram_headroom=1.5)),
                int(1.5 * GIB),
            )

    def test_positive_injects_mb_as_bytes(self):
        with mock.patch.object(config, "VRAM_HEADROOM_MB", 300):
            self.assertEqual(
                model_loader._device_vram_headroom(_fake_ca(vram_headroom=1.5)), 300 * MIB
            )


class SimpleHeadroomTests(unittest.TestCase):
    """``_simple_vram_headroom`` → ``control.init`` 的进程级余量。"""

    def test_zero_and_unset_reserve_is_none(self):
        with mock.patch.object(config, "VRAM_RESERVE_MB", 0):
            self.assertIsNone(model_loader._simple_vram_headroom(_fake_ca()))

    def test_zero_keeps_cli_reserve_in_gb(self):
        with mock.patch.object(config, "VRAM_RESERVE_MB", 0):
            self.assertEqual(
                model_loader._simple_vram_headroom(_fake_ca(reserve_vram=2.0)), 2 * GIB
            )

    def test_positive_overrides_cli_reserve(self):
        with mock.patch.object(config, "VRAM_RESERVE_MB", 512):
            self.assertEqual(
                model_loader._simple_vram_headroom(_fake_ca(reserve_vram=2.0)), 512 * MIB
            )


class ConfigEnvParsingTests(unittest.TestCase):
    """config 在 import 时从 env 读两个 MB 值（默认 0，非法 → 0）。"""

    def _read(self, env_overrides):
        env = os.environ.copy()
        env.pop("ZIV_AI_VRAM_HEADROOM_MB", None)
        env.pop("ZIV_AI_VRAM_RESERVE_MB", None)
        env.update(env_overrides)
        code = (
            "import sys\n"
            "sys.path.insert(0, %r)\n"
            "import config\n"
            "print(config.VRAM_HEADROOM_MB, config.VRAM_RESERVE_MB)\n"
        ) % (SERVER_DIR,)
        result = subprocess.run(
            [sys.executable, "-c", code], capture_output=True, text=True, timeout=120, env=env
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        return result.stdout.strip()

    def test_defaults_are_zero(self):
        self.assertEqual(self._read({}), "0 0")

    def test_env_overrides_are_read(self):
        self.assertEqual(
            self._read({"ZIV_AI_VRAM_HEADROOM_MB": "300", "ZIV_AI_VRAM_RESERVE_MB": "512"}),
            "300 512",
        )

    def test_invalid_env_falls_back_to_zero(self):
        self.assertEqual(self._read({"ZIV_AI_VRAM_HEADROOM_MB": "abc"}), "0 0")


if __name__ == "__main__":
    unittest.main()
