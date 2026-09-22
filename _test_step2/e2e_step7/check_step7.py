"""Step 7 E2E PNG inspector (CPU only).

Usage: python check_step7.py <png> [<png> ...]

Reports PNG mode/size, alpha statistics for RGBA, and a coarse noise heuristic
(blockRatio / lag1) for the RGB content.
"""

import sys

import numpy as np
from PIL import Image


def inspect(path):
    img = Image.open(path)
    print("%s: mode=%s size=%dx%d" % (path, img.mode, img.size[0], img.size[1]))

    if img.mode == "RGBA":
        alpha = np.array(img.getchannel("A"))
        total = alpha.size
        transparent = int((alpha < 128).sum())
        opaque = int((alpha >= 128).sum())
        print(
            "  ALPHA: min=%d max=%d mean=%.1f  transparent(<128)=%.1f%%  opaque(>=128)=%.1f%%"
            % (alpha.min(), alpha.max(), alpha.mean(), 100.0 * transparent / total, 100.0 * opaque / total)
        )
        print(
            "  RGBA verdict: has_alpha=%s partial_alpha=%s"
            % (alpha.min() < 255 or alpha.max() > 0, 0 < alpha.min() and alpha.max() < 255)
        )
    else:
        print("  ALPHA: none (mode is %s, not RGBA)" % img.mode)

    rgb = img.convert("RGB")
    arr = np.asarray(rgb).astype(np.float64) / 255.0
    luma = 0.299 * arr[..., 0] + 0.587 * arr[..., 1] + 0.114 * arr[..., 2]
    mean = luma.mean()
    var = ((luma - mean) ** 2).mean()
    h, w = luma.shape
    by, bx = h // 8, w // 8
    if by and bx and var > 0:
        blocks = luma[: by * 8, : bx * 8].reshape(by, 8, bx, 8).mean(axis=(1, 3))
        block_ratio = ((blocks - mean) ** 2).mean() / var
        lag1 = ((luma[:, :-1] - mean) * (luma[:, 1:] - mean)).mean() / var
    else:
        block_ratio = lag1 = 0.0
    print(
        "  RGB: var=%.4f blockRatio=%.3f lag1=%.3f likely_noise=%s"
        % (var, block_ratio, lag1, (block_ratio < 0.80 or lag1 < 0.90))
    )


if __name__ == "__main__":
    for arg in sys.argv[1:]:
        try:
            inspect(arg)
        except Exception as exc:  # noqa: BLE001 - CLI reporter
            print("%s: ERROR %s" % (arg, exc))
