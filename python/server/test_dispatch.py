"""CPU-only tests for the batch-3 plugin capability dispatch (``sampling_plan``).

纯 stdlib ``unittest``；只 import ``plugins.dispatch`` / ``plugins.loader``（内部只依赖
``config``）与 ``loras``，不触碰 ``torch`` / ``comfy`` / GPU。插件加载隔离测试另起子进程
（同样不 import 重栈）。从 ``python/server`` 运行：::

    python -m unittest test_plugins test_dispatch
"""

import json
import os
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

import config
import loras
from plugins import dispatch, loader

PLUGIN_ID = "qwen21-viggle-6step"


class ActivePluginsTests(unittest.TestCase):
    def setUp(self):
        loader._MODULES.clear()
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = self._tmp.name

    def _make_plugin(self, name, body):
        directory = os.path.join(self.root, "plugins", name)
        os.makedirs(directory, exist_ok=True)
        with open(os.path.join(directory, "__init__.py"), "w", encoding="utf-8") as handle:
            handle.write(body)
        return directory

    def _write_registry(self, entries):
        path = os.path.join(self.root, "plugins.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"version": "1", "plugins": entries}, handle)
        return path

    def _install(self, entries):
        path = self._write_registry(entries)
        patcher = mock.patch.multiple(
            config, PLUGINS_REGISTRY_PATH=path, PLUGINS_BASE_DIR=self.root
        )
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_only_enabled_plugins_are_active(self):
        self._make_plugin("on", "def sampling_plan(context):\n    return {'from': 'on'}\n")
        self._make_plugin("off", "def sampling_plan(context):\n    return {'from': 'off'}\n")
        self._install([
            {"id": "on", "dir": "plugins/on", "enabled_by_default": True},
            {"id": "off", "dir": "plugins/off", "enabled_by_default": False},
        ])
        active = [plugin_id for plugin_id, _ in dispatch.active_plugins()]
        self.assertEqual(active, ["on"])

    def test_call_returns_first_non_none(self):
        self._make_plugin("first", "def sampling_plan(context):\n    return {'from': 'first'}\n")
        self._make_plugin("second", "def sampling_plan(context):\n    return {'from': 'second'}\n")
        self._install([
            {"id": "first", "dir": "plugins/first", "enabled_by_default": True},
            {"id": "second", "dir": "plugins/second", "enabled_by_default": True},
        ])
        self.assertEqual(dispatch.call("sampling_plan", {}), {"from": "first"})

    def test_call_skips_declining_plugins(self):
        self._make_plugin("none", "def sampling_plan(context):\n    return None\n")
        self._make_plugin("yes", "def sampling_plan(context):\n    return {'from': 'yes'}\n")
        self._install([
            {"id": "none", "dir": "plugins/none", "enabled_by_default": True},
            {"id": "yes", "dir": "plugins/yes", "enabled_by_default": True},
        ])
        self.assertEqual(dispatch.call("sampling_plan", {}), {"from": "yes"})

    def test_call_is_none_when_all_decline(self):
        self._make_plugin("none", "def sampling_plan(context):\n    return None\n")
        self._install([{"id": "none", "dir": "plugins/none", "enabled_by_default": True}])
        self.assertIsNone(dispatch.call("sampling_plan", {}))

    def test_call_never_throws(self):
        self._make_plugin("boom", "def sampling_plan(context):\n    raise RuntimeError('boom')\n")
        self._make_plugin("ok", "def sampling_plan(context):\n    return {'from': 'ok'}\n")
        self._install([
            {"id": "boom", "dir": "plugins/boom", "enabled_by_default": True},
            {"id": "ok", "dir": "plugins/ok", "enabled_by_default": True},
        ])
        self.assertEqual(dispatch.call("sampling_plan", {}), {"from": "ok"})

    def test_call_ignores_plugins_without_capability(self):
        self._make_plugin("silent", "MARKER = 1\n")
        self._make_plugin("ok", "def sampling_plan(context):\n    return {'from': 'ok'}\n")
        self._install([
            {"id": "silent", "dir": "plugins/silent", "enabled_by_default": True},
            {"id": "ok", "dir": "plugins/ok", "enabled_by_default": True},
        ])
        self.assertEqual(dispatch.call("sampling_plan", {}), {"from": "ok"})

    def test_unknown_capability_is_none(self):
        self._make_plugin("ok", "def sampling_plan(context):\n    return {'from': 'ok'}\n")
        self._install([{"id": "ok", "dir": "plugins/ok", "enabled_by_default": True}])
        self.assertIsNone(dispatch.call("not_a_capability", {}))


class QwenPluginTests(unittest.TestCase):
    def setUp(self):
        loader._MODULES.clear()
        os.environ.pop(config.plugin_env_name(PLUGIN_ID), None)
        module, status = loader.load_plugin(PLUGIN_ID)
        self.assertIsNotNone(module, status)
        self.module = module

    def _context(self, **overrides):
        context = {
            "op": "inpaint",
            "model": object(),
            "clip": object(),
            "vae": object(),
            "latent": object(),
            "mask": None,
            "prompt": "a photo",
            "image_path": os.path.join("images", "src.png"),
            "mask_path": None,
            "steps": 40,
            "denoise": 1.0,
            "seed": 7,
            "cfg": 1.0,
            "sampler_preset": {},
            "model_id": "qwen-image-2.1",
        }
        context.update(overrides)
        return context

    def test_plugin_meta(self):
        meta = self.module.PLUGIN_META
        self.assertEqual(meta["id"], PLUGIN_ID)
        self.assertIn("sampling_plan", meta["capabilities"])
        self.assertTrue(callable(self.module.sampling_plan))

    def test_import_is_cpu_only(self):
        server_dir = os.path.dirname(os.path.abspath(config.__file__))
        code = (
            "import sys\n"
            "sys.path.insert(0, %r)\n"
            "from plugins import loader\n"
            "module, status = loader.load_plugin(%r)\n"
            "assert module is not None, status\n"
            "assert 'torch' not in sys.modules, 'imported torch'\n"
            "assert 'comfy' not in sys.modules, 'imported comfy'\n"
            "print('ok')\n"
        ) % (server_dir, PLUGIN_ID)
        result = subprocess.run(
            [sys.executable, "-c", code], capture_output=True, text=True, timeout=120
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "ok")

    def test_declines_outpaint(self):
        self.assertIsNone(self.module.sampling_plan(self._context(op="outpaint")))

    def test_declines_t2i(self):
        self.assertIsNone(self.module.sampling_plan(self._context(image_path=None)))

    def test_declines_masked_edit(self):
        self.assertIsNone(self.module.sampling_plan(self._context(mask=object())))

    def test_declines_partial_denoise(self):
        self.assertIsNone(self.module.sampling_plan(self._context(denoise=0.6)))

    def test_declines_missing_denoise(self):
        self.assertIsNone(self.module.sampling_plan(self._context(denoise=None)))

    def test_declines_non_dict_context(self):
        self.assertIsNone(self.module.sampling_plan(None))

    def test_declines_when_lora_missing(self):
        missing = os.path.join("does", "not", "exist.safetensors")
        with mock.patch.object(loras, "resolve_path", return_value=missing):
            self.assertIsNone(self.module.sampling_plan(self._context()))

    def test_plan_for_maskless_edit(self):
        cleanup = mock.Mock()
        sentinel_sigmas = object()
        with tempfile.TemporaryDirectory() as tmp:
            weight = os.path.join(tmp, "lora.safetensors")
            with open(weight, "wb") as handle:
                handle.write(b"")
            with mock.patch.object(loras, "resolve_path", return_value=weight), mock.patch.object(
                self.module, "_load_lora", return_value={"layer": [1, 2]}
            ), mock.patch.object(
                self.module, "_build_sigmas", return_value=sentinel_sigmas
            ), mock.patch.object(
                self.module, "_install_model", return_value=("PATCHED", cleanup)
            ):
                plan = self.module.sampling_plan(self._context())
        self.assertIsNotNone(plan)
        self.assertEqual(plan["model"], "PATCHED")
        self.assertTrue(plan["skip_shift"])
        self.assertIs(plan["sigmas"], sentinel_sigmas)
        self.assertEqual(plan["steps"], len(self.module._SIGMA_NODES))
        self.assertEqual(plan["sampler_name"], "euler")
        self.assertIs(plan["cleanup"], cleanup)

    def test_disabled_plugin_is_not_dispatched(self):
        loader._MODULES.clear()
        os.environ.pop(config.plugin_env_name(PLUGIN_ID), None)
        self.assertIsNone(dispatch.call("sampling_plan", self._context()))


if __name__ == "__main__":
    unittest.main()
