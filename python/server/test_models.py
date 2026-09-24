"""CPU-only unit tests for the model registry reader (Step 8-2). No torch / comfy.

Run: ``python -m unittest test_models`` from ``python/server``.
"""

import json
import os
import tempfile
import unittest

import models

_ENV_KEYS = (
    "ZIV_AI_DIT_PATH",
    "ZIV_AI_TE_PATH",
    "ZIV_AI_VAE_PATH",
    "ZIV_AI_AURAFLOW_SHIFT",
    "ZIV_AI_SAMPLER",
    "ZIV_AI_SCHEDULER",
    "ZIV_AI_CFG",
)


def _write(directory, text):
    path = os.path.join(directory, "models.json")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)
    return path


class ModelRegistryTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        for key in _ENV_KEYS:
            os.environ.pop(key, None)

    def _registry(self, text):
        return models.load_registry(_write(self._tmp.name, text))

    def test_comment_and_unknown_fields_ignored(self):
        registry = self._registry(
            """
            {
              "_comment": "hello",
              "version": "1",
              "models": [
                { "id": "alpha", "dit_path": "d", "te_path": "t", "vae_path": "v",
                  "unknown": 1, "default": true }
              ]
            }
            """
        )
        self.assertEqual(["alpha"], list(registry.keys()))

    def test_resolve_unknown_falls_back_to_default(self):
        registry = self._registry(
            json.dumps(
                {
                    "models": [
                        {"id": "alpha", "dit_path": "a1"},
                        {"id": "beta", "dit_path": "b1", "default": True},
                    ]
                }
            )
        )
        self.assertEqual("beta", models.resolve("beta", registry)["id"])
        self.assertEqual("beta", models.resolve(None, registry)["id"])
        self.assertEqual("beta", models.resolve("nope", registry)["id"])

    def test_resolve_paths_prefers_json_then_env(self):
        registry = self._registry(
            json.dumps(
                {
                    "models": [
                        {
                            "id": "alpha",
                            "dit_path": "json-dit",
                            "te_path": "json-te",
                            "vae_path": "json-vae",
                        }
                    ]
                }
            )
        )
        self.assertEqual("json-dit", models.resolve_paths("alpha", registry)["dit_path"])

        os.environ["ZIV_AI_DIT_PATH"] = "env-dit"
        try:
            self.assertEqual("env-dit", models.resolve_paths("alpha", registry)["dit_path"])
        finally:
            os.environ.pop("ZIV_AI_DIT_PATH", None)

    def test_missing_or_malformed_falls_back_to_config_defaults(self):
        self.assertEqual({}, models.load_registry(_write(self._tmp.name, "{bad")))
        resolved = models.resolve_paths("anything", {})
        self.assertTrue(resolved["dit_path"].endswith(".safetensors"))
        self.assertEqual("qwen-image-2.1", models.resolve_paths(None, {})["id"])

    def test_sampler_env_overrides_json(self):
        registry = self._registry(
            json.dumps(
                {
                    "models": [
                        {
                            "id": "alpha",
                            "default": True,
                            "sampler": {
                                "type": "auraflow",
                                "shift": 3.1,
                                "sampler_name": "euler",
                                "scheduler": "simple",
                                "cfg": 1.0,
                            },
                        }
                    ]
                }
            )
        )
        sampler = models.resolve_sampler("alpha", registry)
        self.assertEqual(3.1, sampler["shift"])
        self.assertEqual("euler", sampler["sampler_name"])

        os.environ["ZIV_AI_AURAFLOW_SHIFT"] = "2.5"
        os.environ["ZIV_AI_SAMPLER"] = "dpmpp_2m"
        try:
            overridden = models.resolve_sampler("alpha", registry)
            self.assertEqual(2.5, overridden["shift"])
            self.assertEqual("dpmpp_2m", overridden["sampler_name"])
        finally:
            os.environ.pop("ZIV_AI_AURAFLOW_SHIFT", None)
            os.environ.pop("ZIV_AI_SAMPLER", None)


if __name__ == "__main__":
    unittest.main()
