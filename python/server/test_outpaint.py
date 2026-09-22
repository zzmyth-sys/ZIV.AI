"""CPU-only tests for the outpaint geometry module (Step 7 Phase 2).

Pure stdlib ``unittest`` + PIL/numpy; imports only :mod:`outpaint`, so no
``comfy`` / ``torch`` stack is needed. Run from ``python/server`` with::

    python -m unittest test_outpaint
"""

import os
import tempfile
import unittest

import numpy as np
from PIL import Image

import outpaint


class NormalizeAnchorTests(unittest.TestCase):
    def test_valid_anchors_pass_through(self):
        for anchor in outpaint.ANCHORS:
            self.assertEqual(outpaint.normalize_anchor(anchor), anchor)

    def test_case_and_whitespace_are_normalized(self):
        self.assertEqual(outpaint.normalize_anchor("  Top-Left "), "top-left")

    def test_invalid_values_degrade_to_center(self):
        for value in (None, "", "bogus", "middle", 123):
            self.assertEqual(outpaint.normalize_anchor(value), "center")


class AnchorPositionTests(unittest.TestCase):
    def test_center_centers_both_axes(self):
        self.assertEqual(outpaint.anchor_position("center", 1000, 500, 400, 500), (300, 0))

    def test_left_pins_x_and_centers_y(self):
        self.assertEqual(outpaint.anchor_position("left", 1000, 500, 400, 300), (0, 100))

    def test_bottom_right_pins_both_axes(self):
        self.assertEqual(outpaint.anchor_position("bottom-right", 1000, 500, 400, 300), (600, 200))


class Snap16Tests(unittest.TestCase):
    def test_snaps_down_to_multiple_of_16(self):
        self.assertEqual(outpaint.snap16(2752), 2752)
        self.assertEqual(outpaint.snap16(2751), 2736)
        self.assertEqual(outpaint.snap16(1), 32)


class BuildMaskTests(unittest.TestCase):
    def test_new_region_is_one_and_source_center_is_zero(self):
        # source 400x300 pasted at (608, 212) in a 1008x512 canvas.
        mask = outpaint.build_mask(1008, 512, 608, 212, 400, 300)

        self.assertEqual(mask.shape, (512, 1008))
        self.assertAlmostEqual(float(mask[0, 0]), 1.0, places=2)        # new region
        self.assertLess(float(mask[362, 808]), 0.05)                    # source centre kept
        # The transition is soft (some pixels strictly between keep and regenerate).
        interior = mask[212:512, 608:1008]
        self.assertTrue(((interior > 0.05) & (interior < 0.95)).any())

    def test_unpadded_edge_does_not_ramp(self):
        # bottom/right flush (source reaches the canvas edge): no ramp there.
        mask = outpaint.build_mask(1008, 512, 608, 212, 400, 300)
        self.assertLess(float(mask[511, 807]), 0.05)  # bottom edge of the source


class BuildOutpaintTests(unittest.TestCase):
    def _make_source(self, width, height):
        handle = tempfile.NamedTemporaryFile(suffix=".png", delete=False)
        handle.close()
        Image.new("RGB", (width, height), (255, 0, 0)).save(handle.name)
        self.addCleanup(lambda: os.path.exists(handle.name) and os.remove(handle.name))
        return handle.name

    def test_canvas_and_mask_match_target_size(self):
        source = self._make_source(400, 300)
        with tempfile.TemporaryDirectory() as workdir:
            canvas_path, mask_path = outpaint.build_outpaint(
                source, 1008, 512, "bottom-right", workdir
            )
            with Image.open(canvas_path) as canvas, Image.open(mask_path) as mask:
                self.assertEqual(canvas.size, (1008, 512))
                self.assertEqual(mask.mode, "L")
                self.assertEqual(mask.size, (1008, 512))

                # Grey 0.5 fill in the new region (official ImagePadForOutpaint).
                self.assertEqual(canvas.getpixel((0, 0)), (128, 128, 128))
                # Source pasted at (608, 212) and kept.
                self.assertEqual(canvas.getpixel((1007, 511)), (255, 0, 0))
                self.assertEqual(mask.getpixel((0, 0)), 255)
                self.assertLess(mask.getpixel((808, 362)), 13)  # source centre ~ kept

    def test_oversize_source_is_scaled_to_fit(self):
        source = self._make_source(2000, 1000)
        with tempfile.TemporaryDirectory() as workdir:
            _, mask_path = outpaint.build_outpaint(source, 1008, 512, "center", workdir)
            with Image.open(mask_path) as mask:
                # Source nearly fills the canvas (centred, 4px top/bottom band),
                # so the corner is mostly regenerate (softened by the blur).
                self.assertGreater(mask.getpixel((0, 0)), 180)
                self.assertLess(mask.getpixel((504, 256)), 13)   # centre = kept


if __name__ == "__main__":
    unittest.main()
