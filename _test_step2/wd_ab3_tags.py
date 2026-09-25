"""WD A/B round 3 - stage 0: tag the three role images (CPU only).

Roles (hard-coded for this round):
  outfit  <- ab3_outfit_1.jpg  (figure 1: hanfu woman, clothes source)
  scene   <- ab3_scene_2.jpg   (figure 2: night pagoda, background source)
  person  <- ab3_person_3.jpg  (figure 3: white-clad woman, subject source)

Threshold 0.5 (per round-3 spec), character threshold left at the config default.
Rating tags are never emitted by the product tagger, so removing them is implicit;
duplicates are dropped and underscores are already converted to spaces.

Writes `_test_step2/wd_ab3_tags.json` with both raw and cleaned lists.
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(REPO, "python", "server"))

import tagger  # noqa: E402

SRC = os.path.join(HERE, "wd_ab3_src")
THRESHOLD = 0.5
IMAGES = {
    "outfit": "ab3_outfit_1.jpg",
    "scene": "ab3_scene_2.jpg",
    "person": "ab3_person_3.jpg",
}


def clean(tags):
    seen = []
    for tag in tags:
        tag = tag.strip()
        if tag and tag not in seen:
            seen.append(tag)
    return seen


out = {}
for role, name in IMAGES.items():
    tags = tagger.tag_image(os.path.join(SRC, name), threshold=THRESHOLD)
    cleaned = clean(tags)
    out[role] = {"image": name, "raw": tags, "clean": cleaned}
    print("== %s (%s): %d raw / %d clean" % (role, name, len(tags), len(cleaned)))
    print("   ", ", ".join(cleaned))
    print()

with open(os.path.join(HERE, "wd_ab3_tags.json"), "w", encoding="utf-8") as handle:
    json.dump(out, handle, ensure_ascii=False, indent=2)
print("wrote", os.path.join(HERE, "wd_ab3_tags.json"))
