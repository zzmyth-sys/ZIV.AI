"""HOST-side integration test: batch-3 ``sampling_plan`` dispatch inside the pipeline.

This is the routing guard the CPU-only suite cannot provide: it monkeypatches the
ComfyUI sampler entry points and drives ``pipeline._run_once`` end to end, proving

  - plugin OFF + maskless edit  -> ``comfy.sample.sample`` + AuraFlow (legacy path)
  - plugin ON  + maskless edit  -> ``comfy.sample.sample_custom``, AuraFlow skipped
  - plugin ON  + outpaint / t2i / masked / denoise<1 -> legacy path (never accelerated)
  - the plugin's ``cleanup`` runs even when the sampler raises (OOM retry safety)
  - the real ``_build_sigmas`` matches the authoritative viggle-turbo schedule

It imports ``comfy`` / ``torch``, so the whole class is **skipped** on a CPU-only
interpreter; ``python -m unittest discover -p "test_*.py"`` therefore stays green
off-GPU while still covering routing on the inference host (Z29/Z30 convention).
"""

import os
import sys
import unittest
from unittest import mock

import config

PLUGIN_ID = "qwen21-viggle-6step"
ENV = config.plugin_env_name(PLUGIN_ID)


def _comfy_available():
    try:
        sys.path.insert(0, config.COMFY_ROOT)
        import comfy.sample  # noqa: F401

        return True
    except Exception:  # noqa: BLE001 - missing / unloadable heavy stack is a skip, not a failure
        return False


COMFY_AVAILABLE = _comfy_available()

if COMFY_AVAILABLE:  # only importable on the inference host
    import comfy.sample
    import comfy_extras.nodes_model_advanced as _nma

    import pipeline
    from plugins import loader

CALLS = {}
MASK_OUT = [None]


def _rec(name):
    def fn(*args, **kwargs):
        CALLS[name] = CALLS.get(name, 0) + 1
        return "SAMPLES"

    return fn


class _FakeModel:
    load_device = None
    model = None

    def __init__(self):
        self.removed = False

    def clone(self):
        clone = _FakeModel()
        _FakeModel.instances.append(clone)
        return clone

    def add_wrapper_with_key(self, *args):
        self.wrapper = args

    def remove_wrappers_with_key(self, *args):
        self.removed = True

    instances = []


