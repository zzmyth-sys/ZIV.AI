"""CPU-only tests for ``sampling_policy`` (Step 1: pure merge / decide).

Pure stdlib ``unittest``; imports only :mod:`sampling_policy` (no ``torch`` / ``comfy`` /
project module). Run from ``python/server``::

    python -m unittest test_sampling_policy
"""

import dataclasses
import unittest

import sampling_policy as sp


class _Sig1D:
    """Minimal duck-typed 1-D sigmas carrier (has ``shape``; avoids importing torch)."""

    def __init__(self, n):
        self.shape = (n,)


class _FixedLen:
    """Object exposing ``ndim`` + ``__len__`` but no ``shape``."""

    def __init__(self, n):
        self.ndim = 1
        self._n = n

    def __len__(self):
        return self._n


class _Sig2D:
    """Two-dimensional ``shape`` -> not legal sigmas."""

    shape = (7, 1)


class _Sig2DNDim:
    """``ndim == 2`` -> not legal sigmas."""

    ndim = 2

    def __len__(self):
        return 7


class SystemDefaultTests(unittest.TestCase):
    def test_empty_inputs_are_all_system_default(self):
        for call in (lambda: sp.resolve(), lambda: sp.resolve(model_entry=None, payload=None, plugin_patch=None)):
            r = call()
            self.assertEqual(r.steps, 25)
            self.assertEqual(r.cfg, 1.0)
            self.assertEqual(r.sampler_name, "euler")
            self.assertEqual(r.scheduler, "simple")
            self.assertEqual(r.shift, 3.1)
            self.assertEqual(r.sampler_type, "auraflow")
            self.assertIsNone(r.sigmas)
            self.assertFalse(r.terminated)
            self.assertEqual(
                r.provenance,
                {
                    "steps": "system_default",
                    "cfg": "system_default",
                    "sampler_name": "system_default",
                    "scheduler": "system_default",
                    "shift": "system_default",
                    "sampler_type": "system_default",
                },
            )
            self.assertEqual(r.conflicts, {})


class LayerOverrideTests(unittest.TestCase):
    def test_single_layer_model_cfg(self):
        r = sp.resolve(model_entry={"sampler": {"cfg": 2.0}})
        self.assertEqual(r.cfg, 2.0)
        self.assertEqual(r.provenance["cfg"], "model_profile")
        self.assertEqual(r.sampler_name, "euler")

    def test_layered_override_per_key(self):
        r = sp.resolve(
            model_entry={"sampler": {"cfg": 2.0, "sampler_name": "euler_a"}},
            payload={"steps": 10},
            plugin_patch={"scheduler": "karras"},
        )
        self.assertEqual(r.cfg, 2.0)
        self.assertEqual(r.sampler_name, "euler_a")
        self.assertEqual(r.steps, 10)
        self.assertEqual(r.scheduler, "karras")
        self.assertEqual(r.provenance["cfg"], "model_profile")
        self.assertEqual(r.provenance["sampler_name"], "model_profile")
        self.assertEqual(r.provenance["steps"], "data")
        self.assertEqual(r.provenance["scheduler"], "plugin")
        self.assertEqual(r.shift, 3.1)
        self.assertEqual(r.sampler_type, "auraflow")

    def test_per_key_independence_with_sigmas(self):
        r = sp.resolve(
            model_entry={"sampler": {"cfg": 2.5}},
            payload={"steps": 99},
            plugin_patch={"sigmas": _Sig1D(7), "steps": 6},
        )
        self.assertEqual(r.cfg, 2.5)
        self.assertEqual(r.provenance["cfg"], "model_profile")
        self.assertEqual(r.steps, 6)
        self.assertEqual(r.provenance["steps"], "plugin")
        self.assertTrue(r.terminated)

    def test_conflicts_recorded_in_chain_order(self):
        r = sp.resolve(
            model_entry={"sampler": {"cfg": 2.0}},
            plugin_patch={"cfg": 3.0},
        )
        self.assertEqual(r.cfg, 3.0)
        self.assertEqual(r.provenance["cfg"], "plugin")
        self.assertEqual(r.conflicts["cfg"], ["model_profile", "plugin"])

    def test_single_layer_key_is_not_a_conflict(self):
        r = sp.resolve(model_entry={"sampler": {"cfg": 2.0}})
        self.assertNotIn("cfg", r.conflicts)


