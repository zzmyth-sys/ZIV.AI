"""CPU-only tests for the pipeline stage split + seam anchors (``pipeline_stages.py``).

Structural contract: the 3 stage functions thread one ctx and dispatch/read the 6 seam
anchors in pipeline order; ``pipeline._run_once`` orchestrates without touching ComfyUI.
``_stage_sample`` is exercised on CPU by injecting fake ``comfy.*`` modules (its only
heavy dependency); the real comfy routing stays covered by ``test_plugin_pipeline``.

从 ``python/server`` 运行：``python -m unittest test_pipeline_stages``。
"""

import contextlib
import os
import sys
import types
import unittest
from unittest import mock

import mem_guard
import pipeline
import pipeline_stages
import plugin_sampling
import preview as preview_module
import seams

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

ANCHORS = ("before_encode", "after_encode", "before_sample", "after_sample",
           "before_decode", "after_decode")


def _ctx(**overrides):
    ctx = {
        "op": "inpaint", "model": "M", "clip": "C", "vae": "V",
        "prompt": "p", "image_path": "src.png", "mask_path": None,
        "additional_images": ["a.png"], "spec": {"value": 512},
        "steps": 8, "denoise": 1.0, "seed": 42, "cfg": 1.0,
        "sampler_preset": {}, "model_id": "qwen-image-2.1",
        "mask_binary": False, "need_negative": True,
        "positive": "P", "negative": "N", "latent": "L", "mask": None, "samples": "S",
    }
    ctx.update(overrides)
    return ctx


def _run_once(**overrides):
    args = dict(
        model="M", clip="C", vae="V", prompt="prompt", image_path="src.png",
        mask_path=None, output_path="out.png", spec={"value": 512}, steps=8, seed=42,
        denoise=1.0, started=0.0, on_progress=None, on_preview=None, poll_cancel=None,
    )
    args.update(overrides)
    return pipeline._run_once(**args)


class _Recorder:
    """Records (anchor, ctx-keys, ctx) and echoes ctx with optional per-anchor patch."""

    def __init__(self, patch=None, cleanups=None):
        self.calls = []
        self.patch = patch or {}
        self.cleanups = cleanups or {}

    def __call__(self, anchor, ctx):
        self.calls.append((anchor, set(ctx), dict(ctx)))
        out = dict(ctx)
        out.update(self.patch.get(anchor, {}))
        bucket = self.cleanups.get(anchor)
        if bucket:
            out[seams.CLEANUPS_KEY] = list(bucket)
        return out

    @property
    def order(self):
        return [anchor for anchor, _, _ in self.calls]

    def keys_for(self, anchor):
        for a, keys, _ in self.calls:
            if a == anchor:
                return keys
        raise AssertionError("anchor %s not called" % anchor)


def _fake_comfy_patch():
    mm = types.ModuleType("comfy.model_management")
    mm.throw_exception_if_processing_interrupted = lambda: None
    sm = types.ModuleType("comfy.sample")
    sm.prepare_noise = lambda latent, seed: "NOISE"
    pkg = types.ModuleType("comfy")
    pkg.model_management = mm
    pkg.sample = sm
    return mock.patch.dict(sys.modules, {
        "comfy": pkg, "comfy.model_management": mm, "comfy.sample": sm,
    })


class AnchorFieldTests(unittest.TestCase):
    """Each anchor's ctx carries exactly the seam whitelist fields (minus CLEANUPS_KEY)."""

    def test_encode_and_decode_anchor_fields(self):
        rec = _Recorder()
        with mock.patch.object(seams, "apply", rec), \
                mock.patch.object(pipeline, "encode_prompt", lambda *a, **k: ("P", "N", "L", "M")), \
                mock.patch.object(pipeline, "vae_decode", lambda vae, samples: ["D"]), \
                mock.patch.object(pipeline, "to_pil", lambda t: ("IMG", 8, 8)):
            enc = pipeline_stages._stage_encode(_ctx())
            dec = pipeline_stages._stage_decode(enc)
        self.assertEqual(rec.order, ["before_encode", "after_encode", "before_decode", "after_decode"])
        self.assertEqual(rec.keys_for("before_encode"),
                         {"prompt", "image_path", "mask_path", "additional_images", "spec"})
        self.assertEqual(rec.keys_for("after_encode"), {"positive", "negative", "latent", "mask"})
        self.assertEqual(rec.keys_for("before_decode"), {"vae", "samples"})
        self.assertEqual(rec.keys_for("after_decode"), {"image", "width", "height"})

    def test_sample_anchor_fields(self):
        rec = _Recorder()
        with mock.patch.object(seams, "apply", rec), \
                mock.patch.object(plugin_sampling, "sample_from", lambda *a, **k: "SAMPLES"), \
                mock.patch.object(plugin_sampling, "cleanup_ctx", mock.Mock()), \
                mock.patch.object(preview_module, "get_previewer", lambda model: None), \
                mock.patch.object(mem_guard, "enforce", lambda: None), \
                _fake_comfy_patch():
            out = pipeline_stages._stage_sample(
                _ctx(), on_progress=None, on_preview=None, poll_cancel=None)
        self.assertEqual(rec.order, ["before_sample", "after_sample"])
        self.assertEqual(rec.keys_for("after_sample"), {"samples"})
        self.assertEqual(out["samples"], "SAMPLES")


