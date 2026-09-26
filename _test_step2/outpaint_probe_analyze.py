"""Analyze a /扩图 probe pair: the exported outpaint mask and the crop canvas.

Prints structural facts only (no judgement of "effect"):
  * mask / canvas size (must match the crop canvas),
  * mask value range / mean / fraction >= 128 (soft ramp present?),
  * how much of the canvas's gray region (128 +/- 3) is covered by mask>0.5
    (1.0 => the mask geometry covers the whole gray band; <1.0 => it misses part).

Usage: python outpaint_probe_analyze.py <mask.png> <canvas.png>
CPU only; no comfy / GPU.
"""

import sys

import numpy as np
from PIL import Image


def load(label, path):
    try:
        arr = np.asarray(Image.open(path))
    except Exception as exc:  # noqa: BLE001
        print("%-6s MISSING/unreadable: %s (%s)" % (label, path, exc))
        return None
    print("%-6s path=%s shape=%s dtype=%s" % (label, path, arr.shape, arr.dtype))
    return arr


def gray(arr):
    return arr[..., 0] if arr.ndim == 3 else arr


def main():
    if len(sys.argv) < 3:
        print("usage: outpaint_probe_analyze.py <mask.png> <canvas.png>")
        return

    mask = load("MASK", sys.argv[1])
    canvas = load("CANVAS", sys.argv[2])
    if mask is None or canvas is None:
        return

    g = gray(mask).astype(np.int32)
    scale = 255 if g.max() > 1 else 1
    frac = g / float(scale)
    print("  mask  min/max/mean = %d / %d / %.1f  frac>=0.5 = %.3f  (soft=%s)"
          % (g.min(), g.max(), g.mean(), (g >= 0.5 * scale).mean(),
             "yes" if (g.min() > 0 and g.max() < scale) or len(np.unique(g)) > 2 else "hard?"))

    if mask.shape[:2] != canvas.shape[:2]:
        print("  SIZE MISMATCH mask=%s canvas=%s" % (mask.shape[:2], canvas.shape[:2]))
        return

    # The crop-outpaint padding is exactly mid-gray (128,128,128) opaque; match it exactly so
    # source pixels that merely look gray are not miscounted as padding.
    if canvas.ndim == 3 and canvas.shape[2] >= 3:
        grey = ((canvas[..., 0] == 128) & (canvas[..., 1] == 128)
                & (canvas[..., 2] == 128))
        if canvas.shape[2] == 4:
            grey &= canvas[..., 3] == 255
    else:
        grey = np.abs(gray(canvas).astype(np.int32) - 128) <= 0
    m = frac >= 0.5
    covered = int((grey & m).sum())
    total = int(grey.sum())
    print("  canvas grey px = %.3f of frame; mask>0.5 = %.3f of frame" % (grey.mean(), m.mean()))
    print("  grey covered by mask>0.5 = %d/%d = %.3f" % (covered, total, covered / max(1, total)))
    print("  hint: covered==1.000 -> mask covers the whole gray band; <1.000 -> geometry misses grey")


if __name__ == "__main__":
    main()
