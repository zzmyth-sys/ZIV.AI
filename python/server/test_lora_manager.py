"""CPU-only tests for the public LoRA manager plugin (owner model). No torch / comfy.

Run: ``python -m unittest test_lora_manager`` from ``python/server``.
"""

import importlib.util
import os
import tempfile
import unittest
from unittest import mock

import config
import loras


def _load_manager():
    path = os.path.join(config.REPO_ROOT, "plugin_packs", "lora-manager", "__init__.py")
    spec = importlib.util.spec_from_file_location("lora_manager_under_test", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class LoraManagerTests(unittest.TestCase):
    def setUp(self):
        self.manager = _load_manager()
        self.manager._CACHE.update({"mtime": None, "public": (), "known": ()})
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = os.path.join(self._tmp.name, "loras")
        os.makedirs(self.root)

    def _touch(self, name):
        with open(os.path.join(self.root, name), "wb") as handle:
            handle.write(b"")

    def test_unowned_weight_is_public(self):
        self._touch("pub.safetensors")
        registry = {"vig": {"id": "vig", "owner": "Plugin", "path": r"E:\x\owned.safetensors"}}
        with mock.patch.object(config, "LORA_ROOT", self.root), \
             mock.patch.object(loras, "load_registry", return_value=registry):
            public, known = self.manager.scan_public(refresh=True)
        self.assertEqual(("pub.safetensors",), public)
        self.assertIn("pub.safetensors", known)

    def test_owned_weight_is_not_public(self):
        self._touch("part-1.safetensors")
        registry = {"face": {"id": "face", "owner": "model", "path": r"C:\x\part-1.safetensors"}}
        with mock.patch.object(config, "LORA_ROOT", self.root), \
             mock.patch.object(loras, "load_registry", return_value=registry):
            public, _ = self.manager.scan_public(refresh=True)
        self.assertEqual((), public)

    def test_absent_owner_counts_as_public(self):
        self._touch("n.safetensors")
        registry = {"n": {"id": "n", "path": r"C:\x\n.safetensors"}}  # no owner
        with mock.patch.object(config, "LORA_ROOT", self.root), \
             mock.patch.object(loras, "load_registry", return_value=registry):
            public, _ = self.manager.scan_public(refresh=True)
        self.assertEqual(("n.safetensors",), public)

    def test_list_public(self):
        self._touch("a.safetensors")
        with mock.patch.object(config, "LORA_ROOT", self.root), \
             mock.patch.object(loras, "load_registry", return_value={}):
            self.assertEqual(["a.safetensors"], self.manager.list_public())

    def test_before_encode_returns_none_and_never_raises(self):
        with mock.patch.object(config, "LORA_ROOT", self.root), \
             mock.patch.object(loras, "load_registry", return_value={}):
            self.assertIsNone(self.manager.before_encode({}))

    def test_no_root_yields_empty(self):
        with mock.patch.object(config, "LORA_ROOT", ""):
            self.assertEqual(((), ()), self.manager.scan_public(refresh=True))

    def test_cache_reuses_unchanged_dir(self):
        self._touch("b.safetensors")
        with mock.patch.object(config, "LORA_ROOT", self.root), \
             mock.patch.object(loras, "load_registry", return_value={}):
            first = self.manager.scan_public(refresh=True)
            # Second call without refresh must hit the cache (no exception, same result).
            second = self.manager.scan_public()
        self.assertEqual(first, second)


if __name__ == "__main__":
    unittest.main()
