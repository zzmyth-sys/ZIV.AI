"""Unit tests for the tracked official G1 workflow builder (no GPU, no network)."""

import os
import sys
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from tools.zivcli.official import g1_workflow  # noqa: E402
from tools.zivcli.official import run_official  # noqa: E402


class NonViggleWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.wf = g1_workflow.build_workflow(0)

    def test_sampler_params_match_g1(self):
        ks = self.wf["10"]
        self.assertEqual(ks["class_type"], "KSampler")
        self.assertEqual(ks["inputs"]["steps"], 40)
        self.assertEqual(ks["inputs"]["cfg"], 1.0)
        self.assertEqual(ks["inputs"]["sampler_name"], "euler")
        self.assertEqual(ks["inputs"]["scheduler"], "simple")
        self.assertEqual(ks["inputs"]["seed"], 42)
        self.assertEqual(ks["inputs"]["denoise"], 1.0)

    def test_auraflow_shift_and_resolution(self):
        self.assertEqual(self.wf["9"]["class_type"], "ModelSamplingAuraFlow")
        self.assertEqual(self.wf["9"]["inputs"]["shift"], 3.1)
        enc = self.wf["7"]["inputs"]
        self.assertEqual(self.wf["7"]["class_type"], "TextEncodeQwenImage21")
        self.assertEqual(enc["resolution"], 1536)
        self.assertEqual(self.wf["8"]["inputs"]["width"], 1216)
        self.assertEqual(self.wf["8"]["inputs"]["height"], 1536)

    def test_three_reference_inputs_and_no_viggle(self):
        enc = self.wf["7"]["inputs"]
        self.assertEqual(enc["images.image_1"], ["4", 0])
        self.assertEqual(enc["images.image_2"], ["5", 0])
        self.assertEqual(enc["images.image_3"], ["6", 0])
        self.assertEqual(self.wf["4"]["inputs"]["image"], "g1_img1.jpg")
        types = {n["class_type"] for n in self.wf.values()}
        self.assertNotIn("ViggleTurboLora", types)
        self.assertNotIn("SamplerCustomAdvanced", types)

    def test_sampler_node_ids(self):
        self.assertEqual(g1_workflow.sampler_node_ids(0), ["10"])


class ViggleWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.wf = g1_workflow.build_workflow(1)

    def test_sigmas_and_lora(self):
        self.assertEqual(self.wf["10"]["class_type"], "ViggleTurboSigmas")
        self.assertEqual(self.wf["10"]["inputs"]["nodes"], g1_workflow.VIGGLE_SIGMAS)
        self.assertEqual(self.wf["9"]["class_type"], "ViggleTurboLora")
        self.assertEqual(self.wf["9"]["inputs"]["strength"], 1.0)

    def test_no_auraflow_uses_custom_sampler(self):
        self.assertEqual(self.wf["14"]["class_type"], "SamplerCustomAdvanced")
        self.assertEqual(self.wf["13"]["inputs"]["noise_seed"], 42)
        self.assertEqual(self.wf["11"]["inputs"]["sampler_name"], "euler")
        types = {n["class_type"] for n in self.wf.values()}
        self.assertNotIn("ModelSamplingAuraFlow", types)
        self.assertNotIn("KSampler", types)

    def test_sampler_node_ids(self):
        self.assertEqual(g1_workflow.sampler_node_ids(1), ["14"])


class NameDerivationTests(unittest.TestCase):
    def test_name_from_path_strips_models_category(self):
        p = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\m.safetensors"
        self.assertEqual(g1_workflow.name_from_path(p, "diffusion_models"),
                         r"image2\m.safetensors")

    def test_name_from_path_fallback_basename(self):
        self.assertEqual(g1_workflow.name_from_path("x/y/z.pt", "text_encoders"), "z.pt")
        self.assertIsNone(g1_workflow.name_from_path(None, "vae"))


class TimingTests(unittest.TestCase):
    def test_extract_timing_splits_load_and_sample(self):
        events = [
            (100.0, "execution_start", {}),
            (110.0, "executing", {"node": "7"}),
            (130.0, "executing", {"node": "10"}),
            (135.0, "progress", {"node": "10"}),
            (200.0, "executed", {"node": "10"}),
            (202.0, "execution_success", {}),
        ]
        t = run_official.extract_timing(events, ["10"], t_submit=99.0)
        self.assertEqual(t["load_sec"], 30.0)   # start -> sampler start
        self.assertEqual(t["sample_sec"], 70.0)  # sampler start -> sampler end
        self.assertEqual(t["wall_total_sec"], 102.0)

    def test_extract_timing_falls_back(self):
        t = run_official.extract_timing([], ["10"], t_submit=50.0)
        self.assertEqual(t["load_sec"], 0.0)
        self.assertEqual(t["sample_sec"], 0.0)
        self.assertEqual(t["wall_total_sec"], 0.0)


if __name__ == "__main__":
    unittest.main()
