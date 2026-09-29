"""Unit tests for zivcli.verify and goldens.json (no GPU, no subprocess)."""

import io
import json
import os
import sys
import tempfile
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from tools.zivcli import verify  # noqa: E402
from tools.zivcli.__main__ import main  # noqa: E402

B3 = "53DF2DB71C76908394B354BEBDCC585CE0FE820524A2273D4C4AF058B396BBC9"
B4 = "5AD34D51D48779243CF6CEA99526584DA682C355C6FC09946C058C97976A26C8"


def fake_run(sha256=None, wall=45.7, exit_code=0, output_path=None):
    def _fn(opts, **kwargs):
        return exit_code, {
            "sha256": sha256,
            "wall_sec": wall,
            "output_path": output_path or opts.out,
            "exit_code": exit_code,
        }

    return _fn


class GoldensFileTests(unittest.TestCase):
    def test_shas_pinned_to_frozen(self):
        goldens = verify.load_goldens()
        self.assertEqual(goldens["b3"]["sha256"], B3)
        self.assertEqual(goldens["b4"]["sha256"], B4)

    def test_b4_viggle_on(self):
        goldens = verify.load_goldens()
        self.assertEqual(goldens["b3"]["viggle"], 0)
        self.assertEqual(goldens["b4"]["viggle"], 1)

    def test_golden_to_opts_resolves_relative(self):
        goldens = verify.load_goldens()
        opts = verify.golden_to_opts("b3", goldens["b3"], out="x.png")
        self.assertTrue(os.path.isabs(opts.img1))
        self.assertTrue(opts.img1.endswith(os.path.join("fixtures", "img1.jpg")))
        self.assertTrue(opts.img2.endswith(os.path.join("fixtures", "img2.png")))
        self.assertEqual(opts.img2, opts.img3)
        self.assertEqual(opts.side, 1536)
        self.assertEqual(opts.nref, 2)
        self.assertEqual(opts.steps, 40)


class RunGoldenTests(unittest.TestCase):
    def test_match_passes_and_writes_verdict(self):
        with tempfile.TemporaryDirectory() as td:
            code, v = verify.run_golden(
                "b3",
                verify.load_goldens()["b3"],
                run_scenario_fn=fake_run(sha256=B3.lower()),
                out_dir=td,
            )
            self.assertEqual(code, 0)
            self.assertTrue(v["pass"])
            self.assertEqual(v["expected_sha256"], B3)
            path = os.path.join(td, "b3.result.json")
            self.assertTrue(os.path.isfile(path))
            with open(path, encoding="utf-8") as fh:
                on_disk = json.load(fh)
            self.assertTrue(on_disk["pass"])
            self.assertEqual(on_disk["golden"], "b3")

    def test_mismatch_fails(self):
        with tempfile.TemporaryDirectory() as td:
            code, v = verify.run_golden(
                "b4",
                verify.load_goldens()["b4"],
                run_scenario_fn=fake_run(sha256="00" * 32),
                out_dir=td,
            )
            self.assertEqual(code, 1)
            self.assertFalse(v["pass"])

    def test_case_normalization(self):
        with tempfile.TemporaryDirectory() as td:
            code, v = verify.run_golden(
                "b3",
                verify.load_goldens()["b3"],
                run_scenario_fn=fake_run(sha256=B3.upper()),
                out_dir=td,
            )
            self.assertEqual(code, 0)
            self.assertTrue(v["pass"])

    def test_missing_actual_sha_fails(self):
        with tempfile.TemporaryDirectory() as td:
            code, v = verify.run_golden(
                "b3",
                verify.load_goldens()["b3"],
                run_scenario_fn=fake_run(sha256=None, exit_code=1),
                out_dir=td,
            )
            self.assertEqual(code, 1)
            self.assertFalse(v["pass"])


    def test_relative_out_dir_absolutized(self):
        with tempfile.TemporaryDirectory() as td:
            old = os.getcwd()
            os.chdir(td)
            try:
                code, v = verify.run_golden(
                    "b3",
                    verify.load_goldens()["b3"],
                    run_scenario_fn=fake_run(sha256=B3),
                    out_dir="out",
                )
                self.assertEqual(code, 0)
                self.assertTrue(
                    os.path.isfile(os.path.join(td, "out", "b3.result.json"))
                )
            finally:
                os.chdir(old)


class RunVerifyTests(unittest.TestCase):
    def test_unknown_golden_returns_2(self):
        code, v = verify.run_verify(
            "nope", run_scenario_fn=fake_run(sha256=B3), goldens={}
        )
        self.assertEqual(code, 2)
        self.assertEqual(v["error"], "unknown_golden")

    def test_known_golden_uses_fake(self):
        with tempfile.TemporaryDirectory() as td:
            code, v = verify.run_verify(
                "b3",
                run_scenario_fn=fake_run(sha256=B3),
                out_dir=td,
            )
            self.assertEqual(code, 0)
            self.assertTrue(v["pass"])


class VerifyGateTests(unittest.TestCase):
    def test_missing_yes_returns_2(self):
        err = io.StringIO()
        old = sys.stderr
        sys.stderr = err
        try:
            code = main(["verify", "b3"])
        finally:
            sys.stderr = old
        self.assertEqual(code, 2)
        self.assertIn("Z30", err.getvalue())


if __name__ == "__main__":
    unittest.main()
