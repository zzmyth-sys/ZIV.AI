"""Cross-language alignment guard (CPU-only, no torch / comfy).

The Python ``lora-manager.scan_public`` and the C# ``SettingsWindow.ScanPublicLoras`` must agree
on the **same shared fixture** (``tests/fixtures/lora-scan``); both read the same ``expected.json``
so a rule change on either side fails its test. Only the set is asserted (sort differs by design).

Run: ``python -m unittest test_lora_scan_alignment`` from ``python/server``.
"""

import importlib.util
import json
import os
import unittest
from unittest import mock

import config
import loras


def _load_manager():
    path = os.path.join(config.REPO_ROOT, "plugin_packs", "lora-manager", "__init__.py")
    spec = importlib.util.spec_from_file_location("lora_manager_align", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class LoraScanAlignmentTests(unittest.TestCase):
    def setUp(self):
        self.manager = _load_manager()
        self.manager._CACHE.update({"mtime": None, "public": (), "known": ()})
        self.fixture = os.path.join(config.REPO_ROOT, "tests", "fixtures", "lora-scan")

    def test_python_scan_matches_shared_expected(self):
        with open(os.path.join(self.fixture, "expected.json"), encoding="utf-8") as handle:
            expected = set(json.load(handle)["public_loras"])

        registry = loras.load_registry(os.path.join(self.fixture, "loras.json"))
        root = os.path.join(self.fixture, "models", "loras")
        with mock.patch.object(config, "LORA_ROOT", root), \
             mock.patch.object(loras, "load_registry", return_value=registry):
            public, _ = self.manager.scan_public(refresh=True)

        self.assertEqual(expected, set(public))
        # The .ckpt sample in the fixture guards the extension-set rule (D1).
        self.assertIn("d.ckpt", public)


if __name__ == "__main__":
    unittest.main()
