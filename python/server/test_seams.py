"""CPU-only tests for the seam system foundation （S1：``python/server/seams.py``）。

纯 stdlib ``unittest``；只 import ``seams``（reducer 为纯函数，不触碰 torch / comfy）。
``apply`` 通过 mock 替换 ``plugins.dispatch.call_chain``，避免依赖真实注册表 / 插件目录。

从 ``python/server`` 运行：::

    python -m unittest test_seams
"""

import os
import subprocess
import sys
import unittest
from unittest import mock

import seams
from plugins import dispatch


def _comfy_available():
    try:
        import comfy_extras.nodes_model_advanced  # noqa: F401

        return True
    except Exception:  # noqa: BLE001 - missing heavy stack is a skip, not a failure
        return False


class SeamEnumTests(unittest.TestCase):
    def test_seams_exact_and_ordered(self):
        self.assertEqual(
            seams.SEAMS,
            (
                "before_encode",
                "after_encode",
                "before_sample",
                "after_sample",
                "before_decode",
                "after_decode",
            ),
        )

    def test_seams_excludes_save(self):
        self.assertNotIn("before_save", seams.SEAMS)
        self.assertNotIn("after_save", seams.SEAMS)

    def test_legacy_map(self):
        self.assertEqual(seams.LEGACY_CAPABILITY_MAP, {"sampling_plan": "before_sample"})

    def test_every_seam_has_applier_and_whitelist(self):
        for anchor in seams.SEAMS:
            self.assertIn(anchor, seams._APPLIERS)
            self.assertIn(anchor, seams._WHITELIST)


class ReducerTests(unittest.TestCase):
    def test_whitelist_keys_merged(self):
        ctx = {"prompt": "a", "op": "inpaint"}
        out = seams._apply_before_encode(ctx, {"prompt": "b"})
        self.assertEqual(out["prompt"], "b")
        self.assertEqual(out["op"], "inpaint")

    def test_non_whitelist_ignored(self):
        ctx = {"prompt": "a"}
        out = seams._apply_before_encode(ctx, {"prompt": "b", "op": "hacked", "seed": 1})
        self.assertEqual(out, {"prompt": "b"})

    def test_reducer_does_not_mutate_input_ctx(self):
        ctx = {"prompt": "a"}
        seams._apply_before_encode(ctx, {"prompt": "b"})
        self.assertEqual(ctx, {"prompt": "a"})

    def test_reducer_returns_new_ctx(self):
        ctx = {"prompt": "a"}
        out = seams._apply_before_encode(ctx, {"prompt": "b"})
        self.assertIsNot(out, ctx)

    def test_patch_non_dict_is_noop_copy(self):
        ctx = {"prompt": "a"}
        out = seams._apply_before_encode(ctx, None)
        self.assertEqual(out, {"prompt": "a"})
        self.assertIsNot(out, ctx)

    def test_after_encode_whitelist(self):
        ctx = {"image_path": "/x.png"}
        out = seams._apply_after_encode(ctx, {"latent": 1, "mask": 2, "image_path": "/hacked"})
        self.assertEqual(out, {"image_path": "/x.png", "latent": 1, "mask": 2})

    def test_before_sample_whitelist(self):
        ctx = {"prompt": "a"}
        out = seams._apply_before_sample(
            ctx, {"model": "M", "steps": 6, "cleanup": "fn", "prompt": "hacked"}
        )
        self.assertEqual(out["model"], "M")
        self.assertEqual(out["steps"], 6)
        self.assertEqual(out["cleanup"], "fn")
        self.assertEqual(out["prompt"], "a")

    def test_after_sample_whitelist(self):
        ctx = {}
        out = seams._apply_after_sample(ctx, {"samples": "S", "steps": 99})
        self.assertEqual(out, {"samples": "S"})

    def test_before_decode_whitelist(self):
        ctx = {}
        out = seams._apply_before_decode(ctx, {"vae": "V", "samples": "S", "mask": "M"})
        self.assertEqual(out, {"vae": "V", "samples": "S"})


