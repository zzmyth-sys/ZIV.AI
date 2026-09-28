"""CPU-only tests for the plugin loader (batch 1).

纯 stdlib ``unittest``；只 import :mod:`plugins.loader`（其内部只依赖 ``config``），
不触碰 ``torch`` / ``comfy`` / GPU。从 ``python/server`` 运行：::

    python -m unittest test_plugins
"""

import importlib
import json
import os
import tempfile
import unittest
from unittest import mock

import config
from plugins import loader


class PluginEnvNameTests(unittest.TestCase):
    def test_env_name_samples(self):
        cases = {
            "pose-map": "ZIV_AI_PLUGIN_POSE_MAP",
            "sdpose.ood": "ZIV_AI_PLUGIN_SDPOSE_OOD",
            "A b9": "ZIV_AI_PLUGIN_A_B9",
            "x_1": "ZIV_AI_PLUGIN_X_1",
        }
        for plugin_id, expected in cases.items():
            self.assertEqual(config.plugin_env_name(plugin_id), expected)


class LoadRegistryTests(unittest.TestCase):
    def test_valid(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "plugins.json")
            with open(path, "w", encoding="utf-8") as handle:
                json.dump({"version": "1", "plugins": [
                    {"id": "a", "dir": "d"},
                    {"id": "b", "dir": "e"},
                    {"id": ""},
                    "junk",
                ]}, handle)
            registry = loader.load_registry(path)
            self.assertEqual(sorted(registry), ["a", "b"])

    def test_missing_file(self):
        self.assertEqual(loader.load_registry(os.path.join("nope", "plugins.json")), {})

    def test_corrupt_json(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "plugins.json")
            with open(path, "w", encoding="utf-8") as handle:
                handle.write("{not json")
            self.assertEqual(loader.load_registry(path), {})


class ResolveDirTests(unittest.TestCase):
    def test_absolute_dir_is_returned(self):
        with tempfile.TemporaryDirectory() as tmp:
            entry = {"id": "a", "dir": tmp}
            self.assertEqual(loader.resolve_dir(entry), os.path.abspath(tmp))

    def test_relative_dir_is_under_base_dir(self):
        # Default base is PLUGINS_BASE_DIR (REPO_ROOT when the env is absent).
        entry = {"id": "a", "dir": os.path.join("Comfyui", "plugins", "a")}
        expected = os.path.abspath(os.path.join(config.PLUGINS_BASE_DIR, "Comfyui", "plugins", "a"))
        self.assertEqual(loader.resolve_dir(entry), expected)

    def test_relative_dir_uses_plugins_base_dir(self):
        # C# injects ZIV_AI_PLUGINS_BASE_DIR = program directory; resolve_dir must follow it.
        entry = {"id": "a", "dir": os.path.join("plugins", "a")}
        with tempfile.TemporaryDirectory() as tmp:
            with mock.patch.object(config, "PLUGINS_BASE_DIR", tmp):
                self.assertEqual(
                    loader.resolve_dir(entry),
                    os.path.abspath(os.path.join(tmp, "plugins", "a")),
                )

    def test_env_override_wins(self):
        entry = {"id": "pose-map", "dir": "relative/dir"}
        override = os.path.abspath(os.path.join("override", "dir"))
        with mock.patch.dict(os.environ, {"ZIV_AI_PLUGIN_POSE_MAP_DIR": override}):
            self.assertEqual(loader.resolve_dir(entry), override)


class EnabledTests(unittest.TestCase):
    def test_env_overrides_default(self):
        entry = {"id": "pose-map", "enabled_by_default": True}
        with mock.patch.dict(os.environ, {"ZIV_AI_PLUGIN_POSE_MAP": "0"}):
            self.assertFalse(config.plugin_enabled("pose-map", entry))

    def test_default_when_env_absent(self):
        entry = {"id": "pose-map", "enabled_by_default": True}
        with mock.patch.dict(os.environ):
            os.environ.pop("ZIV_AI_PLUGIN_POSE_MAP", None)
            self.assertTrue(loader.enabled("pose-map", entry))

    def test_disabled_when_no_entry(self):
        with mock.patch.dict(os.environ):
            os.environ.pop("ZIV_AI_PLUGIN_MISSING", None)
            self.assertFalse(loader.enabled("missing", {}))


class CheckDepsTests(unittest.TestCase):
    def test_all_satisfied(self):
        # 标准库永远可用。
        self.assertEqual(loader.check_deps({"deps": ["os", "json"]}), [])

    def test_missing_one(self):
        self.assertEqual(
            loader.check_deps({"deps": ["os", "definitely_missing_pkg_xyz"]}),
            ["definitely_missing_pkg_xyz"],
        )

    def test_no_deps(self):
        self.assertEqual(loader.check_deps({}), [])


