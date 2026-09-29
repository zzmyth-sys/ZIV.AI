"""Unit tests for zivcli.runner and the ``ziv run`` gate (no GPU, no subprocess)."""

import hashlib
import io
import json
import os
import sys
import tempfile
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from tools.zivcli import runner  # noqa: E402
from tools.zivcli.__main__ import main  # noqa: E402


def make_opts(**kw):
    base = dict(
        op="inpaint",
        side=1536,
        nref=2,
        steps=40,
        viggle=0,
        img1=r"D:\temp\img1.jpg",
        img2=r"D:\devlop\ZIV.AI\_test_step2\viggle_aspect_640x1024.png",
        img3=r"D:\devlop\ZIV.AI\_test_step2\viggle_aspect_640x1024.png",
        out=r"D:\temp\b3.png",
    )
    base.update(kw)
    return runner.RunOptions(**base)


class FakePopen:
    def __init__(self, cmd, out=b"", err=b"", returncode=0, **kwargs):
        self.cmd = cmd
        self.kwargs = kwargs
        self.pid = 4242
        self.returncode = returncode
        self._out = out
        self._err = err
        self.returncode = returncode

    def poll(self):
        return self.returncode

    def communicate(self):
        return self._out, self._err


def fake_factory(out=b"", err=b"", returncode=0):
    def _factory(cmd, **kwargs):
        return FakePopen(cmd, out=out, err=err, returncode=returncode, **kwargs)

    return _factory


class BuildEnvTests(unittest.TestCase):
    def test_full_two_ref(self):
        env = runner.build_env(make_opts(), base={})
        self.assertEqual(env["SCN_OP"], "inpaint")
        self.assertEqual(env["SCN_SIDE"], "1536")
        self.assertEqual(env["SCN_NREF"], "2")
        self.assertEqual(env["SCN_STEPS"], "40")
        self.assertEqual(env["SCN_VIGGLE"], "0")
        self.assertEqual(env["SCN_IMG1"], r"D:\temp\img1.jpg")
        self.assertEqual(env["SCN_IMG2"], r"D:\devlop\ZIV.AI\_test_step2\viggle_aspect_640x1024.png")
        self.assertEqual(env["SCN_IMG3"], r"D:\devlop\ZIV.AI\_test_step2\viggle_aspect_640x1024.png")
        self.assertEqual(env["REPRO_HARNESS"], runner.DEFAULT_HARNESS)

    def test_zero_ref_omits_refs(self):
        env = runner.build_env(make_opts(nref=0), base={})
        self.assertNotIn("SCN_IMG2", env)
        self.assertNotIn("SCN_IMG3", env)

    def test_one_ref_omits_img3(self):
        env = runner.build_env(make_opts(nref=1), base={})
        self.assertIn("SCN_IMG2", env)
        self.assertNotIn("SCN_IMG3", env)

    def test_input_env_subset(self):
        env = runner.build_env(make_opts(), base={})
        subset = runner.input_env(env)
        self.assertIn("SCN_OP", subset)
        self.assertNotIn("PATH", subset)


class ParseAndHashTests(unittest.TestCase):
    def test_parse_result(self):
        line = (
            "LOADED wall=8.0s\n"
            "RESULT wall=45.7s {'output_path': 'D:\\\\temp\\\\b3.png', "
            "'seed': 42, 'width': 1216, 'height': 1536}\n"
        )
        wall, payload = runner.parse_result(line)
        self.assertEqual(wall, 45.7)
        self.assertEqual(payload["output_path"], r"D:\temp\b3.png")

    def test_parse_result_absent(self):
        self.assertIsNone(runner.parse_result("no result here"))

    def test_sha256_matches_hashlib(self):
        with tempfile.TemporaryDirectory() as td:
            path = os.path.join(td, "x.bin")
            with open(path, "wb") as fh:
                fh.write(b"hello ziv")
            expect = hashlib.sha256(b"hello ziv").hexdigest()
            self.assertEqual(runner.sha256_file(path), expect)

    def test_sha256_missing(self):
        self.assertIsNone(runner.sha256_file(r"D:\does\not\exist.png"))


