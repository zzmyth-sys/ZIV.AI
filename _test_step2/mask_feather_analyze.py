"""CPU-only analysis for the mask-feather experiment (no GPU).

Compares the three edited outputs against each other and against the source, so
the seam effect of binary (A) vs gaussian sigma=10 (B) vs sigma=25 (C) can be
read numerically. Also writes a side-by-side strip + a seam crop for eyeballing.
"""

import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "user_input.png")
OUT = {k: os.path.join(HERE, "mask_feather_%s.png" % k) for k in ("A", "B", "C")}
MASK = {k: os.path.join(HERE, "mask_feather_mask_%s.png" % k) for k in ("A", "B", "C")}


def load_rgb(path, size):
    return np.asarray(Image.open(path).convert("RGB").resize(size, Image.LANCZOS)).astype(np.float32)


def load_gray(path, size):
    return np.asarray(Image.open(path).convert("L").resize(size, Image.NEAREST)).astype(np.float32)


def mad(a, b, region=None):
    d = np.abs(a - b)
    if region is not None:
        d = d[region]
    return float(d.mean())


def main():
    with Image.open(OUT["A"]) as im:
        size = im.size  # (w, h)
    src = load_rgb(SRC, size)
    outs = {k: load_rgb(OUT[k], size) for k in ("A", "B", "C")}
    masks = {k: load_gray(MASK[k], size) for k in ("A", "B", "C")}

    core = masks["A"] > 200          # inside the hard rectangle
    outside = masks["A"] < 50        # far outside
    seam = (masks["A"] >= 50) & (masks["A"] <= 200)  # only exists for B/C

    print("size=%s  core_px=%d  outside_px=%d" % (size, int(core.sum()), int(outside.sum())))
    print()
    print("-- output vs SOURCE (mean abs diff, 0-255) --")
    for k in ("A", "B", "C"):
        print("  %s: core=%.2f  outside=%.2f  all=%.2f" % (
            k, mad(outs[k], src, core), mad(outs[k], src, outside), mad(outs[k], src)))

    print()
    print("-- output vs output (mean abs diff) --")
    for x, y in (("A", "B"), ("B", "C"), ("A", "C")):
        print("  %s vs %s: all=%.2f  core=%.2f  outside=%.2f" % (
            x, y, mad(outs[x], outs[y]), mad(outs[x], outs[y], core), mad(outs[x], outs[y], outside)))

    print()
    print("-- seam band (mask 50..200, B/C only) vs A --")
    for k in ("B", "C"):
        band = (masks[k] >= 50) & (masks[k] <= 200)
        print("  %s seam_vs_A=%.2f  seam_px=%d" % (k, mad(outs[k], outs["A"], band), int(band.sum())))

    # Contact sheet: source | A | B | C
    w, h = size
    sheet = Image.new("RGB", (w * 4, h), (0, 0, 0))
    for i, img in enumerate([Image.fromarray(src.astype(np.uint8))] + [Image.fromarray(outs[k].astype(np.uint8)) for k in ("A", "B", "C")]):
        sheet.paste(img, (i * w, 0))
    sheet_path = os.path.join(HERE, "mask_feather_sheet.png")
    sheet.resize((w * 4 // 2, h // 2), Image.LANCZOS).save(sheet_path)
    print()
    print("wrote", sheet_path)

    # Seam zoom: top edge of the rectangle, horizontal strip around y0.
    y0 = h // 4
    band_y0, band_y1 = max(0, y0 - 40), min(h, y0 + 40)
    x0, x1 = w // 4 - 60, w // 4 + 200
    zoom = Image.new("RGB", ((x1 - x0) * 4, (band_y1 - band_y0) * 3), (0, 0, 0))
    for i, arr in enumerate([src] + [outs[k] for k in ("A", "B", "C")]):
        crop = Image.fromarray(arr[band_y0:band_y1, x0:x1].astype(np.uint8))
        crop = crop.resize((crop.width * 1, crop.height * 3), Image.NEAREST)
        zoom.paste(crop, (i * (x1 - x0), 0))
    zoom_path = os.path.join(HERE, "mask_feather_seam_zoom.png")
    zoom.save(zoom_path)
    print("wrote", zoom_path)


if __name__ == "__main__":
    main()
