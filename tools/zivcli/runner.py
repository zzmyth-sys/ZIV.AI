"""``ziv run``: drive one pipeline scenario through the real harness.

Pure-Python: no dependency on the PowerShell runners.  We build the SCN_*
environment, launch ``s5s6_scenarios.py`` under the embedded interpreter,
sample VRAM/RSS with a watchdog thread, parse the RESULT line, hash the
output PNG and persist a machine-readable result JSON.
"""

from __future__ import annotations

import ast
import csv
import hashlib
import json
import os
import re
import subprocess
import sys
import threading
import time
from dataclasses import dataclass

from . import gpu

HOME = r"D:\devlop\ZIV.AI"
ZIVCLI_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PYTHON = os.path.join(HOME, "Comfyui", "python_embeded", "python.exe")
DEFAULT_HARNESS = os.path.join(ZIVCLI_DIR, "harness")
DEFAULT_DRIVER = os.path.join(DEFAULT_HARNESS, "s5s6_scenarios.py")
DEFAULT_CWD = DEFAULT_HARNESS

GIB = float(1 << 30)
POLL_INTERVAL = 0.5
VRAM_SUSTAIN = 6
TAIL_CHARS = 8000

SCN_ENV_KEYS = (
    "SCN_OP",
    "SCN_SIDE",
    "SCN_NREF",
    "SCN_STEPS",
    "SCN_VIGGLE",
    "SCN_OUT",
    "SCN_IMG1",
    "SCN_IMG2",
    "SCN_IMG3",
    "REPRO_HARNESS",
)


@dataclass
class RunOptions:
    op: str
    side: int
    nref: int
    steps: int
    viggle: int
    img1: str
    out: str
    img2: str = None
    img3: str = None
    max_wall: float = 120.0
    max_vram: float = 15500.0
    max_rss_gb: float = 18.0
    harness: str = DEFAULT_HARNESS


def build_env(opts, base=None):
    env = dict(os.environ if base is None else base)
    env["SCN_OP"] = str(opts.op)
    env["SCN_SIDE"] = str(opts.side)
    env["SCN_NREF"] = str(opts.nref)
    env["SCN_STEPS"] = str(opts.steps)
    env["SCN_VIGGLE"] = str(opts.viggle)
    env["SCN_OUT"] = str(opts.out)
    env["SCN_IMG1"] = str(opts.img1)
    if opts.nref >= 1 and opts.img2:
        env["SCN_IMG2"] = str(opts.img2)
    if opts.nref >= 2 and opts.img3:
        env["SCN_IMG3"] = str(opts.img3)
    env["REPRO_HARNESS"] = str(opts.harness)
    return env


def input_env(env):
    return {k: env[k] for k in SCN_ENV_KEYS if k in env}


_RESULT_RE = re.compile(r"RESULT wall=([0-9.]+)s (\{.*\})")


def parse_result(stdout):
    m = _RESULT_RE.search(stdout or "")
    if not m:
        return None
    try:
        payload = ast.literal_eval(m.group(2))
    except (ValueError, SyntaxError):
        return None
    if not isinstance(payload, dict):
        return None
    return float(m.group(1)), payload


def sha256_file(path):
    if not path or not os.path.isfile(path):
        return None
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def process_rss_gb(pid):
    if sys.platform == "win32":
        ws = _working_set_bytes_ctypes(pid)
        if ws is None:
            ws = _working_set_bytes_tasklist(pid)
    else:
        ws = None
    if ws is None:
        return None
    return ws / GIB


try:  # Windows-only structures; defined lazily so imports stay cheap.
    import ctypes
    from ctypes import wintypes

    class _PROCESS_MEMORY_COUNTERS(ctypes.Structure):
        _fields_ = [
            ("cb", wintypes.DWORD),
            ("PageFaultCount", wintypes.DWORD),
            ("PeakWorkingSetSize", ctypes.c_size_t),
            ("WorkingSetSize", ctypes.c_size_t),
            ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
            ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
            ("PagefileUsage", ctypes.c_size_t),
            ("PeakPagefileUsage", ctypes.c_size_t),
        ]

    _psapi = ctypes.WinDLL("psapi", use_last_error=True)
    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _psapi.GetProcessMemoryInfo.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(_PROCESS_MEMORY_COUNTERS),
        wintypes.DWORD,
    ]
    _psapi.GetProcessMemoryInfo.restype = wintypes.BOOL
    _kernel32.OpenProcess.restype = wintypes.HANDLE
except Exception:  # noqa: BLE001 - non-Windows or API missing
    _psapi = None
    _kernel32 = None


def _working_set_bytes_ctypes(pid):
    if _psapi is None or _kernel32 is None:
        return None
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    PROCESS_VM_READ = 0x0010
    handle = _kernel32.OpenProcess(
        PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, False, int(pid)
    )
    if not handle:
        return None
    try:
        counters = _PROCESS_MEMORY_COUNTERS()
        counters.cb = ctypes.sizeof(counters)
        ok = _psapi.GetProcessMemoryInfo(
            handle, ctypes.byref(counters), counters.cb
        )
        if not ok:
            return None
        return int(counters.WorkingSetSize)
    finally:
        _kernel32.CloseHandle(handle)


