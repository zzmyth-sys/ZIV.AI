"""WD A/B experiment - stage 3: contact sheets + drift metrics (CPU only).

For every batch, builds a side-by-side sheet (source | A1 | A2 | B | C) and reports
MAD / PSNR / SSIM of each variant against the (resized) source image, both full-frame
and central-subject crop.
"""

import json
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_DIR = os.path.join(HERE, "wd_ab_src")
OUT_H = 900
GAP = 10
BAR = 44
FONT_PATH = r"C:\Windows\Fonts\arial.ttf"

VARIANTS = {
    "batch1": ["A1", "A2", "B", "C"],
    "batch1b": ["A1", "A2", "B"],
    "batch2": ["A1", "B"],
}


def load_rgb(path):
    return Image.open(path).convert("RGB")


def to_float(im):
    return np.asarray(im).astype(np.float64) / 255.0


def gray(a):
    return a @ np.array([0.299, 0.587, 0.114])


def ssim(a, b):
    from scipy.ndimage import uniform_filter

    win = 11
    c1 = 0.01 ** 2
    c2 = 0.03 ** 2
    ga, gb = gray(a), gray(b)
    mu_a = uniform_filter(ga, win)
    mu_b = uniform_filter(gb, win)
    va = uniform_filter(ga * ga, win) - mu_a * mu_a
    vb = uniform_filter(gb * gb, win) - mu_b * mu_b
    cab = uniform_filter(ga * gb, win) - mu_a * mu_b
    num = (2 * mu_a * mu_b + c1) * (2 * cab + c2)
    den = (mu_a ** 2 + mu_b ** 2 + c1) * (va + vb + c2)
    return float(np.mean(num / den))


def metrics(a, b):
    mse = float(np.mean((a - b) ** 2))
    psnr = 10 * math.log10(1.0 / mse) if mse > 1e-12 else float("inf")
    return {
        "mad": round(float(np.mean(np.abs(a - b))), 4),
        "psnr": round(psnr, 2),
        "ssim": round(ssim(a, b), 4),
    }


def center_crop(a, frac=0.5):
    h, w = a.shape[:2]
    ch, cw = int(h * frac), int(w * frac)
    y0, x0 = (h - ch) // 2, (w - cw) // 2
    return a[y0:y0 + ch, x0:x0 + cw]


def main():
    with open(os.path.join(HERE, "wd_ab_prompts.json"), encoding="utf-8") as handle:
        prompts = json.load(handle)

    font = ImageFont.truetype(FONT_PATH, 26)
    report = {}

    for batch, variants in VARIANTS.items():
        src_path = os.path.join(SRC_DIR, prompts[batch]["subject_image"])
        src = load_rgb(src_path)
        tiles = [("SOURCE", src)]

        first = load_rgb(os.path.join(HERE, "wd_ab_%s_%s.png" % (batch, variants[0])))
        target_hw = (first.height, first.width)
        src_small = src.resize((first.width, first.height), Image.LANCZOS)

        entry = {"size": list(target_hw)}
        a_full = to_float(src_small)
        a_center = center_crop(a_full)
        for variant in variants:
            im = load_rgb(os.path.join(HERE, "wd_ab_%s_%s.png" % (batch, variant)))
            b_full = to_float(im)
            entry[variant] = {
                "full": metrics(a_full, b_full),
                "center": metrics(a_center, center_crop(b_full)),
            }
            tiles.append((variant, im))
        report[batch] = entry

        height = OUT_H
        scaled = []
        for label, im in tiles:
            w = max(1, round(im.width * height / im.height))
            scaled.append((label, im.resize((w, height), Image.LANCZOS)))
        total_w = sum(im.width for _, im in scaled) + GAP * (len(scaled) - 1)
        sheet = Image.new("RGB", (total_w, height + BAR), (24, 24, 24))
        draw = ImageDraw.Draw(sheet)
        x = 0
        for label, im in scaled:
            sheet.paste(im, (x, BAR))
            draw.text((x + 10, 9), label, fill=(240, 240, 240), font=font)
            x += im.width + GAP
        sheet_path = os.path.join(HERE, "wd_ab_sheet_%s.png" % batch)
        sheet.save(sheet_path)
        print("wrote", sheet_path, sheet.size)

    with open(os.path.join(HERE, "wd_ab_metrics.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)

    print()
    print("%-8s %-4s | %-28s | %-28s" % ("batch", "var", "full MAD/PSNR/SSIM", "center MAD/PSNR/SSIM"))
    for batch, entry in report.items():
        for variant, data in entry.items():
            if variant == "size":
                continue
            f, c = data["full"], data["center"]
            print("%-8s %-4s | %6.3f %6.2f %7.4f        | %6.3f %6.2f %7.4f" % (
                batch, variant, f["mad"], f["psnr"], f["ssim"], c["mad"], c["psnr"], c["ssim"]))
    print()
    print("wrote", os.path.join(HERE, "wd_ab_metrics.json"))


if __name__ == "__main__":
    main()
