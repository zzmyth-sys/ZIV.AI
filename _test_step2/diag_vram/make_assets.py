"""CPU-only scenario assets for the diag_vram matrix (PIL / numpy).

Creates, under ``_test_step2/diag_vram/assets/``:
  src_1536.png        main source image (1536, RGB)
  mask_1536.png       inpaint mask, white = central 1/3 square (L)
  ref2/3/4_1024.png   three distinct reference images (1024, RGB)

Usage: python make_assets.py
"""

import os

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "assets")

MAIN = 1536
REF = 1024


def gradient(size, seed):
    xs = np.arange(size, dtype=np.float32)[None, :]
    ys = np.arange(size, dtype=np.float32)[:, None]
    red = np.broadcast_to((xs * 7 + seed) % 256, (size, size))
    green = np.broadcast_to((ys * 5 + seed * 3) % 256, (size, size))
    blue = ((xs + ys) * 3 + seed * 11) % 256
    array = np.stack([red, green, blue], axis=-1).astype(np.uint8)
    return Image.fromarray(array, "RGB")


def main():
    os.makedirs(ASSETS, exist_ok=True)

    gradient(MAIN, 13).save(os.path.join(ASSETS, "src_1536.png"))

    mask = Image.new("L", (MAIN, MAIN), 0)
    third = MAIN // 3
    draw = ImageDraw.Draw(mask)
    draw.rectangle([third, third, 2 * third - 1, 2 * third - 1], fill=255)
    mask.save(os.path.join(ASSETS, "mask_1536.png"))

    colors = [(200, 60, 60), (60, 200, 90), (70, 90, 220)]
    for index, color in enumerate(colors, start=2):
        ref = Image.new("RGB", (REF, REF), color)
        pen = ImageDraw.Draw(ref)
        pen.ellipse([REF // 5, REF // 5, REF * 4 // 5, REF * 4 // 5], fill=(255, 255, 255))
        ref.save(os.path.join(ASSETS, "ref%d_%d.png" % (index, REF)))

    print("assets written to", ASSETS)


if __name__ == "__main__":
    main()
