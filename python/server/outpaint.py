"""Outpaint geometry (Step 7 Phase 2, Block D; aligned to the official workflow).

Pure CPU geometry: no ``comfy`` / ``torch`` import, so it can be unit-tested
without the heavy stack.

Mirrors ComfyUI's official ``Image Outpainting (Qwen-Image)`` blueprint
(``ComfyUI/blueprints/Image Outpainting (Qwen-Image).json``):

- ``ImagePadForOutpaint``: the canvas is filled with **grey 0.5** (not black) and
  the mask is 1 over the new region, ramping to 0 over ``feathering`` pixels into
  the source (squared ramp: ``v = (feathering - d) / feathering``, ``mask = v*v``).
- ``Grow and Blur Mask``: the mask is grown by ``grow`` px and blurred by
  ``blur`` px so the seam is soft.

The result is a grey canvas with the source pasted at the anchor plus a soft
mask (0..1) marking the region to regenerate.
"""

import os

import numpy as np
from PIL import Image, ImageFilter

ANCHORS = {
    "center",
    "left",
    "right",
    "top",
    "bottom",
    "top-left",
    "top-right",
    "bottom-left",
    "bottom-right",
}

CANVAS_NAME = "canvas.png"
MASK_NAME = "mask.png"

# Official blueprint defaults (ImagePadForOutpaint feathering + Grow/Blur Mask).
FEATHERING = 40
GROW = 20
BLUR = 31

# ImagePadForOutpaint fills the new region with 0.5 (grey), not black.
CANVAS_FILL = (128, 128, 128)


def snap16(value):
    return max(32, int(value) // 16 * 16)


def normalize_anchor(value):
    anchor = str(value or "").strip().lower()
    return anchor if anchor in ANCHORS else "center"


def anchor_position(anchor, target_w, target_h, src_w, src_h):
    """Top-left paste position for the source on the target canvas."""
    anchor = normalize_anchor(anchor)
    if "left" in anchor:
        x = 0
    elif "right" in anchor:
        x = target_w - src_w
    else:
        x = (target_w - src_w) // 2

    if "top" in anchor:
        y = 0
    elif "bottom" in anchor:
        y = target_h - src_h
    else:
        y = (target_h - src_h) // 2
    return x, y


def build_mask(target_w, target_h, x, y, src_w, src_h,
               feathering=FEATHERING, grow=GROW, blur=BLUR):
    """Soft regenerate mask (1 = new region, 0 = keep source), float [0, 1].

    Replicates ``ImagePadForOutpaint`` (squared ramp from each padded edge)
    followed by ``GrowMask`` (dilate) and ``ImageBlur``.
    """
    mask = np.ones((target_h, target_w), dtype=np.float32)
    patch = np.zeros((src_h, src_w), dtype=np.float32)

    top_pad = y > 0
    bottom_pad = (y + src_h) < target_h
    left_pad = x > 0
    right_pad = (x + src_w) < target_w

    if feathering > 0 and feathering * 2 < src_h and feathering * 2 < src_w:
        ii = np.arange(src_h, dtype=np.float32)[:, None]
        jj = np.arange(src_w, dtype=np.float32)[None, :]
        dt = ii if top_pad else np.full((src_h, 1), float(src_h), dtype=np.float32)
        db = (src_h - ii) if bottom_pad else np.full((src_h, 1), float(src_h), dtype=np.float32)
        dl = jj if left_pad else np.full((1, src_w), float(src_w), dtype=np.float32)
        dr = (src_w - jj) if right_pad else np.full((1, src_w), float(src_w), dtype=np.float32)
        d = np.minimum(np.minimum(dt, db), np.minimum(dl, dr))
        v = np.clip((feathering - d) / float(feathering), 0.0, 1.0)
        patch = v * v

    mask[y:y + src_h, x:x + src_w] = patch

    if grow > 0:
        mask = _dilate(mask, grow)
    if blur > 0:
        m8 = Image.fromarray((np.clip(mask, 0.0, 1.0) * 255.0).astype(np.uint8), "L")
        m8 = m8.filter(ImageFilter.GaussianBlur(blur))
        mask = np.asarray(m8).astype(np.float32) / 255.0

    return mask


def _dilate(array, radius):
    """Separable rectangular max-dilation (grows the 1-region by ``radius`` px)."""
    out = array.copy()
    for shift in range(1, radius + 1):
        out[:, shift:] = np.maximum(out[:, shift:], array[:, :-shift])
        out[:, :-shift] = np.maximum(out[:, :-shift], array[:, shift:])
    horizontal = out.copy()
    for shift in range(1, radius + 1):
        out[shift:, :] = np.maximum(out[shift:, :], horizontal[:-shift, :])
        out[:-shift, :] = np.maximum(out[:-shift, :], horizontal[shift:, :])
    return out


def build_outpaint(image_path, target_w, target_h, anchor, workdir):
    """Write ``canvas.png`` / ``mask.png`` into ``workdir``; return their paths."""
    target_w = int(target_w)
    target_h = int(target_h)

    source = Image.open(image_path).convert("RGB")
    src_w, src_h = source.size
    if src_w > target_w or src_h > target_h:
        scale = min(target_w / float(src_w), target_h / float(src_h))
        src_w = max(1, int(round(src_w * scale)))
        src_h = max(1, int(round(src_h * scale)))
        source = source.resize((src_w, src_h), Image.LANCZOS)

    canvas = Image.new("RGB", (target_w, target_h), CANVAS_FILL)
    x, y = anchor_position(anchor, target_w, target_h, src_w, src_h)
    canvas.paste(source, (x, y))

    mask = build_mask(target_w, target_h, x, y, src_w, src_h)
    mask_image = Image.fromarray((np.clip(mask, 0.0, 1.0) * 255.0).astype(np.uint8), "L")

    os.makedirs(workdir, exist_ok=True)
    canvas_path = os.path.join(workdir, CANVAS_NAME)
    mask_path = os.path.join(workdir, MASK_NAME)
    canvas.save(canvas_path)
    mask_image.save(mask_path)
    return canvas_path, mask_path
