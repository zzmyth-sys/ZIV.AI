"""Unit tests for zivcli.gpu (no GPU required; nvidia-smi output mocked)."""

import os
import sys
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from tools.zivcli import gpu  # noqa: E402


def fake_smi(used_text, apps_text):
    def _run(args, timeout=10.0):
        if "--query-gpu=memory.used" in args:
            return used_text
        if any("query-compute-apps" in a for a in args):
            return apps_text
        raise AssertionError("unexpected smi args: %r" % (args,))

    return _run


class ParseUsedTests(unittest.TestCase):
    def test_simple(self):
        self.assertEqual(gpu.parse_used_mib("1108\n"), 1108)

    def test_float_form(self):
        self.assertEqual(gpu.parse_used_mib("1108.0\n"), 1108)

    def test_blank_lines_skipped(self):
        self.assertEqual(gpu.parse_used_mib("\n\n  1234  \n"), 1234)

    def test_empty_raises(self):
        with self.assertRaises(gpu.GpuCheckError):
            gpu.parse_used_mib("   \n")


class ParseAppsTests(unittest.TestCase):
    def test_normal_and_permission_na(self):
        text = (
            "2044, [Insufficient Permissions], [N/A]\n"
            "17048, C:\\uv\\python.exe, 1234\n"
        )
        apps = gpu.parse_compute_apps(text)
        self.assertEqual(len(apps), 2)
        self.assertIsNone(apps[0]["used_mib"])
        self.assertEqual(apps[1]["used_mib"], 1234)
        self.assertEqual(apps[1]["pid"], "17048")

    def test_empty(self):
        self.assertEqual(gpu.parse_compute_apps(""), [])


class EvaluateTests(unittest.TestCase):
    def test_free_when_below_threshold_and_no_large(self):
        res = gpu.evaluate(1108, [], threshold_mib=1600)
        self.assertTrue(res["free"])
        self.assertEqual(res["reason"], "ok")

    def test_busy_when_used_at_threshold(self):
        res = gpu.evaluate(1600, [], threshold_mib=1600)
        self.assertFalse(res["free"])
        self.assertIn("threshold", res["reason"])

    def test_boundary_below(self):
        res = gpu.evaluate(1599, [], threshold_mib=1600)
        self.assertTrue(res["free"])

    def test_busy_when_large_app(self):
        apps = [{"pid": "1", "name": "x.exe", "used_mib": 900}]
        res = gpu.evaluate(1108, apps, threshold_mib=1600, large_app_mib=500)
        self.assertFalse(res["free"])
        self.assertEqual(len(res["large_apps"]), 1)
        self.assertIn("large compute-app", res["reason"])

    def test_small_app_ok(self):
        apps = [{"pid": "1", "name": "x.exe", "used_mib": 200}]
        res = gpu.evaluate(1108, apps, threshold_mib=1600, large_app_mib=500)
        self.assertTrue(res["free"])

    def test_na_app_ignored(self):
        apps = [{"pid": "1", "name": "y", "used_mib": None}]
        res = gpu.evaluate(1108, apps, threshold_mib=1600)
        self.assertTrue(res["free"])


class CheckGpuTests(unittest.TestCase):
    def test_free(self):
        res = gpu.check_gpu(smi_run=fake_smi("1108\n", ""))
        self.assertTrue(res["free"])
        self.assertEqual(res["used_mib"], 1108)
        self.assertEqual(res["threshold_mib"], 1600)

    def test_busy_by_large_app(self):
        res = gpu.check_gpu(
            smi_run=fake_smi("1108\n", "42, python.exe, 8000\n")
        )
        self.assertFalse(res["free"])
        self.assertEqual(res["large_apps"][0]["used_mib"], 8000)

    def test_threshold_override(self):
        res = gpu.check_gpu(threshold_mib=1000, smi_run=fake_smi("1108\n", ""))
        self.assertFalse(res["free"])


if __name__ == "__main__":
    unittest.main()
