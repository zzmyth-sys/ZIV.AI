import sys
import numpy as np
from PIL import Image

path = sys.argv[1]
im = Image.open(path).convert("RGB")
a = np.asarray(im).astype(np.float64) / 255.0
luma = 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]
mean = luma.mean()
var = ((luma - mean) ** 2).mean()
h, w = luma.shape
by, bx = h // 8, w // 8
blocks = luma[: by * 8, : bx * 8].reshape(by, 8, bx, 8).mean(axis=(1, 3))
block_ratio = ((blocks - mean) ** 2).mean() / var
lag1 = ((luma[:, :-1] - mean) * (luma[:, 1:] - mean)).mean() / var
print("%s: size=%dx%d var=%.4f blockRatio=%.3f lag1=%.3f likely_noise=%s"
      % (path, w, h, var, block_ratio, lag1, (block_ratio < 0.80 or lag1 < 0.90)))
