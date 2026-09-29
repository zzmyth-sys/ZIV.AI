"""Unit tests for zivcli.batch (no GPU, no subprocess)."""

import io
import json
import os
import sys
import tempfile
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from tools.zivcli import batch  # noqa: E402
from tools.zivcli.__main__ import main  # noqa: E402

A = "aa" * 32
B = "bb" * 32
C = "cc" * 32


def fake_runner(mapping, default_sha="00" * 32):
    def _fn(opts, **kwargs):
        name = os.path.splitext(os.path.basename(opts.out))[0]
        return 0, {
            "sha256": mapping.get(name, default_sha),
            "wall_sec": 1.0,
            "output_path": opts.out,
            "exit_code": 0,
        }

    return _fn


def inline(name, sha):
    return {"name": name, "op": "inpaint", "side": 1536, "nref": 0,
            "steps": 40, "viggle": 0, "img1": "harness/fixtures/img1.jpg",
            "sha256": sha}


class LoadListTests(unittest.TestCase):
    def _write(self, td, payload):
        path = os.path.join(td, "list.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(payload, fh)
        return path

    def test_parses_golden_and_inline(self):
        with tempfile.TemporaryDirectory() as td:
            path = self._write(td, [{"golden": "b3"}, inline("c1", A)])
            entries = batch.load_list(path)
            self.assertEqual(len(entries), 2)
            self.assertEqual(entries[0]["golden"], "b3")
            self.assertEqual(entries[1]["name"], "c1")

    def test_non_array_raises(self):
        with tempfile.TemporaryDirectory() as td:
            path = self._write(td, {"golden": "b3"})
            with self.assertRaises(ValueError):
                batch.load_list(path)

    def test_missing_keys_raises(self):
        with tempfile.TemporaryDirectory() as td:
            path = self._write(td, [{"op": "inpaint"}])
            with self.assertRaises(ValueError):
                batch.load_list(path)


class ResolveTests(unittest.TestCase):
    def test_inline_resolution(self):
        with tempfile.TemporaryDirectory() as td:
            name, opts, expected = batch._resolve(
                inline("c1", "AB"), td, {}
            )
            self.assertEqual(name, "c1")
            self.assertEqual(opts.side, 1536)
            self.assertEqual(opts.nref, 0)
            self.assertTrue(os.path.isabs(opts.img1))
            self.assertEqual(expected, "AB")

    def test_unknown_golden_raises(self):
        with tempfile.TemporaryDirectory() as td:
            with self.assertRaises(KeyError):
                batch._resolve({"golden": "zzz"}, td, {})


class RunBatchTests(unittest.TestCase):
    def test_all_pass(self):
        with tempfile.TemporaryDirectory() as td:
            code, summary = batch.run_batch(
                [inline("a", A), inline("b", B)],
                out_dir=td,
                goldens={},
                run_scenario_fn=fake_runner({"a": A, "b": B}),
                check_gpu_fn=lambda: {"free": True},
            )
            self.assertEqual(code, 0)
            self.assertEqual(summary["total"], 2)
            self.assertEqual(summary["pass"], 2)
            self.assertEqual(summary["fail"], 0)

    def test_fail_does_not_abort_remaining(self):
        with tempfile.TemporaryDirectory() as td:
            code, summary = batch.run_batch(
                [inline("a", A), inline("b", B), inline("c", C)],
                out_dir=td,
                goldens={},
                run_scenario_fn=fake_runner({"a": "00" * 32, "b": B, "c": C}),
                check_gpu_fn=lambda: {"free": True},
            )
            self.assertEqual(code, 1)
            self.assertEqual(summary["total"], 3)
            self.assertEqual(summary["fail"], 1)
            self.assertEqual(len(summary["results"]), 3)
            self.assertFalse(summary["results"][0]["pass"])
            self.assertTrue(summary["results"][2]["pass"])

    def test_result_json_written_per_item(self):
        with tempfile.TemporaryDirectory() as td:
            batch.run_batch(
                [inline("a", A)],
                out_dir=td,
                goldens={},
                run_scenario_fn=fake_runner({"a": A}),
                check_gpu_fn=lambda: {"free": True},
            )
            path = os.path.join(td, "a.result.json")
            self.assertTrue(os.path.isfile(path))
            with open(path, encoding="utf-8") as fh:
                on_disk = json.load(fh)
            self.assertEqual(on_disk["name"], "a")
            self.assertTrue(on_disk["pass"])

    def test_case_normalization(self):
        with tempfile.TemporaryDirectory() as td:
            code, summary = batch.run_batch(
                [inline("a", A)],
                out_dir=td,
                goldens={},
                run_scenario_fn=fake_runner({"a": A.upper()}),
                check_gpu_fn=lambda: {"free": True},
            )
            self.assertEqual(code, 0)
            self.assertTrue(summary["results"][0]["pass"])

    def test_golden_entry(self):
        from tools.zivcli import verify

        goldens = verify.load_goldens()
        b3 = goldens["b3"]["sha256"]
        with tempfile.TemporaryDirectory() as td:
            code, summary = batch.run_batch(
                [{"golden": "b3"}],
                out_dir=td,
                goldens=goldens,
                run_scenario_fn=fake_runner({"b3": b3}),
                check_gpu_fn=lambda: {"free": True},
            )
            self.assertEqual(code, 0)
            self.assertEqual(summary["results"][0]["name"], "b3")

    def test_unknown_golden_item_keeps_going(self):
        with tempfile.TemporaryDirectory() as td:
            code, summary = batch.run_batch(
                [{"golden": "zzz"}, inline("a", A)],
                out_dir=td,
                goldens={},
                run_scenario_fn=fake_runner({"a": A}),
                check_gpu_fn=lambda: {"free": True},
            )
            self.assertEqual(code, 1)
            self.assertEqual(len(summary["results"]), 2)
            self.assertFalse(summary["results"][0]["pass"])
            self.assertIn("error", summary["results"][0])
            self.assertTrue(summary["results"][1]["pass"])

    def test_relative_out_dir_absolutized(self):
        with tempfile.TemporaryDirectory() as td:
            old = os.getcwd()
            os.chdir(td)
            try:
                code, summary = batch.run_batch(
                    [inline("a", A)],
                    out_dir="out",
                    goldens={},
                    run_scenario_fn=fake_runner({"a": A}),
                    check_gpu_fn=lambda: {"free": True},
                )
                self.assertEqual(code, 0)
                self.assertTrue(
                    os.path.isfile(os.path.join(td, "out", "a.result.json"))
                )
            finally:
                os.chdir(old)

    def test_gpu_busy_blocks_batch(self):
        with tempfile.TemporaryDirectory() as td:
            code, summary = batch.run_batch(
                [inline("a", A)],
                out_dir=td,
                goldens={},
                run_scenario_fn=fake_runner({"a": A}),
                check_gpu_fn=lambda: {"free": False, "reason": "busy"},
            )
            self.assertEqual(code, 1)
            self.assertEqual(summary["error"], "gpu_busy")
            self.assertEqual(summary["results"], [])


class BatchGateTests(unittest.TestCase):
    def test_missing_yes_returns_2(self):
        err = io.StringIO()
        old = sys.stderr
        sys.stderr = err
        try:
            code = main(["batch", "whatever.json"])
        finally:
            sys.stderr = old
        self.assertEqual(code, 2)
        self.assertIn("Z30", err.getvalue())

    def test_main_batch_bad_file_returns_2(self):
        old = sys.stderr
        sys.stderr = io.StringIO()
        try:
            code = main(["batch", os.path.join(ROOT, "no_such_list.json"), "--yes"])
        finally:
            sys.stderr = old
        self.assertEqual(code, 2)


if __name__ == "__main__":
    unittest.main()