class SigmasTerminatorTests(unittest.TestCase):
    def test_legal_sigmas_keeps_sampler_cfg_and_sampler_type(self):
        r = sp.resolve(plugin_patch={"sigmas": _Sig1D(4)})
        self.assertTrue(r.terminated)
        self.assertEqual(r.scheduler, "sigmas")
        self.assertEqual(r.sampler_type, "auraflow")
        self.assertEqual(r.sampler_name, "euler")
        self.assertEqual(r.cfg, 1.0)
        self.assertEqual(r.steps, 4)

    def test_b3_no_sigmas_no_skip_shift_shift_is_applied(self):
        # Z-027: no-plugin path (no sigmas) now applies AuraFlow.
        r = sp.resolve(payload={"steps": 40})
        self.assertFalse(r.terminated)
        self.assertEqual(r.shift, 3.1)

    def test_b4_sigmas_with_skip_shift_true_shift_is_none(self):
        r = sp.resolve(plugin_patch={"sigmas": _Sig1D(7), "steps": 6, "skip_shift": True})
        self.assertTrue(r.terminated)
        self.assertIsNone(r.shift)

    def test_sigmas_without_skip_shift_skips_shift(self):
        # Z-027: sigmas present, skip_shift unset => shift skipped.
        r = sp.resolve(plugin_patch={"sigmas": _Sig1D(4)})
        self.assertIsNone(r.shift)

    def test_no_sigmas_skip_shift_false_applies_shift(self):
        r = sp.resolve(plugin_patch={"skip_shift": False})
        self.assertFalse(r.terminated)
        self.assertEqual(r.shift, 3.1)

    def test_steps_zero_clamped_to_one(self):
        r = sp.resolve(payload={"steps": 0})
        self.assertEqual(r.steps, 1)

    def test_plugin_explicit_steps_wins_over_sigma_len(self):
        r = sp.resolve(plugin_patch={"sigmas": _Sig1D(7), "steps": 6})
        self.assertEqual(r.steps, 6)

    def test_skip_shift_false_keeps_merged_shift(self):
        r = sp.resolve(plugin_patch={"sigmas": _Sig1D(3), "skip_shift": False})
        self.assertTrue(r.terminated)
        self.assertEqual(r.shift, 3.1)

    def test_skip_shift_false_uses_model_profile_shift(self):
        r = sp.resolve(
            model_entry={"sampler": {"shift": 2.0}},
            plugin_patch={"sigmas": _Sig1D(3), "skip_shift": False},
        )
        self.assertEqual(r.shift, 2.0)

    def test_non_auraflow_type_never_applies_shift(self):
        r = sp.resolve(
            model_entry={"sampler": {"type": "other", "shift": 2.0}},
            plugin_patch={"skip_shift": False},
        )
        self.assertIsNone(r.shift)

    def test_denoise_marked_ineffective_under_sigmas(self):
        r = sp.resolve(payload={"denoise": 0.5}, plugin_patch={"sigmas": _Sig1D(3)})
        self.assertTrue(r.terminated)
        self.assertEqual(r.provenance["denoise"], "data(ineffective_under_sigmas)")

    def test_no_sigmas_keeps_merge_result(self):
        r = sp.resolve(payload={"steps": 8})
        self.assertFalse(r.terminated)
        self.assertEqual(r.steps, 8)
        self.assertEqual(r.scheduler, "simple")
        self.assertEqual(r.sampler_type, "auraflow")

    def test_illegal_sigmas_are_not_terminating(self):
        for bad in (_Sig1D(0), _Sig2D(), _Sig2DNDim(), "SIGMAS", object(), None, [[1], [2]]):
            r = sp.resolve(plugin_patch={"sigmas": bad})
            self.assertFalse(r.terminated, bad)

    def test_plain_1d_sequences_are_legal_sigmas(self):
        for good in ((1.0, 0.0, 0.0), [1, 2]):
            r = sp.resolve(plugin_patch={"sigmas": good})
            self.assertTrue(r.terminated, good)
            self.assertEqual(r.steps, len(good))


class RobustnessTests(unittest.TestCase):
    def test_non_dict_layers_do_not_raise(self):
        r = sp.resolve(model_entry="oops", payload=[1, 2], plugin_patch=42)
        self.assertEqual(r.cfg, 1.0)
        self.assertEqual(r.steps, 25)

    def test_sampler_block_not_dict_is_ignored(self):
        r = sp.resolve(model_entry={"sampler": ["nope"]})
        self.assertEqual(r.cfg, 1.0)
        self.assertEqual(r.provenance["cfg"], "system_default")

    def test_unknown_keys_are_ignored(self):
        r = sp.resolve(
            model_entry={"sampler": {"warp": 1}},
            payload={"seed": 7, "steps": 5},
            plugin_patch={"model": "M", "cleanup": object(), "warp": 9},
        )
        self.assertEqual(r.steps, 5)
        self.assertNotIn("warp", r.provenance)
        self.assertEqual(r.conflicts, {})

    def test_fixed_len_object_is_legal_sigmas(self):
        r = sp.resolve(plugin_patch={"sigmas": _FixedLen(5)})
        self.assertTrue(r.terminated)
        self.assertEqual(r.steps, 5)

    def test_inputs_are_not_mutated(self):
        model_entry = {"sampler": {"cfg": 2.0}}
        payload = {"steps": 10}
        plugin_patch = {"sigmas": _Sig1D(3), "cfg": 3.0}
        before = (
            {k: dict(v) if isinstance(v, dict) else v for k, v in model_entry.items()},
            dict(payload),
            dict(plugin_patch),
        )
        sp.resolve(model_entry=model_entry, payload=payload, plugin_patch=plugin_patch)
        self.assertEqual(model_entry, before[0])
        self.assertEqual(payload, before[1])
        self.assertEqual(plugin_patch, before[2])

    def test_resolved_is_frozen(self):
        r = sp.resolve()
        with self.assertRaises(dataclasses.FrozenInstanceError):
            r.steps = 1


if __name__ == "__main__":
    unittest.main()
