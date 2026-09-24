"""Model registry reader (Step 8-2).

Reads ``Template/models.json`` — data mapping a model id to weight paths, resolution
metadata and a sampler preset. The C# side reads the profile fields; Python reads the
paths + sampler block. Resolution priority is **environment > models.json > code
defaults** (``config``). Pure CPU at import time (no ``torch`` / ``comfy``), so it is
unit-testable off-GPU.

Adding a model is a data change: add one entry to ``models.json``.
"""

import json
import logging
import os

import config

_LOG = logging.getLogger("zivai.server")


def load_registry(path=None):
    """Load the registry as ``{id: entry}``; missing / malformed file -> ``{}`` (never raises)."""
    registry_path = path or config.MODELS_REGISTRY_PATH
    if not registry_path or not os.path.isfile(registry_path):
        _LOG.warning("models registry not found: %s", registry_path)
        return {}

    try:
        with open(registry_path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, ValueError) as exc:
        _LOG.warning("models registry unreadable (%s): %s", registry_path, exc)
        return {}

    entries = data.get("models") if isinstance(data, dict) else None
    if not isinstance(entries, list):
        return {}

    registry = {}
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        model_id = entry.get("id")
        if isinstance(model_id, str) and model_id.strip():
            registry[model_id.strip()] = entry
    return registry


def default_entry(registry=None):
    """The entry flagged ``default: true``, else the first one, else ``None``."""
    registry = load_registry() if registry is None else registry
    for entry in registry.values():
        if entry.get("default"):
            return entry
    return next(iter(registry.values()), None)


def resolve(model_id, registry=None):
    """Return the entry for a known id; ``None`` / unknown falls back to the default entry."""
    registry = load_registry() if registry is None else registry
    if isinstance(model_id, str) and model_id.strip() and model_id.strip() in registry:
        return registry[model_id.strip()]
    return default_entry(registry)


def _env(name):
    return os.environ.get(name, "").strip()


def _pick(entry, env_name, json_key, code_default):
    value = _env(env_name)
    if value:
        return value
    if isinstance(entry, dict):
        candidate = entry.get(json_key)
        if isinstance(candidate, str) and candidate.strip():
            return candidate.strip()
    return code_default


def resolve_paths(model_id, registry=None):
    """Resolve the weights triple for a model id (env > models.json > config default)."""
    entry = resolve(model_id, registry)
    entry_id = entry.get("id") if isinstance(entry, dict) else None
    display = entry.get("display_name") if isinstance(entry, dict) else None
    return {
        "id": entry_id or config.MODEL_NAME,
        "display_name": display or config.MODEL_NAME,
        "dit_path": _pick(entry, "ZIV_AI_DIT_PATH", "dit_path", config.DIT_MODEL_PATH),
        "te_path": _pick(entry, "ZIV_AI_TE_PATH", "te_path", config.TEXT_ENCODER_PATH),
        "vae_path": _pick(entry, "ZIV_AI_VAE_PATH", "vae_path", config.VAE_PATH),
    }


def _env_float(name, fallback):
    value = _env(name)
    if not value:
        return fallback
    try:
        return float(value)
    except ValueError:
        return fallback


def resolve_sampler(model_id, registry=None):
    """Resolve the sampler preset for a model id (env > models.json > config default)."""
    entry = resolve(model_id, registry)
    sampler = entry.get("sampler") if isinstance(entry, dict) else None
    if not isinstance(sampler, dict):
        sampler = {}

    def json_value(key, code_default):
        value = sampler.get(key)
        return value if value is not None else code_default

    return {
        "type": sampler.get("type", "auraflow"),
        "shift": _env_float("ZIV_AI_AURAFLOW_SHIFT", json_value("shift", config.AURAFLOW_SHIFT)),
        "sampler_name": _env("ZIV_AI_SAMPLER") or json_value("sampler_name", config.SAMPLER_NAME),
        "scheduler": _env("ZIV_AI_SCHEDULER") or json_value("scheduler", config.SCHEDULER_NAME),
        "cfg": _env_float("ZIV_AI_CFG", json_value("cfg", 1.0)),
    }
