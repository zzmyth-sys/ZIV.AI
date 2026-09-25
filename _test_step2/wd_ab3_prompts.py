"""WD A/B round 3 - stage 0b: build the A (short) / B (WD tags) prompts.

Emits two prompt sets:
  round3        -> tag "wd_ab3"   (no suffix, already run)
  round3_light  -> tag "wd_ab3b"  (A/B + a lighting/shadow blending suffix, re-run)

Role mapping is hard-coded but the *slot* order is a single switch so the round-3
prompt can be renumbered without editing the text bodies:

  IMAGE_ORDER = ["person", "outfit", "scene"]
      -> <image1>=person(fig3), <image2>=outfit(fig1), <image3>=scene(fig2)   [recommended]
  IMAGE_ORDER = ["outfit", "scene", "person"]
      -> <image1>=outfit(fig1), <image2>=scene(fig2), <image3>=person(fig3)   [literal round-3 sketch]

The main/first slot (image1) is what pipeline.run uses as the edit base, so the
recommended order puts the person first (subject drives output aspect + structure).

Because WD mis-tags the night scaffold as ``multiple boys`` / ``6+boys`` and mixes
subject tags into the outfit image, B filters per role (blacklists below) instead of
concatenating whole lists.

Writes `_test_step2/wd_ab3_prompts.json`.
"""

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))

# Slot order for the <imageN> markers. Flip to ["outfit", "scene", "person"] for the
# literal round-3 sketch. The first entry is the pipeline main image.
IMAGE_ORDER = ["person", "outfit", "scene"]

# Suffix for the lighting/shadow blending re-run.
LIGHTING = "Blend the lighting and shadows naturally between the person and the background."

QUALITY = {
    "realistic", "photorealistic", "blurry", "letterboxed", "logo", "watermark",
    "depth of field", "signature", "text", "english text", "chinese text",
}
SUBJECT_TAGS = {
    "1girl", "solo", "multiple boys", "6+boys", "people", "asian", "brown eyes",
    "looking at viewer", "smile", "upper body", "parted lips", "clothes writing",
    "black hair", "long hair",
}
SCENE_TAGS = {
    "outdoors", "night", "building", "city", "lamppost", "tree", "scenery",
    "real world location", "tokyo (city)",
}
OUTFIT_TAGS = {"hanfu", "chinese clothes", "long sleeves"}

# Tags kept per role after removing the tags that belong to the *other* roles.
ROLE_DROP = {
    "person": QUALITY | SCENE_TAGS | OUTFIT_TAGS,
    "outfit": QUALITY | SUBJECT_TAGS | SCENE_TAGS,
    "scene": QUALITY | SUBJECT_TAGS,
}

with open(os.path.join(HERE, "wd_ab3_tags.json"), encoding="utf-8") as handle:
    TAGS = json.load(handle)


def kept(role):
    return [t for t in TAGS[role]["clean"] if t not in ROLE_DROP[role]]


kept_tags = {role: kept(role) for role in TAGS}
slots = {role: index + 1 for index, role in enumerate(IMAGE_ORDER)}


def joined(role):
    return ", ".join(kept_tags[role])


A = (
    "Make the girl in <image%d> wear the clothes from <image%d> "
    "and place her in the scene from <image%d>, waving and smiling at the camera."
    % (slots["person"], slots["outfit"], slots["scene"])
)

B = (
    "Use <image%d> as %s. "
    "Dress her in <image%d>'s %s. "
    "Place her into <image%d>'s %s. "
    "She is waving and smiling at the camera."
    % (
        slots["person"], joined("person"),
        slots["outfit"], joined("outfit"),
        slots["scene"], joined("scene"),
    )
)


def pack(tag, suffix):
    return {
        "tag": tag,
        "image_order": IMAGE_ORDER,
        "slots": slots,
        "roles": {role: TAGS[role]["image"] for role in TAGS},
        "tags_clean": {role: TAGS[role]["clean"] for role in TAGS},
        "tags_kept": kept_tags,
        "A": (A + " " + suffix).strip(),
        "B": (B + " " + suffix).strip(),
        "subject_image": TAGS["person"]["image"],
        "additional": [TAGS[IMAGE_ORDER[1]]["image"], TAGS[IMAGE_ORDER[2]]["image"]],
    }


prompts = {
    "round3": pack("wd_ab3", ""),
    "round3_light": pack("wd_ab3b", LIGHTING),
}

with open(os.path.join(HERE, "wd_ab3_prompts.json"), "w", encoding="utf-8") as handle:
    json.dump(prompts, handle, ensure_ascii=False, indent=2)

print("image_order:", IMAGE_ORDER, "->", slots)
for role in ("person", "outfit", "scene"):
    print("  %-6s kept: %s" % (role, joined(role)))
for key, data in prompts.items():
    print()
    print("== %s (tag %s) ==" % (key, data["tag"]))
    print("A:", data["A"])
    print("B:", data["B"])
print()
print("subject_image:", prompts["round3_light"]["subject_image"])
print("additional   :", prompts["round3_light"]["additional"])
print()
print("wrote", os.path.join(HERE, "wd_ab3_prompts.json"))
