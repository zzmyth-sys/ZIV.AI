"""Deterministic scene-tag extraction for the /360 A/B (no LLM, image-independent).

Given raw WD14 tags, ``filter_scene`` applies two ecosystem-sourced lists
(``360_scene_vocab.json``, built by ``360_fetch_vocab.py``):

  1. whitelist (keep) - Danbooru scene groups (locations / backgrounds / lighting
                        / water / fire / doors / flowers / real world locations)
  2. block           - Danbooru person/body/attire/meta groups + the WD14 model's
                        own character (category 4) and rating (category 9) names
  3. scene           - the whitelist hits when they reach ``min_scene``; otherwise
                        the whitelist hits plus the block-filtered remainder.

Whitelist-first is deliberate: a tag that no ecosystem group covers (e.g. a WD
general tag like ``white footwear``) is dropped rather than leaked into the scene
list, while the fallback keeps recall when a scene has few whitelisted tags. No
human or LLM pass is involved; the lists are data, not per-image curation.
"""

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_VOCAB = os.path.join(HERE, "360_scene_vocab.json")


def load_vocab(path=None):
    with open(path or DEFAULT_VOCAB, encoding="utf-8") as handle:
        data = json.load(handle)
    keep = [t.strip().lower() for t in data.get("keep", []) if t.strip()]
    block = [t.strip().lower() for t in data.get("block", []) if t.strip()]
    return keep, block


def dedupe(tags):
    seen = []
    for tag in tags:
        tag = (tag or "").strip()
        if tag and tag not in seen:
            seen.append(tag)
    return seen


def filter_scene(tags, keep, block, min_scene=3):
    """Return a report dict with clean / whitelist / fallback / scene / dropped."""
    clean = dedupe(tags)
    keep_set, block_set = set(keep), set(block)

    whitelist = [t for t in clean if t in keep_set]
    fallback = [t for t in clean if t not in keep_set and t not in block_set]
    scene = list(whitelist) if len(whitelist) >= min_scene else whitelist + fallback
    scene = [t for t in scene if t in keep_set or t not in block_set]
    dropped = [t for t in clean if t not in scene]

    return {
        "clean": clean,
        "whitelist": whitelist,
        "fallback": fallback,
        "scene": scene,
        "dropped": dropped,
        "used_fallback": len(whitelist) < min_scene,
    }
