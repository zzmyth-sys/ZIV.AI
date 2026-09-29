"""``ziv batch``: run a list of scenarios (JSON array) and summarise.

Each entry is either a golden reference ``{"golden": "b3"}`` or an inline
parameter set ``{"name": ..., "op": ..., "sha256": ...}``.  A failing entry
does not abort the batch; the summary reports every outcome.
"""

from __future__ import annotations

import json
import os

from . import gpu, runner, verify

DEFAULT_OUT_DIR = verify.DEFAULT_OUT_DIR


def load_list(path):
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    if not isinstance(data, list):
        raise ValueError("list-file must be a JSON array")
    for i, entry in enumerate(data):
        if not isinstance(entry, dict):
            raise ValueError("entry %d is not an object" % i)
        if "golden" not in entry and "name" not in entry:
            raise ValueError("entry %d needs 'golden' or 'name'" % i)
    return data


def _resolve(entry, out_dir, goldens):
    if "golden" in entry:
        gname = entry["golden"]
        if gname not in goldens:
            raise KeyError("unknown golden %r" % gname)
        golden = goldens[gname]
        name = entry.get("name", gname)
        out = os.path.join(out_dir, name + ".png")
        return name, verify.golden_to_opts(gname, golden, out), golden.get("sha256")

    name = entry["name"]
    out = os.path.join(out_dir, name + ".png")
    opts = runner.RunOptions(
        op=entry.get("op", "inpaint"),
        side=int(entry.get("side", 1024)),
        nref=int(entry.get("nref", 0)),
        steps=int(entry.get("steps", 40)),
        viggle=int(entry.get("viggle", 0)),
        img1=verify.resolve_path(entry["img1"]),
        img2=verify.resolve_path(entry.get("img2")),
        img3=verify.resolve_path(entry.get("img3")),
        prompt=entry.get("prompt"),
        out=out,
        harness=runner.DEFAULT_HARNESS,
    )
    return name, opts, entry.get("sha256")


def _run_entry(entry, out_dir, goldens, run_scenario_fn):
    try:
        name, opts, expected = _resolve(entry, out_dir, goldens)
    except (KeyError, TypeError) as exc:
        return {"name": entry.get("name") or entry.get("golden"), "pass": False,
                "error": str(exc)}

    code, res = run_scenario_fn(opts, write_result=False)
    actual_raw = res.get("sha256")
    actual = (actual_raw or "").strip().lower()
    expected_norm = (expected or "").strip().lower()
    passed = bool(actual) and actual == expected_norm

    verdict = {
        "name": name,
        "expected_sha256": expected,
        "actual_sha256": actual_raw,
        "pass": passed,
        "wall_sec": res.get("wall_sec"),
        "output_path": res.get("output_path"),
        "exit_code": res.get("exit_code"),
    }
    try:
        with open(os.path.join(out_dir, name + ".result.json"), "w", encoding="utf-8") as fh:
            json.dump(verdict, fh, ensure_ascii=False, indent=2)
    except OSError as exc:
        verdict["result_json_error"] = str(exc)
    return verdict


def run_batch(
    entries,
    *,
    out_dir=DEFAULT_OUT_DIR,
    goldens=None,
    run_scenario_fn,
    check_gpu_fn=None,
):
    check_gpu_fn = check_gpu_fn or gpu.check_gpu
    goldens = verify.load_goldens() if goldens is None else goldens
    out_dir = os.path.abspath(os.path.expanduser(out_dir))

    try:
        verdict = check_gpu_fn()
    except Exception as exc:  # noqa: BLE001
        return 1, {"error": "gpu_check_failed", "reason": str(exc),
                   "total": 0, "pass": 0, "fail": 0, "results": []}
    if not verdict.get("free"):
        return 1, {"error": "gpu_busy", "gpu_check": verdict,
                   "total": 0, "pass": 0, "fail": 0, "results": []}

    os.makedirs(out_dir, exist_ok=True)
    results = [
        _run_entry(entry, out_dir, goldens, run_scenario_fn) for entry in entries
    ]
    npass = sum(1 for r in results if r.get("pass"))
    fail = len(results) - npass
    summary = {
        "total": len(results),
        "pass": npass,
        "fail": fail,
        "results": results,
    }
    return (0 if fail == 0 else 1), summary
