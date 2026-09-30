"""Run the official-ComfyUI side of the rebuilt G1 same-load comparison.

Starts (or reuses) the official ComfyUI server, stages the G1 fixtures into its
``input/`` dir, submits the tracked G1 workflow (``g1_workflow.build_workflow``),
times the load / sampling segments from the server websocket, samples peak VRAM
and process RSS, hashes the output PNG and writes a JSON result whose fields
mirror ``tools/zivcli/runner.py`` (``sha256 / wall_sec / peak_vram_mib /
peak_rss_gb / exit_code / killed / output_path / input_env``), plus
``load_sec`` / ``wall_total_sec``.

Z30: GPU task.  Requires ``--yes`` (caller obtained explicit user consent) and a
pre-flight ``gpu-check`` (both enforced before any GPU work).

Stdlib only (plus the repo's ``tools.zivcli.gpu``); ``psutil`` is optional and
only used to sample the server process RSS.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import shutil
import socket
import struct
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid

from tools.zivcli import gpu
from tools.zivcli.official import g1_workflow

try:
    import psutil
except ImportError:  # RSS sampling degrades to None
    psutil = None

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))

HOME = r"D:\devlop\ZIV.AI"
PYTHON = os.path.join(HOME, "Comfyui", "python_embeded", "python.exe")
COMFY_DIR = os.path.join(HOME, "Comfyui", "ComfyUI")
MAIN = os.path.join(COMFY_DIR, "main.py")
INPUT_DIR = os.path.join(COMFY_DIR, "input")
OUTPUT_DIR = os.path.join(COMFY_DIR, "output")

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8188
DEFAULT_OUT_DIR = os.path.join(HERE, "runs", "g1")

FIXTURES = {
    "g1_img1.jpg": os.path.join(ROOT, "tools", "zivcli", "harness", "fixtures", "img1.jpg"),
    "g1_img2.png": os.path.join(ROOT, "tools", "zivcli", "harness", "fixtures", "img2.png"),
    "g1_img3.png": os.path.join(ROOT, "tools", "zivcli", "harness", "fixtures", "img3.png"),
}

DETACHED_PROCESS = 0x00000008
CREATE_NEW_PROCESS_GROUP = 0x00000200


def http_json(host, port, path, payload=None, timeout=30):
    url = "http://%s:%d%s" % (host, port, path)
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(url, data=data,
                                 headers={"content-type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        body = resp.read()
    return json.loads(body) if body else None


def server_up(host, port):
    try:
        http_json(host, port, "/system_stats", timeout=3)
        return True
    except (urllib.error.URLError, OSError, ValueError):
        return False


def ensure_server(host, port, out_dir, wait_s=300, start=True):
    """Return (pid_or_None, started). Reuse a reachable server; else start one."""
    if server_up(host, port):
        return None, False
    if not start:
        raise RuntimeError("official server not reachable at %s:%d" % (host, port))
    os.makedirs(out_dir, exist_ok=True)
    out = open(os.path.join(out_dir, "server.out.txt"), "ab")
    err = open(os.path.join(out_dir, "server.err.txt"), "ab")
    proc = subprocess.Popen(
        [PYTHON, "-s", MAIN, "--port", str(port)],
        cwd=COMFY_DIR, stdout=out, stderr=err, stdin=subprocess.DEVNULL,
        creationflags=DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP, close_fds=True,
    )
    with open(os.path.join(out_dir, "server.pid"), "w", encoding="utf-8") as fh:
        fh.write(str(proc.pid))
    deadline = time.time() + wait_s
    while time.time() < deadline:
        if server_up(host, port):
            return proc.pid, True
        if proc.poll() is not None:
            raise RuntimeError("official server exited early (code %s)" % proc.returncode)
        time.sleep(2)
    raise RuntimeError("official server did not become ready within %ds" % wait_s)


def stage_fixtures(input_dir, fixtures=FIXTURES):
    """Copy the G1 fixtures into ComfyUI/input (staging, not a repo copy)."""
    os.makedirs(input_dir, exist_ok=True)
    staged = {}
    for name, src in fixtures.items():
        if not os.path.isfile(src):
            raise FileNotFoundError(src)
        dst = os.path.join(input_dir, name)
        if not (os.path.isfile(dst) and os.path.getsize(dst) == os.path.getsize(src)):
            shutil.copyfile(src, dst)
        staged[name] = dst
    return staged


class WSClient:
    """Minimal dependency-free WebSocket client (server->client frames unmasked)."""

    def __init__(self, host, port, path, timeout=30):
        self.sock = socket.create_connection((host, port), timeout=timeout)
        key = base64.b64encode(os.urandom(16)).decode("ascii")
        req = ("GET %s HTTP/1.1\r\nHost: %s:%d\r\nUpgrade: websocket\r\n"
               "Connection: Upgrade\r\nSec-WebSocket-Key: %s\r\n"
               "Sec-WebSocket-Version: 13\r\n\r\n" % (path, host, port, key))
        self.sock.sendall(req.encode("ascii"))
        buf = b""
        while b"\r\n\r\n" not in buf:
            chunk = self.sock.recv(4096)
            if not chunk:
                raise ConnectionError("ws handshake closed")
            buf += chunk
        head = buf.split(b"\r\n\r\n", 1)[0].decode("latin1")
        if "101" not in head.split("\r\n")[0]:
            raise ConnectionError("ws handshake failed: " + head.split("\r\n")[0])
        self._rest = buf.split(b"\r\n\r\n", 1)[1]
        self.sock.settimeout(0.5)

    def _read_exact(self, n):
        buf, self._rest = self._rest, b""
        while len(buf) < n:
            chunk = self.sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("ws closed")
            buf += chunk
        self._rest = buf[n:]
        return buf[:n]

    def _send(self, opcode, payload):
        mask = os.urandom(4)
        n = len(payload)
        hdr = bytes([0x80 | opcode])
        if n < 126:
            hdr += bytes([0x80 | n])
        elif n < 65536:
            hdr += bytes([0x80 | 126]) + struct.pack(">H", n)
        else:
            hdr += bytes([0x80 | 127]) + struct.pack(">Q", n)
        masked = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
        self.sock.sendall(hdr + mask + masked)

    def recv(self):
        """Return (opcode, payload) or None on timeout."""
        try:
            hdr = self._read_exact(2)
        except socket.timeout:
            return None
        opcode = hdr[0] & 0x0F
        masked = hdr[1] & 0x80
        length = hdr[1] & 0x7F
        if length == 126:
            length = struct.unpack(">H", self._read_exact(2))[0]
        elif length == 127:
            length = struct.unpack(">Q", self._read_exact(8))[0]
        mask = self._read_exact(4) if masked else b""
        payload = self._read_exact(length) if length else b""
        if mask:
            payload = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
        if opcode == 0x9:  # ping
            self._send(0xA, payload)
            return None
        if opcode == 0x8:
            raise ConnectionError("ws close frame")
        return opcode, payload

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass


def extract_timing(events, sampler_ids, t_submit):
    """Split execution_start -> sampler -> success into load / sample / total."""
    ids = {str(i) for i in sampler_ids}

    def first(pred):
        for ts, name, data in events:
            if pred(name, data):
                return ts
        return None

    t_start = first(lambda n, d: n == "execution_start")
    t_start = t_submit if t_start is None else t_start
    t_sample_start = first(lambda n, d: n == "executing" and str(d.get("node")) in ids)
    if t_sample_start is None:
        t_sample_start = first(lambda n, d: n == "progress" and str(d.get("node")) in ids)
    if t_sample_start is None:
        t_sample_start = t_start
    t_sample_end = first(lambda n, d: n == "executed" and str(d.get("node")) in ids)
    t_end = first(lambda n, d: n in ("execution_success", "execution_error"))
    if t_end is None:
        t_end = events[-1][0] if events else t_submit
    if t_sample_end is None:
        t_sample_end = t_end
    return {
        "load_sec": round(t_sample_start - t_start, 3),
        "sample_sec": round(t_sample_end - t_sample_start, 3),
        "wall_total_sec": round(t_end - t_start, 3),
    }


def start_resource_sampler(pid, interval=0.5):
    """Poll GPU used memory + server RSS; return (stop_event, thread, peaks)."""
    peaks = {"vram": None, "rss": None}
    stop = threading.Event()
    proc = None
    if pid and psutil is not None:
        try:
            proc = psutil.Process(pid)
        except Exception:  # noqa: BLE001
            proc = None

    def loop():
        while not stop.is_set():
            try:
                v = gpu.query_used_mib()
                peaks["vram"] = v if peaks["vram"] is None else max(peaks["vram"], v)
            except Exception:  # noqa: BLE001
                pass
            if proc is not None:
                try:
                    rss = proc.memory_info().rss / float(1 << 30)
                    peaks["rss"] = rss if peaks["rss"] is None else max(peaks["rss"], rss)
                except Exception:  # noqa: BLE001
                    pass
            time.sleep(interval)

    thread = threading.Thread(target=loop, daemon=True)
    thread.start()
    return stop, thread, peaks


def resolve_output(history, prompt_id, comfy_dir=COMFY_DIR):
    entry = (history or {}).get(prompt_id) or {}
    for node_out in (entry.get("outputs") or {}).values():
        for im in node_out.get("images") or []:
            fn = im.get("filename")
            if not fn:
                continue
            base = os.path.join(comfy_dir, "output") if im.get("type", "output") == "output" \
                else os.path.join(comfy_dir, "temp")
            return os.path.join(base, im.get("subfolder") or "", fn)
    return None


def sha256_file(path):
    if not path or not os.path.isfile(path):
        return None
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def run_g1(viggle, *, out_dir=DEFAULT_OUT_DIR, host=DEFAULT_HOST, port=DEFAULT_PORT,
           prompt=g1_workflow.G1_PROMPT, start_server=True, timeout=900):
    verdict = gpu.check_gpu()
    if not verdict.get("free"):
        return 1, {"error": "gpu_busy", "gpu_check": verdict}

    os.makedirs(out_dir, exist_ok=True)
    pid, started = ensure_server(host, port, out_dir, start=start_server)
    stage_fixtures(INPUT_DIR)

    wf = g1_workflow.build_workflow(viggle, prompt=prompt)
    sampler_ids = g1_workflow.sampler_node_ids(viggle)

    client_id = uuid.uuid4().hex
    ws = WSClient(host, port, "/ws?clientId=" + client_id)
    try:
        submit = http_json(host, port, "/prompt",
                           {"prompt": wf, "client_id": client_id}, timeout=60) or {}
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", "replace")
        ws.close()
        return 1, {"error": "prompt_rejected", "status": exc.code, "body": body}
    if submit.get("node_errors"):
        ws.close()
        return 1, {"error": "node_errors", "node_errors": submit["node_errors"]}

    prompt_id = submit.get("prompt_id")
    t_submit = time.monotonic()
    stop, thread, peaks = start_resource_sampler(pid)
    events = []
    deadline = time.monotonic() + timeout
    last_probe = 0.0
    while time.monotonic() < deadline:
        got = ws.recv()
        if got is not None:
            opcode, payload = got
            if opcode == 1:
                try:
                    msg = json.loads(payload.decode("utf-8"))
                except ValueError:
                    continue
                events.append((time.monotonic(), msg.get("type"), msg.get("data") or {}))
                if msg.get("type") in ("execution_success", "execution_error"):
                    break
        now = time.monotonic()
        if now - last_probe >= 2.0:
            last_probe = now
            try:
                hist = http_json(host, port, "/history", timeout=10) or {}
            except (urllib.error.URLError, OSError, ValueError):
                hist = {}
            if prompt_id is None and hist:
                prompt_id = next(iter(hist))
            if prompt_id in hist:
                break
    stop.set()
    thread.join(timeout=2.0)
    ws.close()

    try:
        hist = http_json(host, port, "/history/" + (prompt_id or ""), timeout=20) or {}
    except (urllib.error.URLError, OSError, ValueError):
        hist = {}
    entry = hist.get(prompt_id) or {}
    status_str = ((entry.get("status") or {}).get("status_str"))
    out_path = resolve_output(hist, prompt_id)
    ok = status_str == "success" and out_path is not None
    timing = extract_timing(events, sampler_ids, t_submit)

    error_event = None
    for _ts, name, data in reversed(events):
        if name in ("execution_error", "execution_interrupted"):
            error_event = {k: data.get(k) for k in
                           ("node_type", "exception_type", "exception_message")}
            break

    result = {
        "scenario": "g1",
        "viggle": viggle,
        "sha256": sha256_file(out_path),
        "wall_sec": timing["sample_sec"],
        "load_sec": timing["load_sec"],
        "wall_total_sec": timing["wall_total_sec"],
        "peak_vram_mib": peaks["vram"],
        "peak_rss_gb": peaks["rss"],
        "exit_code": 0 if ok else 1,
        "killed": False,
        "output_path": out_path,
        "prompt_id": prompt_id,
        "status_str": status_str,
        "error": error_event,
        "input_env": {
            "op": "inpaint", "side": g1_workflow.REF_RESOLUTION,
            "output_w": g1_workflow.OUTPUT_W, "output_h": g1_workflow.OUTPUT_H,
            "nref": 2, "steps": g1_workflow.STEPS, "sampler": g1_workflow.SAMPLER,
            "scheduler": g1_workflow.SCHEDULER, "shift": g1_workflow.SHIFT,
            "cfg": g1_workflow.CFG, "seed": g1_workflow.SEED,
            "denoise": g1_workflow.DENOISE, "viggle": viggle,
            "prompt": prompt, "images": dict(g1_workflow.DEFAULT_IMAGES),
        },
        "server": {"host": host, "port": port, "pid": pid, "started": started},
    }
    path = os.path.join(out_dir, "g1_v%d.result.json" % viggle)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(result, fh, ensure_ascii=False, indent=2)
    return (0 if ok else 1), result


def main(argv=None):
    parser = argparse.ArgumentParser(prog="run_official",
                                     description="Official-ComfyUI G1 baseline runner")
    parser.add_argument("--viggle", type=int, default=0, choices=[0, 1])
    parser.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--prompt", default=g1_workflow.G1_PROMPT)
    parser.add_argument("--no-start", action="store_true",
                        help="do not start the server; require it already running")
    parser.add_argument("--yes", action="store_true",
                        help="caller has explicit user consent (Z30)")
    args = parser.parse_args(argv)

    if not args.yes:
        print("Z30: need explicit user consent (GPU task); add --yes", file=sys.stderr)
        return 2

    code, result = run_g1(args.viggle, out_dir=args.out_dir, host=args.host,
                          port=args.port, prompt=args.prompt,
                          start_server=not args.no_start)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return code


if __name__ == "__main__":
    sys.exit(main())
