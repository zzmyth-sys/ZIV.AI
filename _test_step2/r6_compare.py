"""CPU-only R6 verification for the mask-edit pipeline (no GPU, no torch).

Group A (feather = 0, binary mask) vs the source image: pixels OUTSIDE the mask
should barely change (local edit) -> covers ACCEPTANCE 9C.7.16.
Group B (feather = 10, soft mask) vs group A: the seam discontinuity ratio should
be <= A (softer seam) -> covers ACCEPTANCE 9C.7B.16.

Method mirrors _test_step2/mask_feather_analyze.py: mean absolute difference in
0-255, computed inside the hard-mask core, outside it, and over the whole image;
plus a seam-discontinuity ratio (top-edge vertical gradient / internal reference
gradient) as used by mask_feather_result.md.

Usage (any CPU python with numpy + Pillow; the embedded one also works):

  python r6_compare.py --original user_input.png --mask hard_mask.png \
      --a outA.png [--b outB.png] [--sheet r6_sheet.png]

Notes:
  * <hard_mask.png> = the exported mask for group A. It is resized to the output
    size with NEAREST (mask coordinates are the pipeline image's pixels).
  * The original is resized to the output size with LANCZOS (the backend resizes
    the source to its target resolution before editing).
"""

import argparse
import os

import numpy as np
from PIL import Image


def load_rgb(path, size):
    return np.asarray(Image.open(path).convert("RGB").resize(size, Image.LANCZOS)).astype(np.float32)


def load_gray(path, size):
    return np.asarray(Image.open(path).convert("L").resize(size, Image.NEAREST)).astype(np.float32)


def mad(a, b, region=None):
    """Mean absolute difference (0-255), optionally restricted to a boolean region."""
    d = np.abs(a - b)
    if region is not None:
        d = d[region]
    return float(d.mean()) if d.size else 0.0


def seam_ratio(img, hard, outside=50, core=200):
    """Top-edge vertical gradient / internal reference gradient.

    A hard (binary) cope has a sharp step at the mask's top edge; a feathered mask
    spreads it out, so the ratio drops. Returns None when the mask has no usable
    top edge.
    """
    rows = np.where((hard > outside).any(axis=1))[0]
    if rows.size == 0:
        return None

    y_top = int(rows[0])
    cols = np.where(hard[y_top] > outside)[0]
    if cols.size < 8 or y_top < 1 or y_top + 2 >= img.shape[0]:
        return None

    seam = np.abs(img[y_top + 1, cols] - img[y_top - 1, cols]).mean()

    ref0 = min(img.shape[0] - 2, y_top + 40)
    ref1 = min(img.shape[0] - 1, y_top + 60)
    if ref1 - ref0 < 2:
        return None

    ref_block = img[ref0:ref1, cols]
    ref = np.abs(ref_block[1:] - ref_block[:-1]).mean()
    if ref <= 1e-6:
        return None

    return float(seam / ref)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--original", required=True, help="source image (pre-edit)")
    ap.add_argument("--mask", required=True, help="group-A hard mask PNG")
    ap.add_argument("--a", required=True, help="group-A output (feather = 0)")
    ap.add_argument("--b", default=None, help="group-B output (feather > 0)")
    ap.add_argument("--sheet", default=None, help="optional contact-sheet path")
    args = ap.parse_args()

    with Image.open(args.a) as im:
        size = im.size  # (w, h)

    src = load_rgb(args.original, size)
    mask = load_gray(args.mask, size)
    a = load_rgb(args.a, size)
    b = load_rgb(args.b, size) if args.b else None

    core = mask > 200
    outside = mask < 50

    print("size=%s  core_px=%d  outside_px=%d" % (size, int(core.sum()), int(outside.sum())))
    print()
    print("-- Group A vs SOURCE (mean abs diff, 0-255) --")
    print("  A: core=%.2f  outside=%.2f  all=%.2f" % (mad(a, src, core), mad(a, src, outside), mad(a, src)))
    ratio_a = seam_ratio(a, mask)
    print("  A seam_ratio=%s" % ("n/a" if ratio_a is None else "%.3f" % ratio_a))

    ratio_b = None
    if b is not None:
        print()
        print("-- Group B (feather) vs SOURCE / A --")
        print("  B vs source: core=%.2f  outside=%.2f  all=%.2f" % (mad(b, src, core), mad(b, src, outside), mad(b, src)))
        print("  B vs A     : core=%.2f  outside=%.2f  all=%.2f" % (mad(b, a, core), mad(b, a, outside), mad(b, a)))
        ratio_b = seam_ratio(b, mask)
        print("  B seam_ratio=%s" % ("n/a" if ratio_b is None else "%.3f" % ratio_b))
        if ratio_a is not None and ratio_b is not None:
            print("  seam softer (B <= A): %s  (A=%.3f  B=%.3f)" % (ratio_b <= ratio_a, ratio_a, ratio_b))

    print()
    print("PASS hints:")
    print("  9C.7.16  -> A outside MAD small (VAE roundtrip ~<1.5) AND core clearly changed (>)")
    print("  9C.7B.16 -> B seam_ratio <= A seam_ratio (softer seam)")

    if args.sheet:
        w, h = size
        frames = [src, a] + ([b] if b is not None else [])
        sheet = Image.new("RGB", (w * len(frames), h), (0, 0, 0))
        for i, arr in enumerate(frames):
            sheet.paste(Image.fromarray(arr.astype(np.uint8)), (i * w, 0))
        sheet.save(args.sheet)
        print()
        print("wrote", os.path.abspath(args.sheet))


if __name__ == "__main__":
    main()
