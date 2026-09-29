"""Sampling-parameter decision layer (Step 1: pure merge / decide, not wired yet).

CPU-only and stdlib-only: no ``torch`` / ``comfy`` / project-module import. A caller
supplies three sparse layers (model profile, request payload, plugin patch); :func:`resolve`
merges them low->high with provenance, then applies the ``sigmas`` terminator. The result is a
value object; this module never touches the pipeline, seams, plugin dispatch or the IPC
contract.

Coverage chain (low -> high, later wins): ``system_default`` -> ``model_profile`` ->
``data`` -> ``plugin``. Each layer contributes a **sparse** dict: a key it does not set is
``None`` / absent and never fills a lower layer's value.
"""

from dataclasses import dataclass

# Recognized sampling keys (order is only for stable iteration; merge is per key).
KNOWN_KEYS = (
    "steps",
    "cfg",
    "sampler_name",
    "scheduler",
    "shift",
    "sampler_type",
    "skip_shift",
    "sigmas",
    "denoise",
)

# Keys a plugin ``before_sample`` patch may carry (after the seam whitelist). ``model`` /
# ``cleanup`` / ``cleanups`` are intentionally not sampling knobs and are ignored here.
_PLUGIN_KEYS = ("skip_shift", "sigmas", "sampler_name", "scheduler", "cfg", "steps")

# Built-in lowest layer. Written here on purpose (no ``config`` import); Step 4 owns the
# config.py alignment to steps = 25.
_SYSTEM_DEFAULT = {
    "steps": 25,
    "cfg": 1.0,
    "sampler_name": "euler",
    "scheduler": "simple",
    "shift": 3.1,
    "sampler_type": "auraflow",
}

# Representative ``scheduler`` once ``sigmas`` governs the schedule (``sample_custom``
# takes no scheduler). ``sampler_type`` keeps its merged value (A-2).
_SIGMAS_WINS = "sigmas"


@dataclass(frozen=True)
class Resolved:
    """Merged + post-processed sampling knobs. ``provenance`` / ``conflicts`` are plain dicts
    (frozen only blocks field reassignment, not inner mutation — see the report)."""

    steps: int
    cfg: float
    sampler_name: str
    scheduler: str
    shift: float | None
    sampler_type: str
    sigmas: object | None
    terminated: bool
    provenance: dict
    conflicts: dict


def _layer_model_profile(model_entry):
    """Sparse layer from a raw ``models.json`` entry (the caller passes it un-resolved)."""
    if not isinstance(model_entry, dict):
        return {}
    sampler = model_entry.get("sampler")
    if not isinstance(sampler, dict):
        return {}
    layer = {}
    if sampler.get("type") is not None:
        layer["sampler_type"] = sampler["type"]
    for key in ("shift", "sampler_name", "scheduler", "cfg", "steps"):
        if sampler.get(key) is not None:
            layer[key] = sampler[key]
    return layer


def _layer_data(payload):
    """Sparse layer from the request payload (only recognized sampling knobs)."""
    if not isinstance(payload, dict):
        return {}
    return {key: payload[key] for key in KNOWN_KEYS if payload.get(key) is not None}


def _layer_plugin(plugin_patch):
    """Sparse layer from the whitelisted plugin patch (sampling keys only)."""
    if not isinstance(plugin_patch, dict):
        return {}
    return {key: plugin_patch[key] for key in _PLUGIN_KEYS if plugin_patch.get(key) is not None}


def _legal_sigmas(value):
    """Duck-typed 1-D + non-empty test (no ``torch`` import).

    Accepts any object exposing a 1-tuple ``shape``, or ``ndim == 1`` with ``len()``, or a
    plain ``list`` / ``tuple`` of non-sequences.
    """
    if value is None:
        return False
    shape = getattr(value, "shape", None)
    if shape is not None:
        try:
            if len(shape) != 1:
                return False
            return int(shape[0]) > 0
        except (TypeError, ValueError):
            return False
    ndim = getattr(value, "ndim", None)
    if isinstance(ndim, int):
        if ndim != 1:
            return False
        try:
            return len(value) > 0
        except TypeError:
            return False
    if isinstance(value, (list, tuple)):
        if len(value) == 0:
            return False
        return not any(isinstance(item, (list, tuple)) for item in value)
    return False


