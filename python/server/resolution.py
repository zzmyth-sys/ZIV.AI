"""Resolution-policy helpers for the ZIV.AI backend (split out of ``pipeline`` under Z8).

Pure Python: turns a normalized ``submit.payload.resolution`` spec into concrete
target sizes and clamps against ``max_pixels``. No ``torch`` / ``comfy`` import, so
the math stays cheap and the OOM fallback ladder is computed before any heavy work.
"""

import logging
import math

import config

_LOG = logging.getLogger("zivai.server")


def _target_size(width, height, value, mode):
    """Map an input size to (target_width, target_height) for a resolution mode.

    ``side`` keeps the aspect ratio and makes the long edge equal ``value``;
    ``area`` keeps the aspect ratio and makes the pixel area equal ``value**2``
    (the Step 4 formula). Both snap to a multiple of 32 (min 32).
    """
    ratio = width / height
    if mode == "side":
        if ratio >= 1:
            target_w, target_h = value, value / ratio
        else:
            target_w, target_h = value * ratio, value
    else:
        target_w = (value * value * ratio) ** 0.5
        target_h = (value * value / ratio) ** 0.5
    width = max(32, round(target_w / 32) * 32)
    height = max(32, round(target_h / 32) * 32)
    return width, height


def _resolution_candidates(mode=None):
    if mode is None:
        mode = config.RESOLUTION_MODE
    if mode == "side":
        upper = int(config.RESOLUTION_SIDE)
        values = [upper] + [int(v) for v in config.RESOLUTION_SIDE_FALLBACK]
    else:
        upper = int(config.MAX_RESOLUTION)
        values = [upper] + [int(v) for v in config.RESOLUTION_FALLBACK]
    seen = set()
    ordered = []
    for value in values:
        if value > 0 and value <= upper and value not in seen:
            seen.add(value)
            ordered.append(value)
    return ordered or [upper]


def _resolution_specs(request, image_path):
    """Normalized resolution candidates for one run (Step 6.5).

    A payload ``resolution`` wins and yields a single spec; otherwise the config
    default path yields its OOM fallback ladder (backward compatible).
    """
    spec = _normalize_payload_resolution(request.get("resolution"), image_path)
    if spec is not None:
        return [spec]
    mode = config.RESOLUTION_MODE
    return [{"mode": mode, "value": int(v)} for v in _resolution_candidates(mode)]


def _normalize_payload_resolution(payload, image_path):
    """Turn ``submit.payload.resolution`` into a spec, or None to use the default.

    Supported modes: ``side`` / ``area`` / ``scale`` (input long edge × scale) /
    ``explicit`` (width × height). Values above ``max_pixels`` are clamped.
    """
    if not isinstance(payload, dict):
        return None

    mode = str(payload.get("mode") or "").strip().lower()
    max_pixels = _positive_int(payload.get("max_pixels"))

    if mode == "side":
        side = _positive_int(payload.get("side"))
        if side is None:
            return None
        return {"mode": "side", "value": _clamp_side(side, max_pixels)}

    if mode == "area":
        area = _positive_int(payload.get("area"))
        if area is None:
            return None
        return {"mode": "area", "value": _clamp_area(area, max_pixels)}

    if mode == "scale":
        try:
            scale = float(payload.get("scale"))
        except (TypeError, ValueError):
            return None
        if scale <= 0:
            return None
        source_side = _source_long_edge(image_path)
        if source_side is None:
            _LOG.warning("resolution scale requested but input size is unknown; using default")
            return None
        side = _clamp_side(int(round(source_side * scale)), max_pixels)
        _LOG.info("resolution scale=%.3f on source long edge %d -> side=%d", scale, source_side, side)
        return {"mode": "side", "value": side}

    if mode == "explicit":
        width = _positive_int(payload.get("width"))
        height = _positive_int(payload.get("height"))
        if width is None or height is None:
            return None
        width, height = _clamp_explicit(width, height, max_pixels)
        return {"mode": "explicit", "width": width, "height": height}

    if mode:
        _LOG.warning("unknown resolution mode %r; using config default", mode)
    return None


def _positive_int(value):
    try:
        number = int(value)
    except (TypeError, ValueError):
        return None
    return number if number > 0 else None


def _clamp_side(side, max_pixels):
    side = max(32, int(side))
    if max_pixels:
        limit = max(32, int(math.isqrt(int(max_pixels))))
        if side > limit:
            _LOG.warning(
                "resolution side %d exceeds max_pixels %d; clamped to %d",
                side, max_pixels, limit,
            )
            side = limit
    return side


def _clamp_area(area, max_pixels):
    area = max(32 * 32, int(area))
    if max_pixels and area > max_pixels:
        _LOG.warning("resolution area %d exceeds max_pixels %d; clamped", area, max_pixels)
        area = int(max_pixels)
    return area


def _clamp_explicit(width, height, max_pixels):
    width = max(32, int(width))
    height = max(32, int(height))
    if max_pixels and width * height > max_pixels:
        factor = (float(max_pixels) / float(width * height)) ** 0.5
        clamped_w = max(32, int(width * factor))
        clamped_h = max(32, int(height * factor))
        _LOG.warning(
            "resolution %dx%d exceeds max_pixels %d; clamped to %dx%d",
            width, height, max_pixels, clamped_w, clamped_h,
        )
        width, height = clamped_w, clamped_h
    return width, height


def _source_long_edge(path):
    if not path:
        return None
    try:
        from PIL import Image

        with Image.open(path) as image:
            return max(image.size)
    except Exception:
        return None


def _snap16(value):
    return max(32, int(value) // 16 * 16)


def _target_size_from_spec(width, height, spec):
    """Map a source size to a target size for a normalized resolution spec."""
    mode = spec.get("mode")
    if mode == "explicit":
        return _snap16(spec.get("width")), _snap16(spec.get("height"))
    value = int(spec.get("value"))
    return _target_size(width, height, value, "area" if mode == "area" else "side")


def _size_for_no_source(spec):
    """Target (width, height) when there is no source image (t2i)."""
    mode = spec.get("mode")
    if mode == "explicit":
        return _snap16(spec.get("width")), _snap16(spec.get("height"))
    value = int(spec.get("value"))
    if mode == "area":
        side = max(32, round((value ** 0.5) / 32) * 32)
    else:
        side = max(32, round(value / 32) * 32)
    return side, side


def _spec_label(spec):
    mode = spec.get("mode")
    if mode == "explicit":
        return "%dx%d" % (int(spec.get("width")), int(spec.get("height")))
    return "%s:%s" % (mode, spec.get("value"))
