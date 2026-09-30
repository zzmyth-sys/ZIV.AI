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

    def test_raises_when_source_missing(self):
        # Hard-fail (2026-09-30): a missing origin is an explicit error, never a silent
        # fallback to the source path / a decline.
        with tempfile.TemporaryDirectory() as tmp:
            root = os.path.join(tmp, "loras")
            os.makedirs(root)
            with mock.patch.object(config, "LORA_ROOT", root), mock.patch.object(
                loras, "get_source", return_value=os.path.join(tmp, "missing.safetensors")
            ), mock.patch.object(
                loras, "resolve_path", return_value=os.path.join(root, "x.safetensors")
            ):
                with self.assertRaises(self.module.LoraUnifiedError):
                    self.module.sampling_plan(self._context())

    def test_ensure_unified_copies_origin_into_unified_dir(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = os.path.join(tmp, "loras")
            origin = os.path.join(tmp, "origin.safetensors")
            with open(origin, "wb") as handle:
                handle.write(b"weight")
            dest = os.path.join(root, "origin.safetensors")
            loaded = self.module._ensure_unified(origin, dest)
            self.assertEqual(dest, loaded)
            self.assertTrue(os.path.isfile(dest))

    def test_ensure_unified_returns_existing_unified_copy(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = os.path.join(tmp, "loras")
            os.makedirs(root)
            source = os.path.join(root, "already.safetensors")
            with open(source, "wb") as handle:
                handle.write(b"weight")
            self.assertEqual(source, self.module._ensure_unified(None, source))

    def test_ensure_unified_raises_when_dest_not_absolute(self):
        # No LORA_ROOT -> resolve_path returns a bare relative name -> refuse.
        with tempfile.TemporaryDirectory() as tmp:
            origin = os.path.join(tmp, "origin.safetensors")
            with open(origin, "wb") as handle:
                handle.write(b"weight")
            with self.assertRaises(self.module.LoraUnifiedError):
                self.module._ensure_unified(origin, "relative.safetensors")

    def test_ensure_unified_raises_when_copy_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = os.path.join(tmp, "loras")
            origin = os.path.join(tmp, "origin.safetensors")
            with open(origin, "wb") as handle:
                handle.write(b"weight")
            dest = os.path.join(root, "origin.safetensors")
            with mock.patch.object(
                self.module.shutil, "copyfile", side_effect=OSError("read-only")
            ):
                with self.assertRaises(self.module.LoraUnifiedError):
                    self.module._ensure_unified(origin, dest)

    def test_plan_for_maskless_edit(self):
        cleanup = mock.Mock()
        sentinel_sigmas = object()
        with tempfile.TemporaryDirectory() as tmp:
            root = os.path.join(tmp, "loras")
            os.makedirs(root)
            weight = os.path.join(root, "lora.safetensors")
            with open(weight, "wb") as handle:
                handle.write(b"")
            with mock.patch.object(config, "LORA_ROOT", root), mock.patch.object(
                loras, "get_source", return_value=weight
            ), mock.patch.object(
                loras, "resolve_path", return_value=weight
            ), mock.patch.object(
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


class SeamsForTests(unittest.TestCase):
    """S2 ``_seams_for``：entry seams > entry capabilities > legacy map（D2 / D10）。"""

    def setUp(self):
        loader._MODULES.clear()
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = self._tmp.name

    def _seams(self, entry, module=None):
        return dispatch._seams_for("p", module or object(), entry)

    def test_declared_seams_win(self):
        entry = {"seams": ["before_encode"], "capabilities": ["sampling_plan"]}
        self.assertEqual(self._seams(entry), ["before_encode"])

    def test_capabilities_legacy_mapped(self):
        entry = {"capabilities": ["sampling_plan"]}
        self.assertEqual(self._seams(entry), ["before_sample"])

    def test_capabilities_unknown_dropped(self):
        entry = {"capabilities": ["not_a_seam", "sampling_plan"]}
        self.assertEqual(self._seams(entry), ["before_sample"])

    def test_empty_seams_falls_back_to_capabilities(self):
        entry = {"seams": [], "capabilities": ["sampling_plan"]}
        self.assertEqual(self._seams(entry), ["before_sample"])

    def test_none_entry_is_empty(self):
        self.assertEqual(self._seams(None), [])
        self.assertEqual(self._seams({}), [])

    def test_plugin_meta_seams_is_not_read(self):
        # D2：seams 只放数据文件；PLUGIN_META.seams 不参与解析。
        module = mock.Mock()
        module.PLUGIN_META = {"seams": ["after_encode"]}
        self.assertEqual(self._seams({"capabilities": ["sampling_plan"]}, module), ["before_sample"])

    def test_seams_from_registry_when_entry_omitted(self):
        directory = os.path.join(self.root, "plugins", "p")
        os.makedirs(directory, exist_ok=True)
        with open(os.path.join(directory, "__init__.py"), "w", encoding="utf-8") as handle:
            handle.write("MARKER = 1\n")
        path = os.path.join(self.root, "plugins.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(
                {"version": "1", "plugins": [{"id": "p", "dir": "plugins/p", "seams": ["after_sample"]}]},
                handle,
            )
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", path):
            self.assertEqual(dispatch._seams_for("p", object()), ["after_sample"])


class ViggleSeamMigrationTests(unittest.TestCase):
    """S6：Viggle 从 legacy capabilities 映射迁到 seams 直读（行为不变）。

    数据声明 ``seams`` 优先；函数名仍为旧 ``sampling_plan``，靠 :func:`dispatch._fn_for`
    的接缝名→旧函数名回退解析（D6/D7）。不 mock 全局 config，直接读仓库真实
    ``Template/plugins.json``（未启用插件不加载重栈，见 QwenPluginTests）。
    """

    def setUp(self):
        loader._MODULES.clear()
        os.environ.pop(config.plugin_env_name(PLUGIN_ID), None)

    def test_registry_entry_declares_seams_and_resolves_to_before_sample(self):
        entry = loader.load_registry().get(PLUGIN_ID)
        self.assertIsNotNone(entry)
        self.assertEqual(entry.get("seams"), ["before_sample"])
        # 迁移后 _seams_for 走 seams 直读，而非 capabilities → _LEGACY_CAPABILITY_MAP。
        self.assertEqual(dispatch._seams_for(PLUGIN_ID, None, entry), ["before_sample"])

    def test_before_sample_routes_to_legacy_sampling_plan_fn(self):
        module, status = loader.load_plugin(PLUGIN_ID)
        self.assertIsNotNone(module, status)
        # 接缝名 before_sample 无同名函数 → 回退旧函数名 sampling_plan。
        self.assertIs(dispatch._fn_for(module, "before_sample"), module.sampling_plan)


class CallChainTests(unittest.TestCase):
    """S2 ``call_chain``：数组序链式、patch 累积、异常隔离、旧函数名别名。"""

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

    def _install(self, entries):
        path = os.path.join(self.root, "plugins.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"version": "1", "plugins": entries}, handle)
        patcher = mock.patch.multiple(
            config, PLUGINS_REGISTRY_PATH=path, PLUGINS_BASE_DIR=self.root
        )
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_chain_accumulates_in_array_order(self):
        self._make_plugin(
            "one",
            "def before_encode(ctx):\n    ctx.setdefault('order', []).append('one')\n"
            "    return {'prompt': ctx['prompt'] + '1'}\n",
        )
        self._make_plugin(
            "two",
            "def before_encode(ctx):\n    return {'prompt': ctx['prompt'] + '2'}\n",
        )
        self._install([
            {"id": "one", "dir": "plugins/one", "enabled_by_default": True, "seams": ["before_encode"]},
            {"id": "two", "dir": "plugins/two", "enabled_by_default": True, "seams": ["before_encode"]},
        ])
        out = dispatch.call_chain("before_encode", {"prompt": "x"})
        self.assertEqual(out["prompt"], "x12")

    def test_chain_skips_plugins_not_on_seam(self):
        self._make_plugin("a", "def after_sample(ctx):\n    return {'samples': 'A'}\n")
        self._make_plugin("b", "def before_encode(ctx):\n    return {'prompt': 'B'}\n")
        self._install([
            {"id": "a", "dir": "plugins/a", "enabled_by_default": True, "seams": ["after_sample"]},
            {"id": "b", "dir": "plugins/b", "enabled_by_default": True, "seams": ["before_encode"]},
        ])
        out = dispatch.call_chain("before_encode", {"prompt": "x"})
        self.assertEqual(out, {"prompt": "B"})

    def test_chain_isolates_exceptions(self):
        self._make_plugin("boom", "def before_encode(ctx):\n    raise RuntimeError('boom')\n")
        self._make_plugin("ok", "def before_encode(ctx):\n    return {'prompt': 'ok'}\n")
        self._install([
            {"id": "boom", "dir": "plugins/boom", "enabled_by_default": True, "seams": ["before_encode"]},
            {"id": "ok", "dir": "plugins/ok", "enabled_by_default": True, "seams": ["before_encode"]},
        ])
        self.assertEqual(dispatch.call_chain("before_encode", {"prompt": "x"}), {"prompt": "ok"})

    def test_chain_ignores_non_dict_patch(self):
        self._make_plugin("odd", "def before_encode(ctx):\n    return 42\n")
        self._install([
            {"id": "odd", "dir": "plugins/odd", "enabled_by_default": True, "seams": ["before_encode"]},
        ])
        self.assertEqual(dispatch.call_chain("before_encode", {"prompt": "x"}), {"prompt": "x"})

    def test_chain_all_decline_returns_original(self):
        self._make_plugin("noop", "def before_encode(ctx):\n    return None\n")
        self._install([
            {"id": "noop", "dir": "plugins/noop", "enabled_by_default": True, "seams": ["before_encode"]},
        ])
        self.assertEqual(dispatch.call_chain("before_encode", {"prompt": "x"}), {"prompt": "x"})

    def test_chain_legacy_sampling_plan_alias(self):
        # 旧插件只导出 sampling_plan、capabilities 声明旧名 → 经 seam 解析 + 函数名回退被调用。
        self._make_plugin("legacy", "def sampling_plan(ctx):\n    return {'steps': 6}\n")
        self._install([
            {"id": "legacy", "dir": "plugins/legacy", "enabled_by_default": True,
             "capabilities": ["sampling_plan"]},
        ])
        self.assertEqual(dispatch.call_chain("before_sample", {"steps": 40}), {"steps": 6})

    def test_chain_gives_plugin_a_copy_not_live_ctx(self):
        self._make_plugin(
            "mutator",
            "def before_encode(ctx):\n    ctx['prompt'] = 'in-place'\n    return None\n",
        )
        self._install([
            {"id": "mutator", "dir": "plugins/mutator", "enabled_by_default": True,
             "seams": ["before_encode"]},
        ])
        ctx = {"prompt": "x"}
        dispatch.call_chain("before_encode", ctx)
        self.assertEqual(ctx["prompt"], "x")

    def test_chain_does_not_mutate_callers_ctx(self):
        self._make_plugin("a", "def before_encode(ctx):\n    return {'prompt': 'y'}\n")
        self._install([
            {"id": "a", "dir": "plugins/a", "enabled_by_default": True, "seams": ["before_encode"]},
        ])
        ctx = {"prompt": "x"}
        out = dispatch.call_chain("before_encode", ctx)
        self.assertEqual(out["prompt"], "y")
        self.assertEqual(ctx, {"prompt": "x"})

    def test_chain_warns_when_declared_seam_has_no_callable(self):
        # Z-026：声明了 seam 但模块无对应可调用对象 → warning（不再静默跳过）。
        self._make_plugin("ghost", "MARKER = 1\n")
        self._install([
            {"id": "ghost", "dir": "plugins/ghost", "enabled_by_default": True,
             "seams": ["before_sample"]},
        ])
        with self.assertLogs("zivai.server", level="WARNING") as captured:
            out = dispatch.call_chain("before_sample", {"steps": 40})
        self.assertEqual(out, {"steps": 40})
        self.assertTrue(any("ghost" in line for line in captured.output))
        self.assertTrue(any("before_sample" in line for line in captured.output))

    def test_chain_aggregates_cleanups_from_multiple_plugins(self):
        self._make_plugin(
            "c1", "def before_sample(ctx):\n    return {'cleanup': lambda: 'c1'}\n"
        )
        self._make_plugin(
            "c2", "def before_sample(ctx):\n    return {'cleanup': lambda: 'c2'}\n"
        )
        self._install([
            {"id": "c1", "dir": "plugins/c1", "enabled_by_default": True, "seams": ["before_sample"]},
            {"id": "c2", "dir": "plugins/c2", "enabled_by_default": True, "seams": ["before_sample"]},
        ])
        out = dispatch.call_chain("before_sample", {})
        self.assertNotIn("cleanup", out)
        cleanups = out[dispatch.CLEANUPS_KEY]
        self.assertEqual(len(cleanups), 2)
        self.assertEqual([fn() for fn in cleanups], ["c1", "c2"])


if __name__ == "__main__":
    unittest.main()