class AfterDecodeSizeTests(unittest.TestCase):
    class _Img:
        def __init__(self, size):
            self.size = size

    def test_width_height_from_image_size(self):
        ctx = {"image": self._Img((640, 480)), "width": 1, "height": 2}
        out = seams._apply_after_decode(ctx, {})
        self.assertEqual((out["width"], out["height"]), (640, 480))

    def test_image_size_overrides_patch_width_height(self):
        ctx = {"image": self._Img((1024, 768))}
        out = seams._apply_after_decode(ctx, {"width": 10, "height": 20})
        self.assertEqual((out["width"], out["height"]), (1024, 768))

    def test_no_image_keeps_incoming(self):
        ctx = {}
        out = seams._apply_after_decode(ctx, {"width": 10, "height": 20})
        self.assertEqual((out["width"], out["height"]), (10, 20))

    def test_patch_image_replaces_size(self):
        ctx = {"image": self._Img((100, 100))}
        out = seams._apply_after_decode(ctx, {"image": self._Img((50, 60))})
        self.assertEqual((out["width"], out["height"]), (50, 60))


class ApplyTests(unittest.TestCase):
    def test_unknown_anchor_raises(self):
        with self.assertRaises(ValueError):
            seams.apply("not_a_seam", {})

    def test_apply_chains_then_filters(self):
        ctx = {"prompt": "a", "op": "inpaint"}
        with mock.patch.object(
            dispatch, "call_chain", return_value={"prompt": "b", "op": "hacked"}
        ):
            out = seams.apply("before_encode", ctx)
        self.assertEqual(out, {"prompt": "b", "op": "inpaint"})

    def test_apply_does_not_mutate_input_ctx(self):
        ctx = {"prompt": "a"}
        with mock.patch.object(dispatch, "call_chain", return_value={"prompt": "b"}):
            seams.apply("before_encode", ctx)
        self.assertEqual(ctx, {"prompt": "a"})

    def test_apply_after_decode_normalises_size(self):
        ctx = {"image": AfterDecodeSizeTests._Img((320, 200)), "width": 1, "height": 1}
        with mock.patch.object(dispatch, "call_chain", side_effect=lambda seam, c: dict(c)):
            out = seams.apply("after_decode", ctx)
        self.assertEqual((out["width"], out["height"]), (320, 200))

    def test_apply_preserves_aggregated_cleanups(self):
        fns = [lambda: None, lambda: None]
        with mock.patch.object(
            dispatch, "call_chain", return_value={seams.CLEANUPS_KEY: fns}
        ):
            out = seams.apply("before_sample", {})
        self.assertEqual(seams.collect_cleanup(out), fns)


class CpuOnlyImportTests(unittest.TestCase):
    def test_import_is_cpu_only(self):
        server_dir = os.path.dirname(os.path.abspath(seams.__file__))
        code = (
            "import sys\n"
            "sys.path.insert(0, %r)\n"
            "import seams\n"
            "assert 'torch' not in sys.modules, 'imported torch'\n"
            "assert 'comfy' not in sys.modules, 'imported comfy'\n"
            "print('ok')\n"
        ) % (server_dir,)
        result = subprocess.run(
            [sys.executable, "-c", code], capture_output=True, text=True, timeout=120
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), "ok")


