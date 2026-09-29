"""``ziv verify``: run a golden scenario and compare its PNG hash.

Goldens are declared in ``tools/zivcli/goldens.json`` with all parameters and
input paths pinned.  Relative paths resolve against the ``tools/zivcli`` dir.
SHA256 comparison is case-insensitive.
"""

from __future__ import annotations

import json
import os

from . import runner

ZIVCLI_DIR = os.path.dirname(os.path.abspath(__file__))
GOLDENS_PATH = os.path.join(ZIVCLI_DIR, "goldens.json")
DEFAULT_OUT_DIR = os.path.join(ZIVCLI_DIR, "runs")


def load_goldens(path=GOLDENS_PATH):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def resolve_path(path, base=ZIVCLI_DIR):
    if not path:
        return None
    return path if os.path.isabs(path) else os.path.normpath(os.path.join(base, path))


def golden_to_opts(name, golden, out):
    return runner.RunOptions(
        op=golden["op"],
        side=golden["side"],
        nref=golden["nref"],
        steps=golden["steps"],
        viggle=golden["viggle"],
        img1=resolve_path(golden["img1"]),
        img2=resolve_path(golden.get("img2")),
        img3=resolve_path(golden.get("img3")),
        out=out,
        harness=runner.DEFAULT_HARNESS,
    )


def run_golden(
    name,
    golden,
    *,
    run_scenario_fn,
    out_dir=DEFAULT_OUT_DIR,
    write_verdict=True,
):
    out_dir = os.path.abspath(os.path.expanduser(out_dir))
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, name + ".png")
    opts = golden_to_opts(name, golden, out)

    code, res = run_scenario_fn(opts, write_result=False)

    expected = (golden.get("sha256") or "").strip().lower()
    actual_raw = res.get("sha256")
    actual = (actual_raw or "").strip().lower()
    passed = bool(actual) and actual == expected

    verdict = {
        "golden": name,
        "expected_sha256": golden.get("sha256"),
        "actual_sha256": actual_raw,
        "pass": passed,
        "wall_sec": res.get("wall_sec"),
        "output_path": res.get("output_path"),
        "exit_code": res.get("exit_code"),
    }
    if write_verdict:
        verdict_path = os.path.join(out_dir, name + ".result.json")
        with open(verdict_path, "w", encoding="utf-8") as fh:
            json.dump(verdict, fh, ensure_ascii=False, indent=2)
    return (0 if passed else 1), verdict


def run_verify(name, *, run_scenario_fn, goldens=None, out_dir=DEFAULT_OUT_DIR):
    goldens = load_goldens() if goldens is None else goldens
    if name not in goldens:
        return 2, {
            "golden": name,
            "error": "unknown_golden",
            "known": sorted(goldens),
        }
    return run_golden(
        name, goldens[name], run_scenario_fn=run_scenario_fn, out_dir=out_dir
    )
