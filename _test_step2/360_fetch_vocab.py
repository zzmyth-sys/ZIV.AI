"""One-time dev tool: build the /360 scene/block vocabulary from the WD ecosystem.

Sources (no hand-authored per-image list):
  * Danbooru wiki tag groups (`tag_group:*`) - the mature booru grouping used by
    taggers/autocomplete; scene groups -> keep, person/body/attire/meta -> block.
  * The local WD14 model's own `selected_tags.csv` - its `category` column marks
    4 = character and 9 = rating; those names are blocked too.

Writes `_test_step2/360_scene_vocab.json`. This script is a build-time tool only;
the runtime filter (`scene_filter.py`) reads the JSON and never touches the net.
"""

import csv
import json
import os
import re
import time
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
CACHE_DIR = os.path.join(HERE, "danbooru_groups")
WD_CSV = os.path.join(
    r"D:\devlop\ZIV.AI\Comfyui\ComfyUI\custom_nodes\comfyui-wd14-tagger\models",
    "wd-vit-tagger-v3.csv",
)
API = "https://danbooru.donmai.us/wiki_pages/%s.json"
UA = "ZIV.AI-360-AB/0.1 (local experiment)"

_CACHE = None


def _load_cache():
    """Index the downloaded wiki JSONs by their own `title` (case-insensitive)."""
    global _CACHE
    if _CACHE is not None:
        return _CACHE
    _CACHE = {}
    if os.path.isdir(CACHE_DIR):
        for name in os.listdir(CACHE_DIR):
            if not name.endswith(".json"):
                continue
            try:
                with open(os.path.join(CACHE_DIR, name), encoding="utf-8") as handle:
                    data = json.load(handle)
            except (OSError, ValueError):
                continue
            title = str(data.get("title", "")).strip().lower().replace("_", " ")
            if title:
                _CACHE[title] = data
    return _CACHE

KEEP_GROUPS = [
    "Tag group:Locations",
    "Tag group:Real world locations",
    "Tag group:Backgrounds",
    "Tag group:Lighting",
    "Tag group:Doors and Gates",
    "Tag group:Water",
    "Tag group:Fire",
    "Tag group:Flowers",
]

BLOCK_GROUPS = [
    "Tag group:Body parts",
    "Tag group:Face tags",
    "Tag group:Eyes tags",
    "Tag group:Ears tags",
    "Tag group:Hair",
    "Tag group:Hair color",
    "Tag group:Hair styles",
    "Tag group:Posture",
    "Tag group:Hands",
    "Tag group:Gestures",
    "Tag group:Shoulders",
    "Tag group:Neck and neckwear",
    "Tag group:Attire",
    "Tag group:Accessories",
    "Tag group:Handwear",
    "Tag group:Headwear",
    "Tag group:Legwear",
    "Tag group:Sleeves",
    "Tag group:Eyewear",
    "Tag group:Makeup",
    "Tag group:Fashion style",
    "Tag group:Nudity",
    "Tag group:Sexual attire",
    "Tag group:Character count",
    "Tag group:Skin color",
    "Tag group:Image composition",
    "Tag group:Visual aesthetic",
    "Tag group:Theme",
    "Tag group:Subjective",
    "Tag group:Colors",
    "Tag group:Text",
    "Tag group:Symbols",
    "Tag group:Metatags",
]

LINK_RE = re.compile(r"\[\[([^\]]+)\]\]")


def fetch_group(title):
    cached = _load_cache().get(title.strip().lower().replace("_", " "))
    if cached is not None:
        return cached
    url = API % urllib.parse.quote(title, safe="")
    request = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def parse_tags(body):
    tags = set()
    for raw in LINK_RE.findall(body or ""):
        target = raw.split("|", 1)[0].strip().lower()
        if not target or target.startswith("tag group") or target.startswith("list of"):
            continue
        if " " in target and target.endswith(" tags"):
            continue
        tags.add(target)
    return tags


def collect(groups, label):
    out = set()
    for title in groups:
        body = fetch_group(title).get("body", "")
        tags = parse_tags(body)
        out |= tags
        print("  [%s] %-36s +%d (total %d)" % (label, title, len(tags), len(out)), flush=True)
        time.sleep(0.4)
    return out


def wd_blocked():
    blocked = set()
    with open(WD_CSV, newline="", encoding="utf-8-sig") as handle:
        for row in csv.DictReader(handle):
            if row.get("category") in ("4", "9"):
                blocked.add(row["name"].replace("_", " ").strip().lower())
    return blocked


def main():
    print("fetching keep groups...")
    keep = collect(KEEP_GROUPS, "keep")
    print("fetching block groups...")
    block = collect(BLOCK_GROUPS, "block")

    wd = wd_blocked()
    before = len(block)
    block |= wd
    print("  [block] WD csv category 4 (character) + 9 (rating): +%d (total %d)" % (
        len(block) - before, len(block)))

    # A tag in both lists is treated as scene (keep wins); report the overlap.
    overlap = sorted(keep & block)
    keep -= block

    out = {
        "_comment": (
            "Scene/block vocabulary for the /360 A/B, built from the Danbooru wiki "
            "tag groups (tag_group:*) plus the local WD14 wd-vit-tagger-v3.csv "
            "category 4 (character) / 9 (rating). Build-time tool: 360_fetch_vocab.py; "
            "runtime filter: scene_filter.py. Not tuned to any single image."
        ),
        "_sources": {
            "danbooru_wiki_api": "https://danbooru.donmai.us/wiki_pages/tag_group:*.json",
            "wd_csv": os.path.relpath(WD_CSV, HERE),
            "keep_groups": KEEP_GROUPS,
            "block_groups": BLOCK_GROUPS,
        },
        "_overlap_removed": overlap,
        "keep": sorted(keep),
        "block": sorted(block - keep),
    }
    with open(os.path.join(HERE, "360_scene_vocab.json"), "w", encoding="utf-8") as handle:
        json.dump(out, handle, ensure_ascii=False, indent=2)

    print()
    print("keep=%d block=%d overlap_removed=%d" % (
        len(out["keep"]), len(out["block"]), len(overlap)))
    probe = ["outdoors", "day", "tree", "building", "scenery", "real world location",
             "1girl", "solo", "standing", "long hair", "shoes", "white footwear", "wide shot"]
    for tag in probe:
        where = "keep" if tag in keep else ("block" if tag in block else "none")
        print("  %-22s %s" % (tag, where))
    print("wrote", os.path.join(HERE, "360_scene_vocab.json"))


if __name__ == "__main__":
    main()
