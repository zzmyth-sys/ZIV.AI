"""CPU-only tests for the multi-image helpers (Step 9C.5-D).

Pure stdlib ``unittest``; imports only :mod:`multi_image`, so no ``comfy`` / ``torch``
stack is needed. Run from ``python/server`` with::

    python -m unittest test_multi_image
"""

import unittest

import multi_image


class NormalizeAdditionalImagesTests(unittest.TestCase):
    def test_none_and_non_list_yield_empty(self):
        for value in (None, "", "ref.png", 123, {"a": 1}):
            self.assertEqual(multi_image.normalize_additional_images(value), [])

    def test_blank_entries_are_dropped(self):
        self.assertEqual(
            multi_image.normalize_additional_images(["a.png", "", "   ", None, "b.png"]),
            ["a.png", "b.png"],
        )

    def test_order_is_preserved(self):
        self.assertEqual(
            multi_image.normalize_additional_images(["c.png", "a.png", "b.png"]),
            ["c.png", "a.png", "b.png"],
        )

    def test_values_are_stringified_and_stripped(self):
        self.assertEqual(
            multi_image.normalize_additional_images(["  a.png  ", "b.png"]),
            ["a.png", "b.png"],
        )

    def test_empty_list_yields_empty(self):
        self.assertEqual(multi_image.normalize_additional_images([]), [])


class ReferencePathsTests(unittest.TestCase):
    def test_main_is_first_then_extras(self):
        self.assertEqual(
            multi_image.reference_paths("main.png", ["r1.png", "r2.png"]),
            ["main.png", "r1.png", "r2.png"],
        )

    def test_blank_main_is_dropped(self):
        self.assertEqual(
            multi_image.reference_paths("   ", ["r1.png"]),
            ["r1.png"],
        )

    def test_none_main_with_no_extras_is_empty(self):
        self.assertEqual(multi_image.reference_paths(None, None), [])

    def test_extras_are_normalized(self):
        self.assertEqual(
            multi_image.reference_paths("main.png", ["", "r1.png", "  ", None, "r2.png"]),
            ["main.png", "r1.png", "r2.png"],
        )


if __name__ == "__main__":
    unittest.main()
