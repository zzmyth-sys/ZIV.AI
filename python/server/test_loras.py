"""CPU-only unit tests for the LoRA registry reader (Step 8-1). No torch / comfy.

Run: ``python -m unittest test_loras`` from ``python/server``.
"""

import json
import os
import tempfile
import unittest
from unittest import mock

import config
import loras


def _write(directory, text):
    path = os.path.join(directory, "loras.json")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)
    return path


class LorasRegistryTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        # Pin LORA_ROOT empty so the relative-path join added in the config layer cannot
        # make these (root-agnostic) cases depend on an ambient ZIV_AI_LORA_ROOT.
        patcher = mock.patch.object(config, "LORA_ROOT", "")
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_resolve_id_to_entry_and_path(self):
        path = _write(
            self._tmp.name,
            json.dumps(
                {
                    "loras": [
                        {
                            "id": "anime_v2",
                            "path": "D:/lora/anime.safetensors",
                            "default_strength_model": 0.8,
                            "default_strength_clip": 0.8,
                        }
                    ]
                }
            ),
        )
        registry = loras.load_registry(path)
        self.assertIn("anime_v2", registry)
        self.assertEqual("D:/lora/anime.safetensors", loras.resolve_path("anime_v2", registry))
        self.assertEqual(0.8, loras.resolve("anime_v2", registry)["default_strength_model"])

    def test_literal_path_passes_through(self):
        registry = loras.load_registry(_write(self._tmp.name, json.dumps({"loras": []})))
        self.assertEqual("D:/x/y.safetensors", loras.resolve_path("D:/x/y.safetensors", registry))
        self.assertIsNone(loras.resolve("D:/x/y.safetensors", registry))

    def test_missing_file_yields_empty_registry(self):
        self.assertEqual({}, loras.load_registry(os.path.join(self._tmp.name, "nope.json")))
        # An unknown id is passed through as a literal path (so literal paths still work).
        self.assertEqual("anime_v2", loras.resolve_path("anime_v2", {}))

    def test_entries_without_id_are_skipped(self):
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps({"loras": [{"path": "x"}, {"id": ""}, {"id": "ok", "path": "p"}]}),
            )
        )
        self.assertEqual(["ok"], list(registry.keys()))

    def test_empty_or_blank_resolves_none(self):
        registry = loras.load_registry(_write(self._tmp.name, json.dumps({"loras": []})))
        self.assertIsNone(loras.resolve_path("", registry))
        self.assertIsNone(loras.resolve_path(None, registry))

    def test_malformed_json_yields_empty_registry(self):
        self.assertEqual({}, loras.load_registry(_write(self._tmp.name, "{not json")))

    def test_top_level_comment_is_ignored(self):
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps(
                    {
                        "_comment": "hello",
                        "version": "1",
                        "loras": [{"id": "x", "path": "p"}],
                    }
                ),
            )
        )
        self.assertEqual({"x": {"id": "x", "path": "p"}}, registry)

    def test_validate_passes_for_existing_file(self):
        weight = os.path.join(self._tmp.name, "lora.safetensors")
        with open(weight, "wb") as handle:
            handle.write(b"")
        registry = loras.load_registry(_write(self._tmp.name, json.dumps({"loras": []})))
        # Literal absolute path that exists -> returned unchanged.
        self.assertEqual(weight, loras.resolve_path(weight, registry, validate=True))

    def test_validate_raises_for_missing_file(self):
        missing = os.path.join(self._tmp.name, "nope.safetensors")
        with self.assertRaises(ValueError) as ctx:
            loras.resolve_path(missing, {}, validate=True)
        self.assertIn("LoRA 文件不存在", str(ctx.exception))
        self.assertIn(missing, str(ctx.exception))

    def test_validate_raises_for_registry_id_pointing_to_missing(self):
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps({"loras": [{"id": "gone", "path": "Z:/missing/lora.safetensors"}]}),
            )
        )
        with self.assertRaises(ValueError):
            loras.resolve_path("gone", registry, validate=True)

    def test_validate_off_by_default_keeps_pass_through(self):
        # Backward compatible: without validate, a missing path is returned as-is (the plugin
        # relies on this and declines on its own isfile check).
        missing = os.path.join(self._tmp.name, "nope.safetensors")
        self.assertEqual(missing, loras.resolve_path(missing, {}))

    def test_unused_entries_are_not_validated(self):
        # Loading the registry must not touch the filesystem for unused entries.
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps({"loras": [{"id": "unused", "path": "Z:/nope/lora.safetensors"}]}),
            )
        )
        self.assertIn("unused", registry)

    def test_resolve_strength_preserves_explicit_zero(self):
        entry = {"default_strength_model": 0.5}
        self.assertEqual(0.0, loras.resolve_strength(0, entry, "default_strength_model"))

    def test_resolve_strength_uses_registry_default_when_absent(self):
        entry = {"default_strength_model": 0.5}
        self.assertEqual(0.5, loras.resolve_strength(None, entry, "default_strength_model"))

    def test_resolve_strength_falls_back_to_one(self):
        self.assertEqual(1.0, loras.resolve_strength(None, {}, "default_strength_model"))


class LoraRootTests(unittest.TestCase):
    """``config.LORA_ROOT`` (设置窗口 [models] lora_root) 对相对路径的拼接语义。"""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self._registry = loras.load_registry(
            _write(self._tmp.name, json.dumps({"loras": []}))
        )
        self._root = os.path.join(self._tmp.name, "loras")
        self._relative = os.path.join("sub", "w.safetensors")

    def test_relative_path_joined_with_lora_root(self):
        with mock.patch.object(config, "LORA_ROOT", self._root):
            self.assertEqual(
                os.path.join(self._root, self._relative),
                loras.resolve_path(self._relative, self._registry),
            )

    def test_absolute_path_ignores_lora_root(self):
        absolute = os.path.join(self._tmp.name, "abs.safetensors")
        with mock.patch.object(config, "LORA_ROOT", self._root):
            self.assertEqual(absolute, loras.resolve_path(absolute, self._registry))

    def test_registry_relative_path_is_joined(self):
        registry = loras.load_registry(
            _write(
                self._tmp.name,
                json.dumps({"loras": [{"id": "r", "path": self._relative}]}),
            )
        )
        with mock.patch.object(config, "LORA_ROOT", self._root):
            self.assertEqual(
                os.path.join(self._root, self._relative),
                loras.resolve_path("r", registry),
            )

    def test_relative_path_without_root_passes_through(self):
        with mock.patch.object(config, "LORA_ROOT", ""):
            self.assertEqual(
                self._relative,
                loras.resolve_path(self._relative, self._registry),
            )

    def test_relative_path_without_root_and_validate_raises(self):
        with mock.patch.object(config, "LORA_ROOT", ""):
            with self.assertRaises(ValueError) as ctx:
                loras.resolve_path(self._relative, self._registry, validate=True)
        self.assertIn("lora_root", str(ctx.exception))

    def test_relative_path_with_root_and_validate_checks_joined_file(self):
        os.makedirs(os.path.join(self._root, "sub"), exist_ok=True)
        weight = os.path.join(self._root, self._relative)
        with open(weight, "wb") as handle:
            handle.write(b"")
        with mock.patch.object(config, "LORA_ROOT", self._root):
            self.assertEqual(
                weight, loras.resolve_path(self._relative, self._registry, validate=True)
            )


if __name__ == "__main__":
    unittest.main()
