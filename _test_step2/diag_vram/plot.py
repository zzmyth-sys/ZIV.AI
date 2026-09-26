"""CPU-only analyser for the A1/H1 VRAM waveform records.

Reads ``*.jsonl`` produced by ``python/server/diag_vram.py`` (ZIV_AI_DIAG=1),
writes one CSV per run, prints / writes a summary table, and optionally renders a
PNG when matplotlib happens to be installed (no new dependency is required).

Usage::

    python plot.py                 # analyse every *.jsonl next to this script
    python plot.py --dir <dir>     # analyse another directory
    python plot.py --run-id 1024_cold
"""

import argparse
import csv
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

PHASE_ORDER = ("dit", "te", "vae")


def _num(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def load_events(path):
    events = []
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except ValueError:
                continue
    events.sort(key=lambda e: e.get("ts_ms") or 0.0)
    return events


def task_ids(events):
    seen = []
    for event in events:
        tid = event.get("task_id")
        if tid and tid not in seen:
            seen.append(tid)
    return seen


def measured_task(events, mode):
    ids = task_ids(events)
    if not ids:
        return None
    if mode == "last":
        return ids[-1]
    if mode == "first":
        return ids[0]
    return mode if mode in ids else ids[-1]


def _task_events(events, task_id):
    kinds = ("accepted", "progress", "preview", "result", "error")
    return [e for e in events if e.get("task_id") == task_id and e.get("kind") in kinds]


def _first(items, pred):
    for item in items:
        if pred(item):
            return item
    return None


def _last(items, pred):
    found = None
    for item in items:
        if pred(item):
            found = item
    return found


def analyse(events, task_id):
    ev = _task_events(events, task_id)
    result = {
        "task_id": task_id,
        "total": None,
        "load": None,
        "dit": None,
        "te": None,
        "vae": None,
        "sampling": None,
        "peak": None,
        "order": None,
        "max_drop": None,
        "load_end_vram": None,
        "load_present": False,
        "prep": None,
        "status": "unknown",
    }
    if not ev:
        return result, ev

    t0 = ev[0]["ts_ms"]
    t_end = ev[-1]["ts_ms"]
    result["total"] = t_end - t0

    end_event = _last(ev, lambda e: e.get("kind") in ("result", "error"))
    if end_event is not None:
        result["status"] = end_event.get("kind")

    load_events = [e for e in ev if e.get("stage") == "loading_model"]
    result["load_present"] = bool(load_events)

    sampling_ready = _first(
        ev, lambda e: e.get("stage") == "sampling" and e.get("sub_stage") == "ready"
    )

    if load_events:
        lstart = load_events[0]["ts_ms"]
        result["prep"] = lstart - t0
        lend = sampling_ready or _first(ev, lambda e: e.get("stage") == "sampling")
        if lend is not None:
            result["load"] = lend["ts_ms"] - lstart
            result["load_end_vram"] = lend.get("vram_mb")

        order = []
        drops = []
        for sub in PHASE_ORDER:
            sub_events = [e for e in load_events if e.get("sub_stage") == sub]
            if sub_events:
                order.append(sub)
                result[sub] = sub_events[-1]["ts_ms"] - sub_events[0]["ts_ms"]

        ordered = sorted(load_events, key=lambda e: e["ts_ms"])
        for prev, cur in zip(ordered, ordered[1:]):
            a, b = prev.get("vram_mb"), cur.get("vram_mb")
            if _num(a) and _num(b) and a > 0 and b > 0 and a > b:
                drops.append(a - b)
        if drops:
            result["max_drop"] = max(drops)
        result["order"] = "->".join(order)

    if sampling_ready is not None:
        result["sampling"] = t_end - sampling_ready["ts_ms"]
    else:
        first_sampling = _first(ev, lambda e: e.get("stage") == "sampling") or _first(
            ev, lambda e: _num(e.get("step")) and e.get("step", 0) >= 1
        )
        if first_sampling is not None:
            result["sampling"] = t_end - first_sampling["ts_ms"]

    vrams = [e["vram_mb"] for e in ev if _num(e.get("vram_mb")) and e["vram_mb"] > 0]
    if vrams:
        result["peak"] = max(vrams)

    return result, ev


def write_csv(path, events, task_id):
    rel0 = events[0]["ts_ms"] if events else 0.0
    with open(path, "w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(
            ["rel_ms", "ts_ms", "kind", "stage", "sub_stage", "fraction", "step", "total", "vram_mb", "task_id"]
        )
        for event in events:
            writer.writerow(
                [
                    round((event.get("ts_ms") or 0.0) - rel0, 3),
                    event.get("ts_ms"),
                    event.get("kind"),
                    event.get("stage"),
                    event.get("sub_stage"),
                    event.get("fraction"),
                    event.get("step"),
                    event.get("total"),
                    event.get("vram_mb"),
                    event.get("task_id"),
                ]
            )


def fmt_ms(value):
    return "-" if not _num(value) else "%.1fs" % (value / 1000.0)


def fmt_mb(value):
    return "-" if not _num(value) else "%.0f" % value


def resolution_of(run_id):
    parts = run_id.split("_")
    return parts[-1] if parts and parts[-1].isdigit() else parts[0]


def mode_of(run_id):
    if run_id.endswith("_hot"):
        return "hot"
    if run_id.endswith("_cold"):
        return "cold"
    return run_id.split("_")[0]


def note_of(row):
    bits = []
    if not row["load_present"]:
        bits.append("已加载(无加载阶段)")
    else:
        if _num(row["prep"]):
            bits.append("预备(import)=%.1fs" % (row["prep"] / 1000.0))
        if row["order"]:
            bits.append("顺序=%s" % row["order"])
    if _num(row["max_drop"]):
        bits.append("最大回落=%.0fMiB" % row["max_drop"])
    if row["status"] and row["status"] != "result":
        bits.append("结束=%s" % row["status"])
    return "; ".join(bits) or "-"


def plot_png(run_id, events, task_id, phases, path):
    try:
        import matplotlib

        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except Exception:
        return False

    ev = [e for e in _task_events(events, task_id) if _num(e.get("vram_mb"))]
    if not ev:
        return False
    t0 = ev[0]["ts_ms"]
    xs = [(e["ts_ms"] - t0) / 1000.0 for e in ev]
    ys = [e["vram_mb"] for e in ev]

    figure, axes = plt.subplots(figsize=(11, 4.5))
    axes.plot(xs, ys, marker=".", linewidth=1.0, color="#1f77b4")
    axes.set_title("VRAM waveform: %s" % run_id)
    axes.set_xlabel("seconds")
    axes.set_ylabel("VRAM used (MiB)")
    axes.grid(True, alpha=0.3)
    figure.tight_layout()
    figure.savefig(path, dpi=110)
    plt.close(figure)
    return True


def main(argv=None):
    parser = argparse.ArgumentParser(description="Analyse diag_vram JSONL records.")
    parser.add_argument("--dir", default=HERE)
    parser.add_argument("--run-id", default=None, help="single run id (file stem)")
    parser.add_argument("--task", default="last", help="first | last | <task_id>")
    args = parser.parse_args(argv)

    pattern = os.path.join(args.dir, (args.run_id + ".jsonl") if args.run_id else "*.jsonl")
    paths = sorted(glob.glob(pattern))
    paths = [p for p in paths if os.path.basename(p) != "summary.jsonl"]
    if not paths:
        print("no jsonl found for %s" % pattern)
        return 1

    rows = []
    for path in paths:
        run_id = os.path.splitext(os.path.basename(path))[0]
        events = load_events(path)
        task = measured_task(events, args.task)
        phases, measured = analyse(events, task)
        write_csv(os.path.join(args.dir, run_id + ".csv"), events, task)
        png = os.path.join(args.dir, run_id + "_vram.png")
        plotted = plot_png(run_id, events, task, phases, png)
        rows.append((run_id, phases, plotted, len(events)))

    header = "| 分辨率 | 冷/热 | 总时间 | 加载 | TE | DiT 加载 | 采样 | VAE | 峰值显存 | 备注 |"
    sep = "|---|---|---|---|---|---|---|---|---|---|"
    lines = ["# diag_vram summary", "", header, sep]
    for run_id, row, plotted, count in rows:
        lines.append(
            "| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |"
            % (
                resolution_of(run_id),
                mode_of(run_id),
                fmt_ms(row["total"]),
                fmt_ms(row["load"]),
                fmt_ms(row["te"]),
                fmt_ms(row["dit"]),
                fmt_ms(row["sampling"]),
                fmt_ms(row["vae"]),
                fmt_mb(row["peak"]),
                note_of(row),
            )
        )

    summary = "\n".join(lines) + "\n"
    with open(os.path.join(args.dir, "summary.md"), "w", encoding="utf-8") as handle:
        handle.write(summary)

    print(summary)
    for run_id, _row, plotted, count in rows:
        print("%s: %d events, png=%s" % (run_id, count, "yes" if plotted else "no"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