def _sigma_len(value):
    try:
        return int(len(value))
    except TypeError:
        pass
    shape = getattr(value, "shape", None)
    try:
        return int(shape[0])
    except (TypeError, IndexError, ValueError):
        return 0


def _coerce_int(value, fallback):
    """Integer step count with the legacy ``max(1, ...)`` floor (steps only)."""
    try:
        return max(1, int(value))
    except (TypeError, ValueError):
        return max(1, int(fallback))


def _coerce_float(value, fallback):
    try:
        return float(value)
    except (TypeError, ValueError):
        return fallback


def resolve(*, model_entry=None, payload=None, plugin_patch=None):
    """Merge the three sparse layers (low -> high) and apply the ``sigmas`` terminator.

    Pure: the inputs are only read. Unknown keys are ignored; a ``None`` value means "this
    layer did not set it" and never overrides a lower layer.
    """
    layers = (
        ("system_default", dict(_SYSTEM_DEFAULT)),
        ("model_profile", _layer_model_profile(model_entry)),
        ("data", _layer_data(payload)),
        ("plugin", _layer_plugin(plugin_patch)),
    )

    merged = {}
    declared = {}
    for name, layer in layers:
        for key, value in layer.items():
            if key not in KNOWN_KEYS or value is None:
                continue
            merged[key] = value
            declared.setdefault(key, []).append(name)

    provenance = {key: names[-1] for key, names in declared.items()}
    # The system_default baseline is not an override, so it never counts as a conflict
    # (spec example: model_profile + plugin on cfg -> ["model_profile", "plugin"]).
    conflicts = {}
    for key, names in declared.items():
        overrides = [name for name in names if name != "system_default"]
        if len(overrides) >= 2:
            conflicts[key] = overrides

    plugin_layer = layers[3][1]
    raw_plugin = plugin_patch if isinstance(plugin_patch, dict) else {}
    sigmas = merged.get("sigmas")
    sigmas_legal = _legal_sigmas(sigmas)

    if sigmas_legal:
        if "steps" in plugin_layer:
            steps = _coerce_int(plugin_layer["steps"], _sigma_len(sigmas))
        else:
            steps = _coerce_int(_sigma_len(sigmas), _SYSTEM_DEFAULT["steps"])
        scheduler = _SIGMAS_WINS
    else:
        steps = _coerce_int(merged.get("steps"), _SYSTEM_DEFAULT["steps"])
        scheduler = merged.get("scheduler") or _SYSTEM_DEFAULT["scheduler"]

    sampler_type = merged.get("sampler_type") or _SYSTEM_DEFAULT["sampler_type"]
    # Replicates seams._apply_before_sample's legacy skip_shift gate verbatim:
    # an explicit plugin value wins; otherwise shift is skipped when there is no
    # sigmas and applied when sigmas is present (the pre-S3 ``applies_shift``).
    if "skip_shift" in raw_plugin:
        skip_shift = bool(raw_plugin["skip_shift"])
    else:
        skip_shift = not sigmas_legal
    shift = None
    if not skip_shift and sampler_type == "auraflow":
        shift = merged.get("shift")
        if shift is None:
            shift = _SYSTEM_DEFAULT["shift"]

    terminated = sigmas_legal
    if terminated and "denoise" in declared:
        provenance["denoise"] = "%s(ineffective_under_sigmas)" % provenance["denoise"]

    return Resolved(
        steps=steps,
        cfg=_coerce_float(merged.get("cfg"), _SYSTEM_DEFAULT["cfg"]),
        sampler_name=merged.get("sampler_name") or _SYSTEM_DEFAULT["sampler_name"],
        scheduler=scheduler,
        shift=shift,
        sampler_type=sampler_type,
        sigmas=sigmas,
        terminated=terminated,
        provenance=provenance,
        conflicts=conflicts,
    )
