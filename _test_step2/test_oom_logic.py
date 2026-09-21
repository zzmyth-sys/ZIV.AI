"""CPU-only verification of pipeline.run's OOM fallback control flow.

Stubs pipeline._run_once (no model, no GPU): the first candidate raises CUDA
OOM, the second must succeed. Verifies the candidate loop, exception handling
and fallback event without loading any weights.
"""

import os
import sys

SERVER = r"D:\devlop\ZIV.AI\python\server"
sys.path.insert(0, SERVER)

import torch  # noqa: E402

import pipeline  # noqa: E402

pipeline.config.MAX_RESOLUTION = 2048
pipeline.config.RESOLUTION_FALLBACK = [1024, 768, 640]
pipeline.config.FORCE_OOM = False
pipeline._oom_types = lambda: (torch.cuda.OutOfMemoryError,)

calls = []
events = []


def fake_run_once(*args, **kwargs):
    resolution = args[7]
    calls.append(resolution)
    if resolution == 2048:
        raise torch.cuda.OutOfMemoryError("simulated OOM at 2048")
    return {
        "output_path": "x.png", "seed": 42, "width": 1, "height": 1,
        "resolution": resolution, "duration_ms": 1,
    }


pipeline._run_once = fake_run_once
result = pipeline.run(
    None, None, None,
    {"prompt": "p", "image_path": "i.png", "steps": 1, "seed": 42, "denoise": 1.0},
    on_progress=lambda s, t, f, stage, message: events.append(message),
)

print("attempted candidates =", calls)
print("fallback events      =", [e for e in events if e and e.startswith("oom_fallback")])
print("final resolution     =", result["resolution"])
ok = calls == [2048, 1024] and result["resolution"] == 1024 and any(
    e and e.startswith("oom_fallback") for e in events
)
print("RESULT=%s" % ("OK" if ok else "FAIL"))
