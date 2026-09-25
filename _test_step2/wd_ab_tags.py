"""WD A/B experiment - stage 0: tag the subject images (CPU only).

Runs the product tagger (`python/server/tagger.py`) on the two subject images and
writes the raw tag lists to `_test_step2/wd_ab_tags.json`.
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(REPO, "python", "server"))

import config  # noqa: E402
import tagger  # noqa: E402

SRC = os.path.join(HERE, "wd_ab_src")
SUBJECTS = {
    "fig4_grass": "fig4_grass.jpg",   # clean subject (batch 1 / batch 2)
    "fig1_stage": "fig1_stage.jpg",   # text-bearing subject (batch 1b)
}

out = {}
for key, name in SUBJECTS.items():
    tags = tagger.tag_image(os.path.join(SRC, name))
    out[key] = tags
    print("%s (%s): %d tags" % (key, name, len(tags)))
    print("  ", ", ".join(tags))
    print()

with open(os.path.join(HERE, "wd_ab_tags.json"), "w", encoding="utf-8") as handle:
    json.dump(out, handle, ensure_ascii=False, indent=2)
print("wrote", os.path.join(HERE, "wd_ab_tags.json"))