class CollectCleanupTests(unittest.TestCase):
    def test_collects_callable(self):
        fn = lambda: None  # noqa: E731
        self.assertEqual(seams.collect_cleanup({"cleanup": fn}), [fn])

    def test_collects_aggregated_list(self):
        a, b = (lambda: None), (lambda: None)  # noqa: E731
        self.assertEqual(seams.collect_cleanup({seams.CLEANUPS_KEY: [a, b]}), [a, b])

    def test_combines_single_and_aggregated(self):
        a, b, c = (lambda: None), (lambda: None), (lambda: None)  # noqa: E731
        out = seams.collect_cleanup({"cleanup": a, seams.CLEANUPS_KEY: [b, c]})
        self.assertEqual(out, [a, b, c])

    def test_aggregated_ignores_non_callable(self):
        fn = lambda: None  # noqa: E731
        self.assertEqual(seams.collect_cleanup({seams.CLEANUPS_KEY: [fn, "x"]}), [fn])

    def test_empty_when_absent(self):
        self.assertEqual(seams.collect_cleanup({}), [])

    def test_ignores_non_callable(self):
        self.assertEqual(seams.collect_cleanup({"cleanup": "not-callable"}), [])

    def test_handles_non_dict(self):
        self.assertEqual(seams.collect_cleanup(None), [])

    def test_does_not_mutate_ctx(self):
        fn = lambda: None  # noqa: E731
        ctx = {"cleanup": fn, "prompt": "a"}
        seams.collect_cleanup(ctx)
        self.assertEqual(set(ctx), {"cleanup", "prompt"})


class CleanupKeyConsistencyTests(unittest.TestCase):
    def test_key_matches_dispatch(self):
        self.assertEqual(seams.CLEANUPS_KEY, dispatch.CLEANUPS_KEY)

    def test_aggregated_key_in_every_whitelist(self):
        for anchor in seams.SEAMS:
            self.assertIn(seams.CLEANUPS_KEY, seams._WHITELIST[anchor])


class BeforeSampleForceTests(unittest.TestCase):
    """S3：``before_sample`` reducer 的施力面（等价旧 ``pipeline.py`` 直调 ``plugin_sampling``）。

    桩掉 ``dispatch.call_chain`` 注入 patch，只验证 reducer 的 AuraFlow 施力 / 归一。AuraFlow
    分支内 import 真实 ``comfy_extras``（本机无重栈则跳过该分支用例）。
    """

    def _ctx(self, **overrides):
        ctx = {
            "model": "M0",
            "steps": 40,
            "sampler_preset": {"type": "auraflow", "shift": 3.1},
        }
        ctx.update(overrides)
        return ctx

    def _apply(self, ctx, patch):
        with mock.patch.object(dispatch, "call_chain", return_value=dict(patch)):
            return seams.apply("before_sample", ctx)

    def test_no_patch_keeps_model_steps_sigmas_none(self):
        out = self._apply(self._ctx(), {})
        self.assertEqual(out["model"], "M0")
        self.assertEqual(out["steps"], 40)
        self.assertIsNone(out["sigmas"])

    def test_explicit_sigmas_skip_shift_keeps_custom_model(self):
        sigmas = mock.Mock(shape=(2,))
        out = self._apply(self._ctx(), {"model": "MP", "sigmas": sigmas, "skip_shift": True})
        self.assertEqual(out["model"], "MP")
        self.assertIs(out["sigmas"], sigmas)

    def test_steps_normalised_to_max1(self):
        self.assertEqual(self._apply(self._ctx(), {"steps": 0})["steps"], 1)
        self.assertEqual(self._apply(self._ctx(), {"steps": "6"})["steps"], 6)
        self.assertEqual(self._apply(self._ctx(), {"steps": None})["steps"], 40)

    def test_viggle_shape_sigmas_not_swallowed(self):
        # 回归守卫：diffusers 惯例 N 节点 + 末尾 0 -> 7 个 sigmas 而 steps=6，不得被吞。
        sigmas = mock.Mock(shape=(7,))
        out = self._apply(
            self._ctx(),
            {"model": "MP", "steps": 6, "sigmas": sigmas, "skip_shift": True},
        )
        self.assertIs(out["sigmas"], sigmas)
        self.assertEqual(out["steps"], 6)

    def test_falsy_patch_model_keeps_base(self):
        out = self._apply(self._ctx(), {"model": None})
        self.assertEqual(out["model"], "M0")

    def test_cleanups_survive_reducer(self):
        fns = [lambda: None]
        out = self._apply(self._ctx(), {seams.CLEANUPS_KEY: fns})
        self.assertEqual(seams.collect_cleanup(out), fns)


