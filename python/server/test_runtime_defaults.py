"""CPU-only tests for env-gated ComfyUI runtime defaults.

``model_loader._apply_runtime_defaults`` flips ``comfy.cli_args.args`` flags before the
first ``import comfy.model_management``. These tests inject a fake ``comfy.cli_args`` (no
torch / comfy import) and assert the ``DISABLE_PINNED_MEMORY`` gate, plus that the flags
stay independent (mirrors the DISABLE_SMART_MEMORY contract).

从 ``python/server`` 运行：``python -m unittest test_runtime_defaults``。
"""

import sys
import types
import unittest
from unittest import mock

import config
import model_loader


def _fake_cli_args():
    pkg = types.ModuleType("comfy")
    ca = types.ModuleType("comfy.cli_args")
    ca.args = types.SimpleNamespace(
        disable_smart_memory=False,
        use_sage_attention=False,
        disable_pinned_memory=False,
    )
    pkg.cli_args = ca
    return pkg, ca


class RuntimeDefaultsTests(unittest.TestCase):
    def _apply(self, **cfg):
        pkg, ca = _fake_cli_args()
        base = dict(DISABLE_SMART_MEMORY=False, DISABLE_PINNED_MEMORY=False, SAGE_ATTENTION=False)
        base.update(cfg)
        with mock.patch.dict(sys.modules, {"comfy": pkg, "comfy.cli_args": ca}), \
                mock.patch.multiple(config, **base):
            model_loader._apply_runtime_defaults()
        return ca.args

    def test_pinned_on_sets_flag(self):
        args = self._apply(DISABLE_PINNED_MEMORY=True)
        self.assertTrue(args.disable_pinned_memory)

    def test_pinned_off_leaves_flag(self):
        args = self._apply(DISABLE_PINNED_MEMORY=False)
        self.assertFalse(args.disable_pinned_memory)

    def test_flags_independent(self):
        args = self._apply(DISABLE_SMART_MEMORY=True, DISABLE_PINNED_MEMORY=False)
        self.assertTrue(args.disable_smart_memory)
        self.assertFalse(args.disable_pinned_memory)


class ConfigEnvParsingTests(unittest.TestCase):
    """``config.DISABLE_PINNED_MEMORY`` 仅在 env == "1" 时为真。"""

    def _read(self, value):
        import os
        import subprocess

        env = os.environ.copy()
        env.pop("ZIV_AI_DISABLE_PINNED_MEMORY", None)
        if value is not None:
            env["ZIV_AI_DISABLE_PINNED_MEMORY"] = value
        server_dir = os.path.dirname(os.path.abspath(config.__file__))
        code = (
            "import sys\n"
            "sys.path.insert(0, %r)\n"
            "import config\n"
            "print(config.DISABLE_PINNED_MEMORY)\n"
        ) % (server_dir,)
        result = subprocess.run(
            [sys.executable, "-c", code], capture_output=True, text=True, timeout=120, env=env
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        return result.stdout.strip()

    def test_default_off(self):
        self.assertEqual(self._read(None), "False")

    def test_one_is_true(self):
        self.assertEqual(self._read("1"), "True")

    def test_other_values_false(self):
        self.assertEqual(self._read("0"), "False")
        self.assertEqual(self._read("true"), "False")


if __name__ == "__main__":
    unittest.main()
