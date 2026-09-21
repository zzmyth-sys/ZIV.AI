import time, struct, sys

PIPE = r"\\.\pipe\zivai_bench"

f = None
for i in range(100):
    try:
        f = open(PIPE, "r+b", buffering=0)
        break
    except (FileNotFoundError, OSError):
        time.sleep(0.1)

if f is None:
    print("CONNECT_FAIL")
    sys.exit(1)

print("PY_CONNECTED")
count = 0
total = 0
t0 = time.time()
while True:
    hdr = f.read(4)
    if not hdr or len(hdr) < 4:
        break
    n = struct.unpack("<i", hdr)[0]
    data = b""
    while len(data) < n:
        chunk = f.read(n - len(data))
        if not chunk:
            break
        data += chunk
    if len(data) < n:
        break
    f.write(hdr)
    f.write(data)
    count += 1
    total += n
    print("ECHO n=%d" % n)

print("PY_DONE count=%d total_MB=%.2f elapsed_s=%.2f" % (count, total / 1024 / 1024, time.time() - t0))
f.close()
