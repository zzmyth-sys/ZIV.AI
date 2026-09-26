"""WD scene-tag A/B for /360 - stage 0b: build the A / B prompts (CPU only).

A (baseline): the plain 360 equirectangular instruction.
B (WD scene): A plus ``The scene is {wd_scene_tags}.`` where the tags are the
filtered scene list from ``360_ab_tags.py``.

Writes `_test_step2/360_ab_prompts.json`.
"""

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

A = (
    "Generate a complete 360-degree equirectangular panorama from the input "
    "perspective image. Use a true equirectangular projection covering the entire "
    "360-degree horizontal and 180-degree vertical field of view. Keep the scene "
    "continuous and seamless."
)

with open(os.path.join(HERE, "360_ab_tags.json"), encoding="utf-8") as handle:
    tags = json.load(handle)

scene = tags["scene"]
if len(scene) < 3:
    raise SystemExit("fewer than 3 scene tags; change the image first")

B = A + " The scene is " + ", ".join(scene) + "."

out = {
    "image": tags["image"],
    "scene_tags": scene,
    "A": A,
    "B": B,
}

with open(os.path.join(HERE, "360_ab_prompts.json"), "w", encoding="utf-8") as handle:
    json.dump(out, handle, ensure_ascii=False, indent=2)

print("scene_tags:", ", ".join(scene))
print()
print("A:", A)
print()
print("B:", B)
print()
print("wrote", os.path.join(HERE, "360_ab_prompts.json"))
