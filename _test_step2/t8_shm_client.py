import mmap, struct, ctypes, time, sys
from ctypes import wintypes

MapName = "zivai_shm_bench"
cap = 8 * 1024 * 1024 + 64

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
SYNCHRONIZE = 0x00100000
EVENT_MODIFY_STATE = 0x0002
WAIT_OBJECT_0 = 0
INFINITE = 0xFFFFFFFF

kernel32.OpenEventW.restype = wintypes.HANDLE
kernel32.OpenEventW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
kernel32.WaitForSingleObject.restype = wintypes.DWORD
kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel32.SetEvent.restype = wintypes.BOOL
kernel32.SetEvent.argtypes = [wintypes.HANDLE]

def open_event(name):
    for _ in range(200):
        h = kernel32.OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, False, name)
        if h:
            return h
        time.sleep(0.1)
    raise OSError("cannot open event %s" % name)

def wait(h, ms=INFINITE):
    return kernel32.WaitForSingleObject(h, ms)

def setev(h):
    return kernel32.SetEvent(h)

try:
    data_ready = open_event("zivai_shm_data_ready")
    ack = open_event("zivai_shm_ack")
    mm = mmap.mmap(-1, cap, tagname=MapName)
except Exception as e:
    print("SETUP_FAIL %r" % e)
    sys.exit(1)

print("PY_SHM_CONNECTED")
count = 0
total = 0
t0 = time.time()
while True:
    r = wait(data_ready)
    if r != WAIT_OBJECT_0:
        print("WAIT_FAIL r=%s" % r)
        break
    mm.seek(0)
    n = struct.unpack("<i", mm.read(4))[0]
    if n < 0 or n > cap - 4:
        print("TERMINATE n=%d" % n)
        break
    data = mm.read(n)
    setev(ack)
    count += 1
    total += n

print("PY_DONE count=%d total_MB=%.2f elapsed_s=%.2f" % (count, total / 1024 / 1024, time.time() - t0))
mm.close()
