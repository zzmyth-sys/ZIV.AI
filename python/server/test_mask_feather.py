"""CPU-only tests for the revised Z19 mask semantics (Step 9C.7-B).

Verifies :func:`pipeline._load_mask_tensor` keeps a grayscale ramp when
``binary=False`` (user / C# feathered masks) and thresholds to 0 / 1 when
``binary=True``. Uses a temp PNG; no GPU / ComfyUI sampling.

Run from ``python/server`` with::

    python -m unittest test_mask_feather
"""

import os
import shutil
import tempfile
import unittest

import numpy as np
from PIL import Image

import pipeline


def _write_ramp(path, width=16, height=8):
    """Write an 8-bit grayscale horizontal ramp (0 .. 255)."""
    array = np.zeros((height, width), dtype=np.uint8)
    for y in range(height):
        for x in range(width):
            array[y, x] = int(round(x / (width - 1) * 255.0))
    Image.fromarray(array, mode="L").save(path)


class LoadMaskTensorTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.mkdtemp(prefix="zivai_mask_feather_")
        self.path = os.path.join(self.directory, "ramp.png")
        _write_ramp(self.path)

    def tearDown(self):
        shutil.rmtree(self.directory, ignore_errors=True)

    def test_grayscale_keeps_the_soft_ramp(self):
        tensor = pipeline._load_mask_tensor(self.path, binary=False)
        values = tensor[0].numpy()

        # The full ramp reaches both ends and keeps intermediate values.
        self.assertAlmostEqual(float(values.max()), 1.0, places=2)
        self.assertAlmostEqual(float(values.min()), 0.0, places=2)
        self.assertTrue(((values > 0.0) & (values < 1.0)).any())

    def test_binary_thresholds_to_zero_one(self):
        tensor = pipeline._load_mask_tensor(self.path, binary=True)
        values = tensor[0].numpy()

        unique = set(np.unique(values).tolist())
        self.assertTrue(unique.issubset({0.0, 1.0}), unique)
        # Half the ramp is below 0.5, half at/above it.
        self.assertEqual(float(values.min()), 0.0)
        self.assertEqual(float(values.max()), 1.0)

    def test_tensor_shape_is_one_by_height_by_width(self):
        tensor = pipeline._load_mask_tensor(self.path, binary=False)

        self.assertEqual(tuple(tensor.shape), (1, 8, 16))


if __name__ == "__main__":
    unittest.main()
