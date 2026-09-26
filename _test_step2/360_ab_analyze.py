"""WD scene-tag A/B for /360 - stage 2: contact sheet + objective checks (CPU).

Builds one sheet (input | A | B) for human review and reports, for each output:

  size          - must be 1920x1080 (16:9)
  seam_lr       - mean |left column - right column| (equirect wrap; lower = more seamless)
  seam_tb       - mean |top row - bottom row| (pole wrap; indicative only)
  full/center   - MAD / PSNR / SSIM against the input resized to the output size
                  (indicative only: a projection change is expected to differ)

Writes `360_ab_sheet.png` and `360_ab_metrics.json`.
"""

import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "WD", "360.jpg")
OUT_H = 720
GAP = 10
BAR = 44
FONT_PATH = r"C:\Windows\Fonts\arial.ttf"


def to_float(im):
    return np.asarray(im.convert("RGB")).astype(np.float64) / 255.0


def gray(a):
    return a @ np.array([0.299, 0.587, 0.114])


def ssim(a, b):
    from scipy.ndimage import uniform_filter

    win = 11
    c1, c2 = 0.01 ** 2, 0.03 ** 2
    ga, gb = gray(a), gray(b)
    mu_a, mu_b = uniform_filter(ga, win), uniform_filter(gb, win)
    va = uniform_filter(ga * ga, win) - mu_a * mu_a
    vb = uniform_filter(gb * gb, win) - mu_b * mu_b
    cab = uniform_filter(ga * gb, win) - mu_a * mu_b
    return float(np.mean(((2 * mu_a * mu_b + c1) * (2 * cab + c2)) /
                         ((mu_a ** 2 + mu_b ** 2 + c1) * (va + vb + c2))))


def metrics(a, b):
    mse = float(np.mean((a - b) ** 2))
    return {
        "mad": round(float(np.mean(np.abs(a - b))), 4),
        "psnr": round(10 * math.log10(1.0 / mse), 2) if mse > 1e-12 else float("inf"),
        "ssim": round(ssim(a, b), 4),
    }


def center_crop(a, frac=0.5):
    h, w = a.shape[:2]
    ch, cw = int(h * frac), int(w * frac)
    return a[(h - ch) // 2:(h - ch) // 2 + ch, (w - cw) // 2:(w - cw) // 2 + cw]


def edge_diff(arr):
    """Mean abs 0-255 difference between the first/last columns and rows."""
    lr = float(np.mean(np.abs(arr[:, 0].astype(np.int16) - arr[:, -1].astype(np.int16))))
    tb = float(np.mean(np.abs(arr[0, :].astype(np.int16) - arr[-1, :].astype(np.int16))))
    return round(lr, 3), round(tb, 3)


def main():
    font = ImageFont.truetype(FONT_PATH, 26)
    src = Image.open(SRC).convert("RGB")

    tiles = [("INPUT", src)]
    report = {"source": os.path.relpath(SRC, HERE)}
    first = Image.open(os.path.join(HERE, "360_ab_A.png")).convert("RGB")
    src_resized = src.resize((first.width, first.height), Image.LANCZOS)
    ref_full = to_float(src_resized)
    ref_center = center_crop(ref_full)

    for variant in ("A", "B"):
        im = Image.open(os.path.join(HERE, "360_ab_%s.png" % variant)).convert("RGB")
        tiles.append((variant, im))
        arr = np.asarray(im)
        lr, tb = edge_diff(arr)
        f = to_float(im)
        report[variant] = {
            "size": [im.width, im.height],
            "ratio": round(im.width / im.height, 4),
            "is_16to9": im.width == 1920 and im.height == 1080,
            "seam_lr": lr,
            "seam_tb": tb,
            "full": metrics(ref_full, f),
            "center": metrics(ref_center, center_crop(f)),
        }

    scaled = []
    for label, im in tiles:
        w = max(1, round(im.width * OUT_H / im.height))
        scaled.append((label, im.resize((w, OUT_H), Image.LANCZOS)))
    total_w = sum(im.width for _, im in scaled) + GAP * (len(scaled) - 1)
    sheet = Image.new("RGB", (total_w, OUT_H + BAR), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    x = 0
    for label, im in scaled:
        sheet.paste(im, (x, BAR))
        draw.text((x + 10, 9), label, fill=(240, 240, 240), font=font)
        x += im.width + GAP
    sheet.save(os.path.join(HERE, "360_ab_sheet.png"))
    print("wrote", os.path.join(HERE, "360_ab_sheet.png"), sheet.size)

    with open(os.path.join(HERE, "360_ab_metrics.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)

    for variant in ("A", "B"):
        r = report[variant]
        print("%-3s | %dx%d 16:9=%s | seam_lr=%.3f seam_tb=%.3f | full MAD/PSNR/SSIM %6.3f %6.2f %7.4f | center %6.3f %6.2f %7.4f" % (
            variant, r["size"][0], r["size"][1], r["is_16to9"], r["seam_lr"], r["seam_tb"],
            r["full"]["mad"], r["full"]["psnr"], r["full"]["ssim"],
            r["center"]["mad"], r["center"]["psnr"], r["center"]["ssim"]))
    print("wrote", os.path.join(HERE, "360_ab_metrics.json"))


if __name__ == "__main__":
    main()
