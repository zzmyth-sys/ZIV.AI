"""CPU-only analysis for the TE-Speed A/B experiment (no GPU, no torch).

Compares te_speed_A.png (baseline) vs te_speed_B.png (TE-Speed) numerically and
writes a side-by-side contact sheet. Mirrors mask_feather_analyze.py /
r6_compare.py style.

  * MAD (mean absolute difference, 0-255) A vs B, A vs source, B vs source
  * PSNR (A vs B)
  * SSIM (A vs B, grayscale, box window k=7) -- approximate, no skimage needed
  * abs-diff percentiles (where the predictor diverges)
  * speedup summary from te_speed_ab_result.json (if present)

Usage (embedded python):
  & "D:\\devlop\\ZIV.AI\\Comfyui\\python_embeded\\python.exe" ^
      "D:\\devlop\\ZIV.AI\\_test_step2\\te_speed_analyze.py"
Optional: --a <path> --b <path> --src <path> --sheet <path>
"""

import argparse
import json
import math
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))


def load_rgb(path, size):
    return np.asarray(
        Image.open(path).convert("RGB").resize(size, Image.LANCZOS)
    ).astype(np.float32)


def gray(arr):
    return arr @ np.array([0.299, 0.587, 0.114], dtype=np.float32)


def box_blur(x, k=7):
    ker = np.ones(k, dtype=np.float64) / float(k)
    y = np.apply_along_axis(lambda m: np.convolve(m, ker, mode="same"), 0, x)
    y = np.apply_along_axis(lambda m: np.convolve(m, ker, mode="same"), 1, y)
    return y


def ssim(a, b, k=7):
    a = a.astype(np.float64)
    b = b.astype(np.float64)
    c1 = (0.01 * 255.0) ** 2
    c2 = (0.03 * 255.0) ** 2
    mu_a = box_blur(a, k)
    mu_b = box_blur(b, k)
    va = box_blur(a * a, k) - mu_a * mu_a
    vb = box_blur(b * b, k) - mu_b * mu_b
    cov = box_blur(a * b, k) - mu_a * mu_b
    num = (2.0 * mu_a * mu_b + c1) * (2.0 * cov + c2)
    den = (mu_a ** 2 + mu_b ** 2 + c1) * (va + vb + c2)
    return float((num / den).mean())


def psnr(a, b):
    mse = float(np.mean((a.astype(np.float64) - b.astype(np.float64)) ** 2))
    if mse <= 0:
        return float("inf")
    return 10.0 * math.log10((255.0 ** 2) / mse)


def speedup_from_json(path):
    if not os.path.isfile(path):
        return
    with open(path, "r", encoding="utf-8") as handle:
        data = json.load(handle)
    runs = data.get("runs", [])
    a = [r["wall_s"] for r in runs if r.get("ok") and r.get("tag", "").startswith("A")]
    b = [r["wall_s"] for r in runs if r.get("ok") and r.get("tag", "").startswith("B")]
    print("-- timing (from %s) --" % os.path.basename(path))
    if a:
        print("  A wall_s: %s  mean=%.2f" % ([round(x, 2) for x in a], sum(a) / len(a)))
    if b:
        print("  B wall_s: %s  mean=%.2f" % ([round(x, 2) for x in b], sum(b) / len(b)))
    if a and b:
        ma, mb = sum(a) / len(a), sum(b) / len(b)
        print("  speedup = (A-B)/A = %.1f%%  (A=%.2fs B=%.2fs)" % (
            100.0 * (ma - mb) / ma, ma, mb))
    a_peak = [r.get("smi_peak_MiB") for r in runs if r.get("ok") and r.get("tag", "").startswith("A")]
    b_peak = [r.get("smi_peak_MiB") for r in runs if r.get("ok") and r.get("tag", "").startswith("B")]
    if a_peak and b_peak:
        print("  smi peak MiB: A=%s B=%s" % (a_peak, b_peak))
    st = data.get("probe_status")
    if st:
        print("  TE-Speed status: %s" % st)
    print()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--a", default=os.path.join(HERE, "te_speed_A.png"))
    ap.add_argument("--b", default=os.path.join(HERE, "te_speed_B.png"))
    ap.add_argument("--src", default=os.path.join(HERE, "user_input.png"))
    ap.add_argument("--sheet", default=os.path.join(HERE, "te_speed_sheet.png"))
    args = ap.parse_args()

    speedup_from_json(os.path.join(HERE, "te_speed_ab_result.json"))

    if not (os.path.isfile(args.a) and os.path.isfile(args.b)):
        print("missing A or B output (run te_speed_ab.py first):")
        print("  A:", args.a, os.path.isfile(args.a))
        print("  B:", args.b, os.path.isfile(args.b))
        return

    with Image.open(args.a) as im:
        size = im.size  # (w, h)
    a = load_rgb(args.a, size)
    b = load_rgb(args.b, size)
    src = load_rgb(args.src, size)

    ab = np.abs(a - b)
    print("size=%s" % (size,))
    print()
    print("-- MAD (mean abs diff, 0-255) --")
    print("  A vs B    : all=%.2f" % float(ab.mean()))
    print("  A vs src  : all=%.2f" % float(np.abs(a - src).mean()))
    print("  B vs src  : all=%.2f" % float(np.abs(b - src).mean()))
    print()
    print("-- A vs B difference distribution --")
    for p in (50, 75, 90, 95, 99, 99.9):
        print("  p%-5s = %.2f" % (p, float(np.percentile(ab, p))))
    print("  max    = %.2f" % float(ab.max()))
    print()
    print("-- A vs B similarity --")
    print("  PSNR = %.2f dB" % psnr(a, b))
    print("  SSIM (gray, box k=7) = %.4f" % ssim(gray(a), gray(b)))
    print()
    print("Hints: speedup >=30%% + acceptable PSNR/SSIM -> default on; big quality drop -> keep off.")

    w, h = size
    sheet = Image.new("RGB", (w * 3, h), (0, 0, 0))
    for i, arr in enumerate([src, a, b]):
        sheet.paste(Image.fromarray(arr.astype(np.uint8)), (i * w, 0))
    half = (w * 3 // 2, h // 2)
    sheet.resize(half, Image.LANCZOS).save(args.sheet)
    print()
    print("wrote", os.path.abspath(args.sheet), "(source | A | B)")


if __name__ == "__main__":
    main()