class ReadbackTests(unittest.TestCase):
    def test_before_encode_patch_feeds_encode_prompt(self):
        seen = {}
        rec = _Recorder({"before_encode": {"prompt": "PATCHED", "spec": {"value": 768}}})
        with mock.patch.object(seams, "apply", rec), \
                mock.patch.object(pipeline, "encode_prompt",
                                  lambda *a, spec=None, **k: seen.update(prompt=a[2], spec=spec) or ("P", "N", "L", "M")):
            pipeline_stages._stage_encode(_ctx())
        self.assertEqual(seen["prompt"], "PATCHED")
        self.assertEqual(seen["spec"], {"value": 768})

    def test_after_encode_patch_reaches_decode_ctx(self):
        rec = _Recorder({"after_encode": {"latent": "LAT2", "positive": "P2"}})
        with mock.patch.object(seams, "apply", rec), \
                mock.patch.object(pipeline, "encode_prompt", lambda *a, **k: ("P", "N", "L", "M")):
            out = pipeline_stages._stage_encode(_ctx())
        self.assertEqual(out["latent"], "LAT2")
        self.assertEqual(out["positive"], "P2")

    def test_after_decode_patch_overrides_image(self):
        rec = _Recorder({"after_decode": {"image": "IMG2", "width": 3, "height": 4}})
        with mock.patch.object(seams, "apply", rec), \
                mock.patch.object(pipeline, "vae_decode", lambda vae, samples: ["D"]), \
                mock.patch.object(pipeline, "to_pil", lambda t: ("IMG", 8, 8)):
            out = pipeline_stages._stage_decode(_ctx())
        self.assertEqual((out["image"], out["width"], out["height"]), ("IMG2", 3, 4))


class OrchestratorTests(unittest.TestCase):
    def _stub_stages(self, calls, sample_kwargs, sample_error=None):
        def enc(ctx):
            calls.append("encode")
            merged = dict(ctx)
            merged.update(positive="P", negative="N", latent="L", mask=None)
            return merged

        def smp(ctx, **kw):
            calls.append("sample")
            sample_kwargs.append(kw)
            if sample_error is not None:
                raise sample_error
            merged = dict(ctx)
            merged.update(samples="S")
            return merged

        def dec(ctx):
            calls.append("decode")
            merged = dict(ctx)
            merged.update(image="IMG", height=8, width=8)
            return merged

        return enc, smp, dec

    def test_stage_order_forwarded_callbacks_and_return_keys(self):
        calls, sample_kwargs = [], []
        enc, smp, dec = self._stub_stages(calls, sample_kwargs)
        progress = object()
        with mock.patch.object(pipeline_stages, "_stage_encode", enc), \
                mock.patch.object(pipeline_stages, "_stage_sample", smp), \
                mock.patch.object(pipeline_stages, "_stage_decode", dec), \
                mock.patch.object(pipeline, "save_png", mock.Mock()) as save:
            result = _run_once(on_progress=progress)

        self.assertEqual(calls, ["encode", "sample", "decode"])
        self.assertIs(sample_kwargs[0]["on_progress"], progress)
        self.assertIn("on_preview", sample_kwargs[0])
        self.assertIn("poll_cancel", sample_kwargs[0])
        save.assert_called_once_with("IMG", "out.png")
        self.assertEqual(
            set(result), {"output_path", "seed", "width", "height", "resolution", "duration_ms"}
        )
        self.assertEqual((result["width"], result["height"]), (8, 8))
        self.assertEqual(result["resolution"], 512)
        self.assertEqual(result["seed"], 42)

    def test_need_negative_tracks_cfg(self):
        calls, sample_kwargs = [], []
        enc, smp, dec = self._stub_stages(calls, sample_kwargs)
        seen = {}

        def record_enc(ctx):
            seen["need_negative"] = ctx["need_negative"]
            return enc(ctx)

        with mock.patch.object(pipeline_stages, "_stage_encode", record_enc), \
                mock.patch.object(pipeline_stages, "_stage_sample", smp), \
                mock.patch.object(pipeline_stages, "_stage_decode", dec), \
                mock.patch.object(pipeline.config, "SKIP_NEGATIVE_AT_CFG1", True), \
                mock.patch.object(pipeline, "save_png", mock.Mock()):
            _run_once(sampler={"cfg": 1.0})
            self.assertFalse(seen["need_negative"])  # cfg==1.0 discards uncond -> skip encode
            _run_once(sampler={"cfg": 3.5})
            self.assertTrue(seen["need_negative"])

    def test_stage_exception_propagates_without_save(self):
        calls, sample_kwargs = [], []
        enc, smp, dec = self._stub_stages(calls, sample_kwargs, sample_error=RuntimeError("oom"))
        with mock.patch.object(pipeline_stages, "_stage_encode", enc), \
                mock.patch.object(pipeline_stages, "_stage_sample", smp), \
                mock.patch.object(pipeline_stages, "_stage_decode", dec), \
                mock.patch.object(pipeline, "save_png", mock.Mock()) as save:
            with self.assertRaises(RuntimeError):
                _run_once()
        save.assert_not_called()


