"""WD scene-tag A/B for /360 - stage 0: tag the input image + filter (CPU only).

Tags the user-supplied perspective image with WD14 (threshold 0.5), then extracts
scene tags with the deterministic, ecosystem-sourced filter in ``scene_filter.py``
(Danbooru tag groups + WD14 categories; no LLM, no per-image list). The scene list
is what variant B appends; it must contain at least 3 tags or the run aborts.

Writes `_test_step2/360_ab_tags.json`. Never touches the GPU (Z29 / Z30).
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(REPO, "python", "server"))
sys.path.insert(0, HERE)

import tagger  # noqa: E402
import scene_filter  # noqa: E402

SRC = os.path.join(HERE, "WD", "360.jpg")
THRESHOLD = 0.5
MIN_SCENE = 3

keep, block = scene_filter.load_vocab()
raw = tagger.tag_image(SRC, threshold=THRESHOLD)
report = scene_filter.filter_scene(raw, keep, block)
scene = report["scene"]

out = {
    "image": os.path.relpath(SRC, HERE),
    "threshold": THRESHOLD,
    "vocab": {
        "keep": len(keep),
        "block": len(block),
        "source": "360_scene_vocab.json (Danbooru tag groups + WD14 categories)",
    },
    "raw": raw,
    "clean": report["clean"],
    "whitelist": report["whitelist"],
    "fallback": report["fallback"],
    "used_fallback": report["used_fallback"],
    "dropped": report["dropped"],
    "scene": scene,
}

with open(os.path.join(HERE, "360_ab_tags.json"), "w", encoding="utf-8") as handle:
    json.dump(out, handle, ensure_ascii=False, indent=2)

print("image    :", out["image"])
print("threshold:", THRESHOLD)
print("vocab    : keep=%d block=%d (%s)" % (len(keep), len(block), out["vocab"]["source"]))
print("raw (%d)  : %s" % (len(report["clean"]), ", ".join(report["clean"])))
print("whitelist : %s" % ", ".join(report["whitelist"]))
print("fallback  : %s%s" % (
    ", ".join(report["fallback"]) if report["fallback"] else "(none)",
    "  <- USED" if report["used_fallback"] else ""))
print("dropped   : %s" % (", ".join(report["dropped"]) if report["dropped"] else "(none)"))
print("scene(%d) : %s" % (len(scene), ", ".join(scene)))
print("wrote", os.path.join(HERE, "360_ab_tags.json"))

if len(scene) < MIN_SCENE:
    print("ERROR: fewer than %d scene tags after filtering -> change the image." % MIN_SCENE)
    sys.exit(2)
