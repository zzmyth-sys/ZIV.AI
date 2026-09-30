"""CPU-only tests for MODEL_ROOT derivation (A10 revision). No comfy / torch.

Run: ``python -m unittest test_config_paths`` from ``python/server``.
"""

import importlib
import os
import tempfile
import unittest

import config

_ENV_KEYS = ("ZIV_AI_MODEL_ROOT", "ZIV_AI_COMFY_ROOT")


class ModelRootDerivationTests(unittest.TestCase):
    def setUp(self):
        self._saved = {key: os.environ.get(key) for key in _ENV_KEYS}

    def tearDown(self):
        for key, value in self._saved.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value
        importlib.reload(config)

    def test_env_model_root_wins(self):
        os.environ.pop("ZIV_AI_COMFY_ROOT", None)
        os.environ["ZIV_AI_MODEL_ROOT"] = r"X:\custom\models"
        importlib.reload(config)

        self.assertEqual(r"X:\custom\models", config.MODEL_ROOT)
        self.assertTrue(config.DIT_MODEL_PATH.startswith(r"X:\custom\models"))

    def test_derives_model_root_from_comfy_root(self):
        os.environ.pop("ZIV_AI_MODEL_ROOT", None)
        with tempfile.TemporaryDirectory() as root:
            os.makedirs(os.path.join(root, "models"))
            os.environ["ZIV_AI_COMFY_ROOT"] = root
            importlib.reload(config)

            self.assertEqual(os.path.join(root, "models"), config.MODEL_ROOT)
            self.assertEqual(
                os.path.join(root, "models", "vae", "qwen_image_2.1_vae_bf16.safetensors"),
                config.VAE_PATH,
            )

    def test_falls_back_when_comfy_models_absent(self):
        os.environ.pop("ZIV_AI_MODEL_ROOT", None)
        with tempfile.TemporaryDirectory() as root:  # no models/ subdirectory
            os.environ["ZIV_AI_COMFY_ROOT"] = root
            importlib.reload(config)

            # Falls back to the built-in default; the derived path stays a safetensors path.
            self.assertTrue(config.DIT_MODEL_PATH.endswith(".safetensors"))


if __name__ == "__main__":
    unittest.main()