class SixAnchorOrderTests(unittest.TestCase):
    """Full ``_run_once`` through the real stages: 6 anchors fire once, in pipeline order."""

    def test_six_anchors_in_order(self):
        rec = _Recorder()
        with mock.patch.object(seams, "apply", rec), \
                mock.patch.object(pipeline, "encode_prompt", lambda *a, **k: ("P", "N", "L", "M")), \
                mock.patch.object(pipeline, "vae_decode", lambda vae, samples: ["D"]), \
                mock.patch.object(pipeline, "to_pil", lambda t: ("IMG", 8, 8)), \
                mock.patch.object(pipeline, "save_png", mock.Mock()), \
                mock.patch.object(plugin_sampling, "sample_from", lambda *a, **k: "SAMPLES"), \
                mock.patch.object(plugin_sampling, "cleanup_ctx", mock.Mock()), \
                mock.patch.object(preview_module, "get_previewer", lambda model: None), \
                mock.patch.object(mem_guard, "enforce", lambda: None), \
                _fake_comfy_patch():
            result = _run_once()
        self.assertEqual(rec.order, list(ANCHORS))
        self.assertEqual((result["width"], result["height"]), (8, 8))


class CleanupTests(unittest.TestCase):
    """._run_once` accumulates seam cleanups and runs them LIFO in a `finally`."""

    @contextlib.contextmanager
    def _run(self, rec, sample_from=None):
        sample = sample_from or (lambda *a, **k: "SAMPLES")
        with mock.patch.object(seams, "apply", rec), \
                mock.patch.object(pipeline, "encode_prompt", lambda *a, **k: ("P", "N", "L", "M")), \
                mock.patch.object(pipeline, "vae_decode", lambda vae, samples: ["D"]), \
                mock.patch.object(pipeline, "to_pil", lambda t: ("IMG", 8, 8)), \
                mock.patch.object(pipeline, "save_png", mock.Mock()), \
                mock.patch.object(plugin_sampling, "sample_from", mock.Mock(side_effect=sample)), \
                mock.patch.object(preview_module, "get_previewer", lambda model: None), \
                mock.patch.object(mem_guard, "enforce", lambda: None), \
                _fake_comfy_patch():
            yield pipeline._run_once(
                "M", "C", "V", "prompt", "src.png", None, "out.png",
                {"value": 512}, 8, 42, 1.0, 0.0, None, None, None,
            )

    def test_no_plugin_anchor_ctx_excludes_cleanups_key(self):
        rec = _Recorder()
        with mock.patch.object(seams, "apply", rec), \
                mock.patch.object(pipeline, "encode_prompt", lambda *a, **k: ("P", "N", "L", "M")):
            pipeline_stages._stage_encode(_ctx())
        for anchor in ("before_encode", "after_encode"):
            self.assertNotIn(seams.CLEANUPS_KEY, rec.keys_for(anchor))

    def test_cleanups_run_in_reverse_registration_order(self):
        order = []
        f1, f2 = 0, 0

        def make(name):
            def fn():
                order.append(name)
            return fn

        rec = _Recorder(cleanups={
            "before_sample": [make("before_sample")],
            "after_decode": [make("after_decode")],
        })
        with self._run(rec):
            pass
        self.assertEqual(order, ["after_decode", "before_sample"])  # LIFO

    def test_cleanup_runs_when_later_stage_raises(self):
        ran = []

        def boom(*a, **k):
            raise RuntimeError("sampler oom")

        rec = _Recorder(cleanups={"before_sample": [lambda: ran.append("before_sample")]})
        with self.assertRaises(RuntimeError):
            with self._run(rec, sample_from=boom):
                pass
        self.assertEqual(ran, ["before_sample"])  # registered before the raise, still ran

    def test_cleanup_once_per_attempt_no_duplication(self):
        counts = {"n": 0}

        def fn():
            counts["n"] += 1

        rec = _Recorder(cleanups={
            "before_sample": [fn], "after_sample": [fn],
            "before_decode": [fn], "after_decode": [fn],
            "before_encode": [fn], "after_encode": [fn],
        })
        with self._run(rec):
            pass
        with self._run(rec):
            pass
        self.assertEqual(counts["n"], 12)  # 6 per attempt x 2 attempts, exactly once each

    def test_legacy_cleanup_ctx_not_used(self):
        rec = _Recorder()
        with mock.patch.object(pipeline_stages.plugin_sampling, "cleanup_ctx", mock.Mock()) as legacy:
            with self._run(rec):
                pass
        legacy.assert_not_called()


if __name__ == "__main__":
    unittest.main()
