"""WD A/B round 3 - stage 2: contact sheet for A vs B (CPU only).

Builds one sheet: three role sources (person | outfit | scene) followed by A and B,
and reports MAD / PSNR / SSIM of A / B against the person source (resized), both
full-frame and central crop. Metrics are indicative only (background is replaced by
design); human review is the judge.

Writes `_test_step2/wd_ab3_sheet.png` and `wd_ab3_metrics.json`.
"""

import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_DIR = os.path.join(HERE, "wd_ab3_src")
OUT_H = 900
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


def main():
    key = sys.argv[1] if len(sys.argv) > 1 else "round3"
    with open(os.path.join(HERE, "wd_ab3_prompts.json"), encoding="utf-8") as handle:
        prompts = json.load(handle)[key]
    tag = prompts["tag"]

    font = ImageFont.truetype(FONT_PATH, 26)
    tiles = [
        ("PERSON (fig3)", Image.open(os.path.join(SRC_DIR, prompts["roles"]["person"]))),
        ("OUTFIT (fig1)", Image.open(os.path.join(SRC_DIR, prompts["roles"]["outfit"]))),
        ("SCENE (fig2)", Image.open(os.path.join(SRC_DIR, prompts["roles"]["scene"]))),
    ]

    first = Image.open(os.path.join(HERE, "%s_A.png" % tag)).convert("RGB")
    src_person = Image.open(os.path.join(SRC_DIR, prompts["roles"]["person"])).convert("RGB")
    person_small = src_person.resize((first.width, first.height), Image.LANCZOS)
    a_full, a_center = to_float(person_small), center_crop(to_float(person_small))

    report = {"size": [first.height, first.width]}
    for variant in ("A", "B"):
        im = Image.open(os.path.join(HERE, "%s_%s.png" % (tag, variant))).convert("RGB")
        tiles.append((variant, im))
        b_full = to_float(im)
        report[variant] = {
            "full": metrics(a_full, b_full),
            "center": metrics(a_center, center_crop(b_full)),
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
    sheet.save(os.path.join(HERE, "%s_sheet.png" % tag))
    print("wrote", os.path.join(HERE, "%s_sheet.png" % tag), sheet.size)

    with open(os.path.join(HERE, "%s_metrics.json" % tag), "w", encoding="utf-8") as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)

    print("%-3s | %-26s | %-26s" % ("var", "full MAD/PSNR/SSIM", "center MAD/PSNR/SSIM"))
    for variant in ("A", "B"):
        f, c = report[variant]["full"], report[variant]["center"]
        print("%-3s | %6.3f %6.2f %7.4f    | %6.3f %6.2f %7.4f" % (
            variant, f["mad"], f["psnr"], f["ssim"], c["mad"], c["psnr"], c["ssim"]))
    print("wrote", os.path.join(HERE, "%s_metrics.json" % tag))


if __name__ == "__main__":
    main()