class RunScenarioTests(unittest.TestCase):
    def test_gpu_busy_blocks_before_launch(self):
        def boom(*a, **k):
            raise AssertionError("must not launch when GPU is busy")

        code, res = runner.run_scenario(
            make_opts(),
            check_gpu_fn=lambda: {"free": False, "reason": "used 9000 MiB"},
            popen_factory=boom,
        )
        self.assertEqual(code, 1)
        self.assertEqual(res["error"], "gpu_busy")

    def test_happy_path_writes_json(self):
        with tempfile.TemporaryDirectory() as td:
            out = os.path.join(td, "b3.png")
            with open(out, "wb") as fh:
                fh.write(b"\x89PNG fake bytes")
            stdout = (
                "LOADED wall=8.0s\n"
                "RESULT wall=45.7s {'output_path': %r, 'seed': 42}\n" % out
            ).encode("utf-8")
            code, res = runner.run_scenario(
                make_opts(out=out, max_wall=1000.0),
                check_gpu_fn=lambda: {"free": True},
                popen_factory=fake_factory(out=stdout),
                used_mib_fn=lambda: 500,
                rss_gb_fn=lambda pid: 0.5,
                kill_fn=lambda pid: None,
                sleep_fn=lambda s: None,
                poll_interval=0.0,
                cwd=td,
            )
            self.assertEqual(code, 0)
            self.assertFalse(res["killed"])
            self.assertEqual(res["wall_sec"], 45.7)
            self.assertEqual(res["output_path"], out)
            self.assertEqual(
                res["sha256"], hashlib.sha256(b"\x89PNG fake bytes").hexdigest()
            )
            result_path = out + ".result.json"
            self.assertTrue(os.path.isfile(result_path))
            with open(result_path, encoding="utf-8") as fh:
                on_disk = json.load(fh)
            self.assertEqual(on_disk["sha256"], res["sha256"])
            self.assertEqual(on_disk["input_env"]["SCN_VIGGLE"], "0")

    def test_crash_no_result(self):
        with tempfile.TemporaryDirectory() as td:
            out = os.path.join(td, "b3.png")
            code, res = runner.run_scenario(
                make_opts(out=out),
                check_gpu_fn=lambda: {"free": True},
                popen_factory=fake_factory(out=b"", err=b"boom", returncode=1),
                used_mib_fn=lambda: 500,
                rss_gb_fn=lambda pid: 0.5,
                kill_fn=lambda pid: None,
                sleep_fn=lambda s: None,
                poll_interval=0.0,
                cwd=td,
            )
            self.assertEqual(code, 1)
            self.assertIsNone(res["wall_sec"])
            self.assertEqual(res["exit_code"], 1)


class RunGateTests(unittest.TestCase):
    def test_missing_yes_returns_2(self):
        err = io.StringIO()
        old = sys.stderr
        sys.stderr = err
        try:
            code = main(
                ["run", "--img1", "a.png", "--out", "b.png"]
            )
        finally:
            sys.stderr = old
        self.assertEqual(code, 2)
        self.assertIn("Z30", err.getvalue())

    def test_yes_present_reaches_runner(self):
        captured = {}

        def fake_run(opts):
            captured["opts"] = opts
            return 0, {"sha256": "deadbeef"}

        old = runner.run_scenario
        runner.run_scenario = fake_run
        try:
            code = main(
                [
                    "run",
                    "--img1", "a.png",
                    "--img2", "b.png",
                    "--img3", "c.png",
                    "--out", "d.png",
                    "--viggle", "0",
                    "--yes",
                ]
            )
        finally:
            runner.run_scenario = old
        self.assertEqual(code, 0)
        self.assertEqual(captured["opts"].viggle, 0)
        self.assertEqual(captured["opts"].nref, 0)


if __name__ == "__main__":
    unittest.main()