class FitSigmasTests(unittest.TestCase):
    """``_fit_sigmas`` 语义：只挡非法值（非 1-D / 空 / None），**不比对** steps。"""

    def test_seven_sigmas_with_six_steps_passes(self):
        # Viggle 实况：steps=6，sigmas=7（N+1）。核心回归用例。
        sigmas = mock.Mock(shape=(7,))
        self.assertIs(seams._fit_sigmas(sigmas, 6), sigmas)

    def test_matching_length_passes(self):
        sigmas = mock.Mock(shape=(6,))
        self.assertIs(seams._fit_sigmas(sigmas, 6), sigmas)

    def test_any_positive_1d_passes(self):
        for n in (1, 2, 40, 41):
            sigmas = mock.Mock(shape=(n,))
            self.assertIs(seams._fit_sigmas(sigmas, 6), sigmas)

    def test_two_d_rejected(self):
        self.assertIsNone(seams._fit_sigmas(mock.Mock(shape=(7, 1)), 6))

    def test_empty_rejected(self):
        self.assertIsNone(seams._fit_sigmas(mock.Mock(shape=(0,)), 6))

    def test_none_rejected(self):
        self.assertIsNone(seams._fit_sigmas(None, 6))

    def test_plain_object_without_shape_rejected(self):
        self.assertIsNone(seams._fit_sigmas("SIGMAS", 6))
        self.assertIsNone(seams._fit_sigmas([1.0, 0.0], 2))


@unittest.skipUnless(_comfy_available(), "ComfyUI stack not available (CPU-only interpreter)")
class BeforeSampleAuraFlowTests(unittest.TestCase):
    """AuraFlow 施力分支：需真实 ``comfy_extras``（CPU 下单测跳过）。"""

    # Z-027 复刻行为：无 sigmas 默认 skip_shift=True ⇒ 不施 AuraFlow。
    # 本测试期望作者意图（无 sigmas 应施力），与当前实现相反。
    # 待 Z-027 裁决后移除 expectedFailure（修实现则测试变 PASS）。
    @unittest.expectedFailure
    def test_shift_applied_without_sigmas(self):
        ctx = {"model": "M0", "steps": 40, "sampler_preset": {"type": "auraflow", "shift": 3.1}}
        with mock.patch.object(dispatch, "call_chain", return_value={}), \
                mock.patch("comfy_extras.nodes_model_advanced.ModelSamplingAuraFlow") as fake:
            fake.return_value.patch_aura.return_value = ["PATCHED"]
            out = seams.apply("before_sample", ctx)
        fake.return_value.patch_aura.assert_called_once_with("M0", 3.1)
        self.assertEqual(out["model"], "PATCHED")

    # Z-027 复刻行为：无 sigmas 默认 skip_shift=True ⇒ 不施 AuraFlow。
    # 本测试期望作者意图（无 sigmas 应施力），与当前实现相反。
    # 待 Z-027 裁决后移除 expectedFailure（修实现则测试变 PASS）。
    @unittest.expectedFailure
    def test_shift_skipped_with_sigmas(self):
        ctx = {"model": "M0", "steps": 2, "sampler_preset": {"type": "auraflow", "shift": 3.1}}
        with mock.patch.object(dispatch, "call_chain", return_value={"sigmas": mock.Mock(shape=(3,))}), \
                mock.patch("comfy_extras.nodes_model_advanced.ModelSamplingAuraFlow") as fake:
            out = seams.apply("before_sample", ctx)
        fake.return_value.patch_aura.assert_not_called()
        self.assertEqual(out["model"], "M0")

    def test_shift_skipped_for_non_auraflow_preset(self):
        ctx = {"model": "M0", "steps": 40, "sampler_preset": {"type": "other", "shift": 3.1}}
        with mock.patch.object(dispatch, "call_chain", return_value={}), \
                mock.patch("comfy_extras.nodes_model_advanced.ModelSamplingAuraFlow") as fake:
            seams.apply("before_sample", ctx)
        fake.return_value.patch_aura.assert_not_called()


if __name__ == "__main__":
    unittest.main()