class LoadPluginTests(unittest.TestCase):
    def setUp(self):
        loader._MODULES.clear()
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = self._tmp.name

    def _write_registry(self, entry):
        path = os.path.join(self.root, "plugins.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"version": "1", "plugins": [entry]}, handle)
        return path

    def _make_plugin(self, name, body):
        directory = os.path.join(self.root, name)
        os.makedirs(directory, exist_ok=True)
        with open(os.path.join(directory, "__init__.py"), "w", encoding="utf-8") as handle:
            handle.write(body)
        return directory

    def test_success(self):
        directory = self._make_plugin("good", "MARKER = 'ok'\n")
        path = self._write_registry({"id": "good", "dir": directory})
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", path):
            module, status = loader.load_plugin("good")
        self.assertEqual(status, "loaded")
        self.assertEqual(module.MARKER, "ok")

    def test_missing_dir(self):
        path = self._write_registry({"id": "gone", "dir": os.path.join(self.root, "gone")})
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", path):
            module, status = loader.load_plugin("gone")
        self.assertIsNone(module)
        self.assertEqual(status, "missing_dir")

    def test_missing_entry(self):
        directory = os.path.join(self.root, "empty")
        os.makedirs(directory, exist_ok=True)
        path = self._write_registry({"id": "empty", "dir": directory})
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", path):
            module, status = loader.load_plugin("empty")
        self.assertIsNone(module)
        self.assertEqual(status, "missing_entry")

    def test_import_failure(self):
        directory = self._make_plugin("boom", "raise RuntimeError('boom')\n")
        path = self._write_registry({"id": "boom", "dir": directory})
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", path):
            module, status = loader.load_plugin("boom")
        self.assertIsNone(module)
        self.assertTrue(status.startswith("load_failed:"))

    def test_unknown_id(self):
        path = self._write_registry({"id": "known", "dir": self.root})
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", path):
            module, status = loader.load_plugin("unknown")
        self.assertIsNone(module)
        self.assertEqual(status, "unknown_plugin")


class PluginRegistryPathTests(unittest.TestCase):
    """C# 权威 + env 注入：Python 必须读 C# 注入的同一文件（``ZIV_AI_PLUGINS_REGISTRY``）。

    开发期 C# ``TemplateDirectory`` 解析到程序目录（bin），Python 默认回退 ``REPO_ROOT``；
    两端一致靠的是 C# 把同一路径写进 env。这里锁定 env 覆盖与「load_registry 用 config 路径」。
    """

    def test_base_dir_default_is_repo_root(self):
        self.assertEqual(config.PLUGINS_BASE_DIR, config.REPO_ROOT)

    def test_base_dir_env_override_is_honored(self):
        override = os.path.abspath(os.path.join("custom", "program"))
        with mock.patch.dict(os.environ, {"ZIV_AI_PLUGINS_BASE_DIR": override}):
            reloaded = importlib.reload(config)
            try:
                self.assertEqual(reloaded.PLUGINS_BASE_DIR, override)
            finally:
                os.environ.pop("ZIV_AI_PLUGINS_BASE_DIR", None)
                importlib.reload(config)

    def test_default_path_is_repo_template(self):
        self.assertEqual(
            config.PLUGINS_REGISTRY_PATH,
            os.path.join(config.REPO_ROOT, "Template", "plugins.json"),
        )

    def test_env_override_is_honored(self):
        override = os.path.abspath(os.path.join("custom", "plugins.json"))
        with mock.patch.dict(os.environ, {"ZIV_AI_PLUGINS_REGISTRY": override}):
            reloaded = importlib.reload(config)
            try:
                self.assertEqual(reloaded.PLUGINS_REGISTRY_PATH, override)
            finally:
                os.environ.pop("ZIV_AI_PLUGINS_REGISTRY", None)
                importlib.reload(config)

    def test_load_registry_uses_config_path(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "plugins.json")
            with open(path, "w", encoding="utf-8") as handle:
                json.dump({"version": "1", "plugins": [{"id": "envpick", "dir": tmp}]}, handle)
            with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", path):
                registry = loader.load_registry()
        self.assertIn("envpick", registry)


class UserOverrideMergeTests(unittest.TestCase):
    """``plugins.user.json`` 整条目覆盖（batch 3 注销登记）。"""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = self._tmp.name

    def _write(self, name, plugins):
        path = os.path.join(self.root, name)
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"version": "1", "plugins": plugins}, handle)
        return path

    def test_user_entry_replaces_builtin_whole_entry(self):
        builtin = self._write(
            "plugins.json",
            [{"id": "a", "dir": "builtin-a", "enabled_by_default": True}],
        )
        self._write("plugins.user.json", [{"id": "a", "dir": "user-a"}])
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", builtin):
            registry = loader.load_registry()
        self.assertEqual(registry["a"], {"id": "a", "dir": "user-a"})

    def test_new_user_id_is_appended_after_builtins(self):
        builtin = self._write("plugins.json", [{"id": "a"}])
        self._write("plugins.user.json", [{"id": "b"}, {"id": "c"}])
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", builtin):
            registry = loader.load_registry()
        self.assertEqual(list(registry), ["a", "b", "c"])

    def test_missing_user_file_yields_builtin(self):
        builtin = self._write("plugins.json", [{"id": "a"}])
        with mock.patch.object(config, "PLUGINS_REGISTRY_PATH", builtin):
            registry = loader.load_registry()
        self.assertEqual(list(registry), ["a"])

    def test_explicit_path_does_not_merge_sibling_user_file(self):
        builtin = self._write("plugins.json", [{"id": "a"}])
        self._write("plugins.user.json", [{"id": "b"}])
        self.assertEqual(list(loader.load_registry(builtin)), ["a"])

    def test_builtin_missing_ignores_user_file(self):
        self._write("plugins.user.json", [{"id": "b"}])
        with mock.patch.object(
            config, "PLUGINS_REGISTRY_PATH", os.path.join(self.root, "plugins.json")
        ):
            self.assertEqual(loader.load_registry(), {})


if __name__ == "__main__":
    unittest.main()
