"""CPU-only unit tests for the LoRA registry reader (Step 8-1). No torch / comfy.

Run: ``python -m unittest test_loras`` from ``python/server``.
"""

import json
import os
import tempfile
import unittest

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

    def test_resolve_strength_preserves_explicit_zero(self):
        entry = {"default_strength_model": 0.5}
        self.assertEqual(0.0, loras.resolve_strength(0, entry, "default_strength_model"))

    def test_resolve_strength_uses_registry_default_when_absent(self):
        entry = {"default_strength_model": 0.5}
        self.assertEqual(0.5, loras.resolve_strength(None, entry, "default_strength_model"))

    def test_resolve_strength_falls_back_to_one(self):
        self.assertEqual(1.0, loras.resolve_strength(None, {}, "default_strength_model"))


if __name__ == "__main__":
    unittest.main()