@unittest.skipUnless(COMFY_AVAILABLE, "ComfyUI stack not available (CPU-only interpreter)")
class PluginPipelineRoutingTests(unittest.TestCase):
    def setUp(self):
        CALLS.clear()
        MASK_OUT[0] = None
        _FakeModel.instances = []
        self._patch = mock.patch.multiple(
            comfy.sample,
            sample=_rec("sample"),
            sample_custom=_rec("sample_custom"),
            prepare_noise=lambda latent, seed: "NOISE",
        )
        self._patch.start()
        self.addCleanup(self._patch.stop)
        self._aura = mock.patch.object(_nma.ModelSamplingAuraFlow, "patch_aura", _rec("patch_aura"))
        self._aura.start()
        self.addCleanup(self._aura.stop)
        for target, value in (
            ("encode_prompt", lambda *a, **kw: ("POS", "NEG", "LATENT", MASK_OUT[0])),
            ("vae_decode", lambda vae, samples: ["DECODED"]),
            ("to_pil", lambda tensor: ("IMG", 8, 8)),
            ("save_png", lambda image, path: None),
        ):
            patcher = mock.patch.object(pipeline, target, value)
            patcher.start()
            self.addCleanup(patcher.stop)
        preview = mock.patch.object(pipeline.preview_module, "get_previewer", lambda model: None)
        preview.start()
        self.addCleanup(preview.stop)

    def tearDown(self):
        os.environ.pop(ENV, None)
        loader._MODULES.clear()

    def _run(self, op, image_path, mask=None, denoise=1.0):
        CALLS.clear()
        MASK_OUT[0] = mask
        _FakeModel.instances = []
        pipeline._run_once(
            _FakeModel(), "CLIP", "VAE", "prompt",
            image_path, None, os.path.join(config.REPO_ROOT, "output", "_routing_test.png"),
            {"value": 512}, 8, 42, denoise, 0.0,
            None, None, None,
            mask_binary=False,
            sampler={
                "type": "auraflow", "shift": 3.1, "sampler_name": "euler",
                "scheduler": "simple", "cfg": 1.0,
            },
            op=op, model_id="qwen-image-2.1",
        )
        return {
            "sample": CALLS.get("sample", 0),
            "sample_custom": CALLS.get("sample_custom", 0),
            "patch_aura": CALLS.get("patch_aura", 0),
        }

    def _enable(self):
        loader._MODULES.clear()
        os.environ[ENV] = "1"
        plugin, status = loader.load_plugin(PLUGIN_ID)
        self.assertIsNotNone(plugin, status)
        # the 1.3 GB weight + real sigmas are irrelevant to routing; stub them.
        plugin._load_lora = lambda path: {}
        plugin._build_sigmas = lambda latent: "SIGMAS"
        return plugin

    def _edit(self):
        return os.path.join(config.REPO_ROOT, "_test_step2", "input_test_512.png")

    def test_disabled_edit_uses_legacy_path(self):
        os.environ.pop(ENV, None)
        loader._MODULES.clear()
        self.assertEqual(
            self._run("inpaint", self._edit()),
            {"sample": 1, "sample_custom": 0, "patch_aura": 1},
        )

    def test_enabled_edit_uses_plan_and_cleans_up(self):
        self._enable()
        self.assertEqual(
            self._run("inpaint", self._edit()),
            {"sample": 0, "sample_custom": 1, "patch_aura": 0},
        )
        self.assertTrue(any(clone.removed for clone in _FakeModel.instances))

    def test_cleanup_runs_when_sampler_raises(self):
        self._enable()
        with mock.patch.object(comfy.sample, "sample_custom", side_effect=RuntimeError("oom")):
            with self.assertRaises(RuntimeError):
                self._run("inpaint", self._edit())
        self.assertTrue(any(clone.removed for clone in _FakeModel.instances))

    def test_plan_re_resolved_per_attempt_without_hook_leak(self):
        """run() retries _run_once per resolution fallback: each attempt must install a
        fresh clone and tear it down, so a failed attempt never leaks hooks into the next."""
        self._enable()
        attempts = {"n": 0}

        def flaky(*args, **kwargs):
            attempts["n"] += 1
            if attempts["n"] == 1:
                raise RuntimeError("simulated OOM")
            CALLS["sample_custom"] = CALLS.get("sample_custom", 0) + 1
            return "SAMPLES"

        with mock.patch.object(comfy.sample, "sample_custom", flaky):
            with self.assertRaises(RuntimeError):
                self._run("inpaint", self._edit())
            first_attempt = list(_FakeModel.instances)
            second = self._run("inpaint", self._edit())

        self.assertEqual(second, {"sample": 0, "sample_custom": 1, "patch_aura": 0})
        self.assertTrue(first_attempt and all(c.removed for c in first_attempt))
        self.assertTrue(_FakeModel.instances and all(c.removed for c in _FakeModel.instances))
        self.assertIsNot(_FakeModel.instances[0], first_attempt[0])

    def test_enabled_non_edit_ops_never_accelerate(self):
        self._enable()
        cases = [
            ("outpaint", self._edit(), None, 1.0),
            ("t2i", None, None, 1.0),
            ("inpaint", self._edit(), "MASK", 1.0),
            ("inpaint", self._edit(), None, 0.5),
        ]
        for op, image_path, mask, denoise in cases:
            with self.subTest(op=op, mask=mask, denoise=denoise):
                self.assertEqual(
                    self._run(op, image_path, mask=mask, denoise=denoise),
                    {"sample": 1, "sample_custom": 0, "patch_aura": 1},
                )


@unittest.skipUnless(COMFY_AVAILABLE, "ComfyUI stack not available (CPU-only interpreter)")
class QwenPluginSigmaScheduleTests(unittest.TestCase):
    """The real sigma math must match the authoritative viggle-turbo node output."""

    def _plugin(self):
        loader._MODULES.clear()
        plugin, status = loader.load_plugin(PLUGIN_ID)
        self.assertIsNotNone(plugin, status)
        return plugin

    def test_sigma_schedule_matches_reference(self):
        import torch

        plugin = self._plugin()
        # 1024x640 -> 64x40 latent (/16) -> 2560 tokens; values captured from the
        # authoritative ViggleTurboSigmas node for this token count.
        latent = torch.zeros([1, 64, 40, 64])
        sigmas = plugin._build_sigmas(latent).tolist()
        expected = [1.0, 0.965249, 0.928378, 0.847450, 0.649338, 0.381666, 0.0]
        self.assertEqual(len(sigmas), len(expected))
        for got, want in zip(sigmas, expected):
            self.assertAlmostEqual(got, want, places=5)


if __name__ == "__main__":
    unittest.main()
