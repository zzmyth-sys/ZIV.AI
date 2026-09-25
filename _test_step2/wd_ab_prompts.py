"""WD A/B experiment - stage 0b: build the A1 / A2 / C prompts.

A1 = all tags joined + edit instruction
A2 = quality/metadata tags filtered out, then joined + edit instruction
C  = edit instruction only (no tags)
B  = filled later by wd_ab_llm.py (LLM rewrite)

Writes `_test_step2/wd_ab_prompts.json`.
"""

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

EDIT1 = "Replace the background with a classical Chinese garden with white walls and dark tiled roofs."
EDIT2 = "Keep the person from <image1> unchanged. Use the scene from <image2> as the new background."

# Metadata / quality / provenance tags that describe the source rendering, not the scene.
QUALITY = {
    "realistic", "photorealistic", "blurry", "blurry background", "blurry foreground",
    "letterboxed", "logo", "copyright name", "watermark", "depth of field",
    "signature", "artist name", "web address", "username", "censored", "bar censor",
    "text", "english text", "chinese text", "speech bubble", "twitter username",
}

with open(os.path.join(HERE, "wd_ab_tags.json"), encoding="utf-8") as handle:
    TAGS = json.load(handle)


def cleaned(tags):
    return [t for t in tags if t not in QUALITY]


def build(subject, edit):
    tags = TAGS[subject]
    keep = cleaned(tags)
    return {
        "subject": subject,
        "edit": edit,
        "tags": tags,
        "tags_clean": keep,
        "dropped": [t for t in tags if t in QUALITY],
        "A1": ", ".join(tags) + ". " + edit,
        "A2": ", ".join(keep) + ". " + edit,
        "C": edit,
        "B": None,
    }


prompts = {
    "batch1": build("fig4_grass", EDIT1),
    "batch1b": build("fig1_stage", EDIT1),
    "batch2": build("fig4_grass", EDIT2),
}
prompts["batch1"]["subject_image"] = "fig4_grass.jpg"
prompts["batch1b"]["subject_image"] = "fig1_stage.jpg"
prompts["batch2"]["subject_image"] = "fig4_grass.jpg"
prompts["batch2"]["reference"] = "fig2_garden_v.png"

with open(os.path.join(HERE, "wd_ab_prompts.json"), "w", encoding="utf-8") as handle:
    json.dump(prompts, handle, ensure_ascii=False, indent=2)

for key, data in prompts.items():
    print("==", key, "| subject", data["subject_image"])
    if "reference" in data:
        print("   ref", data["reference"])
    print("   dropped:", data["dropped"])
    print("   A1:", data["A1"][:160])
    print("   A2:", data["A2"][:160])
    print("   A1 len=%d  A2 len=%d  C len=%d" % (
        len(data["A1"]), len(data["A2"]), len(data["C"])))
    print()

print("wrote", os.path.join(HERE, "wd_ab_prompts.json"))