def _working_set_bytes_tasklist(pid):
    try:
        proc = subprocess.run(
            ["tasklist", "/FI", "PID eq %d" % int(pid), "/FO", "CSV", "/NH"],
            capture_output=True,
            text=True,
            timeout=10,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    rows = [r for r in csv.reader(proc.stdout.splitlines()) if r]
    if not rows or len(rows[0]) < 5:
        return None
    digits = re.sub(r"[^0-9]", "", rows[0][-1])
    if not digits:
        return None
    return int(digits) * 1024  # tasklist reports KiB


def kill_tree(pid):
    try:
        subprocess.run(
            ["taskkill", "/PID", str(int(pid)), "/T", "/F"],
            capture_output=True,
            text=True,
            timeout=15,
        )
    except (OSError, subprocess.SubprocessError):
        pass


def _decode(raw):
    if raw is None:
        return ""
    if isinstance(raw, bytes):
        return raw.decode("utf-8", "replace")
    return raw


def _tail(text):
    text = text or ""
    return text[-TAIL_CHARS:]


def _safe(fn, *args):
    try:
        return fn(*args)
    except Exception:  # noqa: BLE001 - watchdog must never die from a probe
        return None


def run_scenario(
    opts,
    *,
    check_gpu_fn=None,
    popen_factory=None,
    used_mib_fn=None,
    rss_gb_fn=None,
    kill_fn=None,
    sha_fn=None,
    now_fn=None,
    sleep_fn=None,
    poll_interval=POLL_INTERVAL,
    vram_sustain=VRAM_SUSTAIN,
    python_exe=DEFAULT_PYTHON,
    driver=DEFAULT_DRIVER,
    cwd=DEFAULT_CWD,
    base_env=None,
    write_result=True,
):
    check_gpu_fn = check_gpu_fn or gpu.check_gpu
    popen_factory = popen_factory or subprocess.Popen
    used_mib_fn = used_mib_fn or gpu.query_used_mib
    rss_gb_fn = rss_gb_fn or process_rss_gb
    kill_fn = kill_fn or kill_tree
    sha_fn = sha_fn or sha256_file
    now_fn = now_fn or time.monotonic
    sleep_fn = sleep_fn or time.sleep

    try:
        verdict = check_gpu_fn()
    except Exception as exc:  # noqa: BLE001
        return 1, {
            "error": "gpu_check_failed",
            "reason": str(exc),
            "output_path": opts.out,
        }
    if not verdict.get("free"):
        return 1, {"error": "gpu_busy", "gpu_check": verdict, "output_path": opts.out}

    env = build_env(opts, base=base_env)
    cmd = [python_exe, "-s", driver]
    proc = popen_factory(
        cmd,
        cwd=cwd,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )

    peaks = {"vram": None, "rss": None}
    state = {"killed": False, "reason": None}
    stop = threading.Event()

    def watchdog():
        sustained = 0
        start = now_fn()
        while not stop.is_set():
            if proc.poll() is not None:
                break
            used = _safe(used_mib_fn)
            if used is not None:
                peaks["vram"] = used if peaks["vram"] is None else max(peaks["vram"], used)
                sustained = sustained + 1 if used >= opts.max_vram else 0
            rss = _safe(rss_gb_fn, proc.pid)
            if rss is not None:
                peaks["rss"] = rss if peaks["rss"] is None else max(peaks["rss"], rss)
            reason = None
            if vram_sustain and sustained >= vram_sustain:
                reason = "vram %s MiB sustained %d polls" % (used, sustained)
            elif rss is not None and opts.max_rss_gb > 0 and rss > opts.max_rss_gb:
                reason = "rss %.1f GB > %.1f GB" % (rss, opts.max_rss_gb)
            elif (now_fn() - start) > opts.max_wall:
                reason = "wall %.0fs > %.0fs" % (now_fn() - start, opts.max_wall)
            if reason:
                state["killed"] = True
                state["reason"] = reason
                kill_fn(proc.pid)
                break
            sleep_fn(poll_interval)

    thread = threading.Thread(target=watchdog, daemon=True)
    thread.start()
    raw_out, raw_err = proc.communicate()
    stop.set()
    thread.join(timeout=2.0)

    out_text = _decode(raw_out)
    err_text = _decode(raw_err)
    exit_code = proc.returncode

    wall_sec = None
    driver_result = None
    out_path = opts.out
    parsed = parse_result(out_text)
    if parsed is not None:
        wall_sec, driver_result = parsed
        out_path = driver_result.get("output_path", opts.out)

    result = {
        "sha256": sha_fn(out_path),
        "wall_sec": wall_sec,
        "output_path": out_path,
        "peak_vram_mib": peaks["vram"],
        "peak_rss_gb": peaks["rss"],
        "exit_code": exit_code,
        "killed": state["killed"],
        "stdout_tail": _tail(out_text),
        "stderr_tail": _tail(err_text),
        "input_env": input_env(env),
    }
    if state["reason"]:
        result["kill_reason"] = state["reason"]
    if driver_result is not None:
        result["driver_result"] = driver_result

    if write_result:
        try:
            with open(out_path + ".result.json", "w", encoding="utf-8") as fh:
                json.dump(result, fh, ensure_ascii=False, indent=2)
        except OSError as exc:
            result["result_json_error"] = str(exc)

    return (0 if exit_code == 0 and not state["killed"] else 1), result
