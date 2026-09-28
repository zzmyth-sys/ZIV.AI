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


if __name__ == "__main__":
    unittest.main()