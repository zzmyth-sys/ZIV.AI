"""Unit tests for the soft/hard-aware mask resize (P1 · /扩图 edge fix).

``_encode`` resamples the mask to the latent's pixel size. A soft (0..255) mask — the
crop-outpaint mask or a feathered hand mask — must keep its ramp (bilinear, matching the
official VAEEncodeForInpaint); a hard 0/1 mask stays nearest (Z19 unchanged).

CPU only (no comfy / torch.cuda): pure tensor math.
"""

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import torch  # noqa: E402

import pipeline  # noqa: E402


class ResizeMaskTests(unittest.TestCase):
    def test_mask_is_binary(self):
        hard = torch.zeros((1, 4, 4))
        hard[0, :, :2] = 1.0
        self.assertTrue(pipeline._mask_is_binary(hard))
        self.assertFalse(pipeline._mask_is_binary(hard + 0.01))

    def test_soft_mask_bilinear_keeps_the_ramp(self):
        # A horizontal 0..1 ramp downsampled 8 -> 4 keeps intermediate values.
        ramp = torch.linspace(0.0, 1.0, 8).view(1, 1, 8).repeat(1, 4, 1)
        out = pipeline._resize_mask(ramp, 4, 4, mode="bilinear")
        self.assertEqual((1, 4, 4), tuple(out.shape))
        values = {round(float(v), 3) for v in out.flatten()}
        self.assertGreater(len(values), 2, "bilinear must keep a gradient, not 0/1 steps")

    def test_hard_mask_nearest_stays_binary(self):
        hard = torch.zeros((1, 4, 4))
        hard[0, :, :2] = 1.0
        out = pipeline._resize_mask(hard, 2, 2, mode="nearest")
        self.assertLessEqual({float(v) for v in out.flatten()}, {0.0, 1.0})

    def test_default_mode_is_nearest(self):
        hard = torch.zeros((1, 4, 4))
        hard[0, :2, :] = 1.0
        out = pipeline._resize_mask(hard, 2, 2)
        self.assertLessEqual({float(v) for v in out.flatten()}, {0.0, 1.0})


if __name__ == "__main__":
    unittest.main()
