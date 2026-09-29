"""Host-RAM / shared-GPU-memory guard (Windows; stdlib only, no torch / comfy).

专用 VRAM 打满后，Windows 会把权重外溢到「共享 GPU 内存」(= 系统 RAM)，而 DynamicVRAM 还会把
权重额外 pin 在 host RAM（``comfy.model_management.pinned_hostbuf_size`` ≈ 2× 模型）。二者叠加会让
host RAM / commit 激增，严重时整机卡死（只能强关）——本模块在采样每一步读 Windows 内存计数，
越线即抛 :class:`HostMemoryError`，由 ``pipeline.run`` 的 OOM 降级路径接管，**在驱动/系统崩之前**
干净卸载退出，而不是把机器拖死。

阈值来自 ``config``（env 可覆盖）；非 Windows 或 API 不可用时全部 no-op（返回 ``None`` 不抛）。
"""

import ctypes
import logging
import os
from ctypes import wintypes

import config

_LOG = logging.getLogger("zivai.server")

_GIB = 1024 ** 3


class HostMemoryError(RuntimeError):
    """host RAM / commit 越过安全线；当作 OOM 处理（触发分辨率降级）。"""


class _MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [
        ("dwLength", wintypes.DWORD),
        ("dwMemoryLoad", wintypes.DWORD),
        ("ullTotalPhys", ctypes.c_ulonglong),
        ("ullAvailPhys", ctypes.c_ulonglong),
        ("ullTotalPageFile", ctypes.c_ulonglong),
        ("ullAvailPageFile", ctypes.c_ulonglong),
        ("ullTotalVirtual", ctypes.c_ulonglong),
        ("ullAvailVirtual", ctypes.c_ulonglong),
        ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
    ]


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


def _kernel32():
    return getattr(ctypes, "windll", None) and ctypes.windll.kernel32


def _psapi():
    lib = getattr(ctypes, "windll", None)
    return getattr(lib, "psapi", None) if lib is not None else None


def _rss():
    """This process's working set in bytes, or ``None`` when the API is unavailable.

    The handle must be declared as ``HANDLE`` (the default ctypes arg marshalling passes the
    ``(HANDLE)-1`` pseudo-handle as a 32-bit int, which makes ``GetProcessMemoryInfo`` fail).
    """
    k32 = _kernel32()
    api = _psapi()
    if k32 is None or api is None:
        return None
    try:
        k32.GetCurrentProcess.restype = wintypes.HANDLE
        fn = api.GetProcessMemoryInfo
        fn.argtypes = [wintypes.HANDLE, ctypes.POINTER(_PROCESS_MEMORY_COUNTERS), wintypes.DWORD]
        fn.restype = wintypes.BOOL
        counters = _PROCESS_MEMORY_COUNTERS()
        counters.cb = ctypes.sizeof(counters)
        if fn(k32.GetCurrentProcess(), ctypes.byref(counters), counters.cb):
            return int(counters.WorkingSetSize)
    except (OSError, AttributeError):
        return None
    return None


def snapshot():
    """Return ``{total_phys, avail_phys, total_commit, avail_commit, rss}`` in bytes, or ``None``.

    ``avail_commit`` = available page file (the commit budget); ``rss`` = this process's working set
    (best-effort; ``None`` when the API is unavailable).
    """
    k32 = _kernel32()
    if k32 is None:
        return None
    try:
        status = _MEMORYSTATUSEX()
        status.dwLength = ctypes.sizeof(status)
        if not k32.GlobalMemoryStatusEx(ctypes.byref(status)):
            return None
    except (OSError, AttributeError):
        return None

    rss = _rss()

    return {
        "total_phys": int(status.ullTotalPhys),
        "avail_phys": int(status.ullAvailPhys),
        "total_commit": int(status.ullTotalPageFile),
        "avail_commit": int(status.ullAvailPageFile),
        "rss": rss,
    }


def enabled():
    if os.name != "nt":
        return False
    if not getattr(config, "GUARD_ENABLED", True):
        return False
    return (
        getattr(config, "GUARD_MIN_FREE_RAM_GB", 0) > 0
        or getattr(config, "GUARD_MIN_FREE_COMMIT_GB", 0) > 0
        or getattr(config, "GUARD_MAX_RSS_GB", 0) > 0
    )


def check(snap=None):
    """Return a human reason string when over budget, else ``None`` (also ``None`` when disabled)."""
    if not enabled():
        return None
    snap = snapshot() if snap is None else snap
    if snap is None:
        return None

    min_free_ram = getattr(config, "GUARD_MIN_FREE_RAM_GB", 0) * _GIB
    min_free_commit = getattr(config, "GUARD_MIN_FREE_COMMIT_GB", 0) * _GIB
    max_rss = getattr(config, "GUARD_MAX_RSS_GB", 0) * _GIB

    if min_free_ram and snap["avail_phys"] < min_free_ram:
        return "available physical RAM %.1fGB < %.1fGB" % (snap["avail_phys"] / _GIB, min_free_ram / _GIB)
    if min_free_commit and snap["avail_commit"] < min_free_commit:
        return "available commit %.1fGB < %.1fGB" % (snap["avail_commit"] / _GIB, min_free_commit / _GIB)
    if max_rss and snap["rss"] and snap["rss"] > max_rss:
        return "process RSS %.1fGB > %.1fGB" % (snap["rss"] / _GIB, max_rss / _GIB)
    return None


def enforce():
    """Raise :class:`HostMemoryError` when over budget; no-op otherwise."""
    reason = check()
    if reason:
        _LOG.warning("host memory guard tripped: %s; aborting sampling", reason)
        raise HostMemoryError("host memory budget exceeded (%s)" % reason)
