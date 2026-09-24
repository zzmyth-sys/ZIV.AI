"""LoRA registry reader (Step 8-1).

Reads ``Template/loras.json`` — a data file mapping a LoRA id to a weight path and
default strengths. The C# side only carries the id in ``LoraOptions.Path``; this
module resolves it to a path. Pure CPU: no ``torch`` / ``comfy`` import, so it is
unit-testable without a GPU.

Adding a LoRA template is data-only: edit ``commands.json`` + ``loras.json``.
"""

import json
import logging
import os

import config

_LOG = logging.getLogger("zivai.server")


def load_registry(path=None):
    """Load the registry as ``{id: entry}``; missing / malformed file -> ``{}`` (never raises)."""
    registry_path = path or config.LORA_REGISTRY_PATH
    if not registry_path or not os.path.isfile(registry_path):
        _LOG.warning("LoRA registry not found: %s", registry_path)
        return {}

    try:
        with open(registry_path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, ValueError) as exc:
        _LOG.warning("LoRA registry unreadable (%s): %s", registry_path, exc)
        return {}

    entries = data.get("loras") if isinstance(data, dict) else None
    if not isinstance(entries, list):
        return {}

    registry = {}
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        lora_id = entry.get("id")
        if isinstance(lora_id, str) and lora_id.strip():
            registry[lora_id.strip()] = entry
    return registry


def resolve(id_or_path, registry=None):
    """Return the registry entry for a LoRA id, or ``None`` when the value is not a known id."""
    if not isinstance(id_or_path, str) or not id_or_path.strip():
        return None
    registry = load_registry() if registry is None else registry
    return registry.get(id_or_path.strip())


def resolve_strength(value, entry=None, default_key=None):
    """Resolve one LoRA strength (Step 8-2, semantic-inversion fix).

    An **explicit** value wins — including ``0`` (0 = fully suppress), which must not be
    mistaken for "unset". Only ``None`` (the key was absent) falls back to the registry's
    ``default_key`` and finally to ``1.0``. Never uses ``value or default`` (that would
    rewrite an explicit ``0``).
    """
    if value is not None:
        return float(value)
    if isinstance(entry, dict) and default_key and entry.get(default_key) is not None:
        return float(entry[default_key])
    return 1.0


def resolve_path(id_or_path, registry=None):
    """Resolve a LoRA id (or literal path) to a weight path.

    A registry hit returns its ``path``; otherwise the value is returned unchanged so a literal
    absolute path still works. Empty / whitespace -> ``None``.
    """
    if not isinstance(id_or_path, str) or not id_or_path.strip():
        return None
    value = id_or_path.strip()
    entry = resolve(value, registry)
    if entry and isinstance(entry.get("path"), str) and entry["path"].strip():
        return entry["path"].strip()
    return value
