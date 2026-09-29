"""GPU idle check for zivcli.

Two criteria (Z30):

1. ``memory.used`` on the target GPU is below ``threshold_mib``.
2. No compute-app consumes more than ``large_app_mib``.

Parsing is split into pure functions so it can be unit tested without a GPU.
"""

from __future__ import annotations

import subprocess

SM = "nvidia-smi"
DEFAULT_THRESHOLD_MIB = 1600
DEFAULT_LARGE_APP_MIB = 500

_USED_ARGS = ["--query-gpu=memory.used", "--format=csv,noheader,nounits"]
_APPS_ARGS = [
    "--query-compute-apps=pid,process_name,used_memory",
    "--format=csv,noheader,nounits",
]


class GpuCheckError(RuntimeError):
    """Raised when nvidia-smi is unavailable or its output cannot be parsed."""


def run_smi(args, timeout=10.0):
    try:
        proc = subprocess.run(
            [SM, *args], capture_output=True, text=True, timeout=timeout
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise GpuCheckError("nvidia-smi failed: %s" % exc) from exc
    if proc.returncode != 0:
        raise GpuCheckError(
            "nvidia-smi exit=%d: %s" % (proc.returncode, proc.stderr.strip())
        )
    return proc.stdout


def parse_used_mib(text):
    for line in text.splitlines():
        line = line.strip()
        if not line:
            continue
        field = line.split(",")[0].strip()
        try:
            return int(float(field))
        except ValueError as exc:
            raise GpuCheckError("cannot parse memory.used from %r" % line) from exc
    raise GpuCheckError("empty nvidia-smi memory.used output")


def parse_compute_apps(text):
    apps = []
    for line in text.splitlines():
        line = line.strip()
        if not line:
            continue
        parts = [p.strip() for p in line.split(",")]
        if len(parts) < 3:
            continue
        pid, name, used = parts[0], parts[1], parts[2]
        try:
            used_mib = int(used)
        except ValueError:
            used_mib = None
        apps.append({"pid": pid, "name": name, "used_mib": used_mib})
    return apps


def query_used_mib():
    return parse_used_mib(run_smi(_USED_ARGS))


def query_compute_apps():
    return parse_compute_apps(run_smi(_APPS_ARGS))


def evaluate(
    used_mib,
    apps,
    threshold_mib=DEFAULT_THRESHOLD_MIB,
    large_app_mib=DEFAULT_LARGE_APP_MIB,
):
    large = [
        a
        for a in apps
        if a.get("used_mib") is not None and a["used_mib"] > large_app_mib
    ]
    reasons = []
    if used_mib >= threshold_mib:
        reasons.append("used %d MiB >= threshold %d MiB" % (used_mib, threshold_mib))
    for a in large:
        reasons.append(
            "large compute-app pid=%s name=%s used=%d MiB"
            % (a["pid"], a["name"], a["used_mib"])
        )
    free = not reasons
    return {
        "free": free,
        "used_mib": used_mib,
        "threshold_mib": threshold_mib,
        "large_apps": large,
        "reason": "ok" if free else "; ".join(reasons),
    }


def check_gpu(
    threshold_mib=DEFAULT_THRESHOLD_MIB,
    large_app_mib=DEFAULT_LARGE_APP_MIB,
    smi_run=run_smi,
):
    used_mib = parse_used_mib(smi_run(_USED_ARGS))
    apps = parse_compute_apps(smi_run(_APPS_ARGS))
    return evaluate(used_mib, apps, threshold_mib, large_app_mib)
