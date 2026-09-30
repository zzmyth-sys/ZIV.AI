"""G1 official-ComfyUI workflow builder (rebuilt G1 definition; judge-fixed).

Builds the official ComfyUI API workflow (the ``/prompt`` graph) that mirrors the
ZIV G1 scenario, so both sides run the same load:

    op=inpaint, side=1536 (output 1216x1536), nref=2 (main + two references),
    steps=40, sampler=euler, scheduler=simple, shift=3.1 (ModelSamplingAuraFlow),
    cfg=1.0, seed=42, denoise=1.0; Viggle=0/1 (the Viggle take-over uses the
    6-step sigma schedule "1.0,0.9375,0.875,0.75,0.5,0.25").

Pure JSON construction (stdlib only; no torch / comfy / network) so it is
unit-testable head-less.  Model / LoRA names are derived from
``Template/models.json`` and ``Template/loras.json`` (data-driven), with
known-good fallbacks for a missing registry.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))

# --- rebuilt G1 definition (judge-fixed; do not drift) ---
OUTPUT_W = 1216
OUTPUT_H = 1536
REF_RESOLUTION = 1536
STEPS = 40
SAMPLER = "euler"
SCHEDULER = "simple"
SHIFT = 3.1
CFG = 1.0
SEED = 42
DENOISE = 1.0
VIGGLE_SIGMAS = "1.0, 0.9375, 0.875, 0.75, 0.5, 0.25"
G1_PROMPT = ("Replace the background of <image1> with the scene from <image2>; "
             "relight it like <image3>.")

# staged input names: run_official copies the fixtures into ComfyUI/input/
DEFAULT_IMAGES = {
    "image_1": "g1_img1.jpg",
    "image_2": "g1_img2.png",
    "image_3": "g1_img3.png",
}

_FALLBACK = {
    "unet": r"image2\qwen_image_2.1_int8_convrot.safetensors",
    "clip": "qwen3vl_8b_int8_convrot.safetensors",
    "vae": "qwen_image_2.1_vae_bf16.safetensors",
    "viggle_lora": "Qwen-Image-2.1-viggle-turbo-v0.2.1-6step-lora-r256.safetensors",
}


def _load_json(path):
    try:
        with open(path, encoding="utf-8") as fh:
            return json.load(fh)
    except (OSError, ValueError):
        return None


def name_from_path(path, category):
    """ComfyUI ``folder_paths`` name = the part after ``models/<category>/``."""
    if not path:
        return None
    p = str(path).replace("/", "\\")
    marker = "\\models\\" + category + "\\"
    i = p.lower().find(marker.lower())
    return p[i + len(marker):] if i >= 0 else os.path.basename(p)


def default_model_names(root=ROOT):
    """Derive ComfyUI loader names from the registries; fall back when absent."""
    names = dict(_FALLBACK)
    reg = _load_json(os.path.join(root, "Template", "models.json")) or {}
    for m in reg.get("models", []):
        if not m.get("default"):
            continue
        names["unet"] = name_from_path(m.get("dit_path"), "diffusion_models") or names["unet"]
        names["clip"] = name_from_path(m.get("te_path"), "text_encoders") or names["clip"]
        names["vae"] = name_from_path(m.get("vae_path"), "vae") or names["vae"]
        break
    loras = _load_json(os.path.join(root, "Template", "loras.json")) or {}
    for entry in loras.get("loras", []):
        if entry.get("id") == "qwen21-viggle-turbo-6step":
            base = os.path.basename(str(entry.get("path", "")).replace("/", "\\"))
            names["viggle_lora"] = base or names["viggle_lora"]
            break
    return names


def build_workflow(viggle, *, images=None, models=None, prompt=G1_PROMPT,
                   negative_prompt=""):
    """Return the official ComfyUI API workflow dict for the G1 scenario."""
    images = dict(DEFAULT_IMAGES if images is None else images)
    models = default_model_names() if models is None else models

    encode_inputs = {
        "clip": ["2", 0],
        "prompt": prompt,
        "negative_prompt": negative_prompt,
        "resolution": REF_RESOLUTION,
        "vae": ["3", 0],
    }
    # Reference slots are node links ([LoadImage id, slot]); the LoadImage nodes
    # below hold the staged filenames.
    image_links = {"image_1": ["4", 0], "image_2": ["5", 0], "image_3": ["6", 0]}
    for name, link in image_links.items():
        if name in images:
            encode_inputs["images." + name] = link

    wf = {
        "1": {"class_type": "UNETLoader", "inputs": {
            "unet_name": models["unet"], "weight_dtype": "default"}},
        "2": {"class_type": "CLIPLoader", "inputs": {
            "clip_name": models["clip"], "type": "qwen_image"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": models["vae"]}},
        "4": {"class_type": "LoadImage", "inputs": {"image": images["image_1"]}},
        "5": {"class_type": "LoadImage", "inputs": {"image": images["image_2"]}},
        "6": {"class_type": "LoadImage", "inputs": {"image": images["image_3"]}},
        "7": {"class_type": "TextEncodeQwenImage21", "inputs": encode_inputs},
        "8": {"class_type": "EmptyLatentImage", "inputs": {
            "width": OUTPUT_W, "height": OUTPUT_H, "batch_size": 1}},
    }

    if not viggle:
        # KSampler internally uses comfy.sample.sample, the same path ZIV runs
        # with ModelSamplingAuraFlow(shift) + euler / simple / cfg=1.0.
        wf["9"] = {"class_type": "ModelSamplingAuraFlow", "inputs": {
            "model": ["1", 0], "shift": SHIFT}}
        wf["10"] = {"class_type": "KSampler", "inputs": {
            "model": ["9", 0], "positive": ["7", 0], "negative": ["7", 1],
            "latent_image": ["8", 0], "seed": SEED, "steps": STEPS,
            "cfg": CFG, "sampler_name": SAMPLER, "scheduler": SCHEDULER,
            "denoise": DENOISE}}
        wf["11"] = {"class_type": "VAEDecode", "inputs": {
            "samples": ["10", 0], "vae": ["3", 0]}}
        wf["12"] = {"class_type": "SaveImage", "inputs": {
            "images": ["11", 0], "filename_prefix": "g1_noviggle"}}
    else:
        # Viggle take-over: runtime LoRA side branch + the 6-step sigma schedule
        # (no AuraFlow, mirroring ZIV when the plugin supplies sigmas).
        wf["9"] = {"class_type": "ViggleTurboLora", "inputs": {
            "model": ["1", 0], "lora_name": models["viggle_lora"], "strength": 1.0}}
        wf["10"] = {"class_type": "ViggleTurboSigmas", "inputs": {
            "latent": ["8", 0], "nodes": VIGGLE_SIGMAS}}
        wf["11"] = {"class_type": "KSamplerSelect", "inputs": {"sampler_name": SAMPLER}}
        wf["12"] = {"class_type": "BasicGuider", "inputs": {
            "model": ["9", 0], "conditioning": ["7", 0]}}
        wf["13"] = {"class_type": "RandomNoise", "inputs": {"noise_seed": SEED}}
        wf["14"] = {"class_type": "SamplerCustomAdvanced", "inputs": {
            "noise": ["13", 0], "guider": ["12", 0], "sampler": ["11", 0],
            "sigmas": ["10", 0], "latent_image": ["8", 0]}}
        wf["15"] = {"class_type": "VAEDecode", "inputs": {
            "samples": ["14", 0], "vae": ["3", 0]}}
        wf["16"] = {"class_type": "SaveImage", "inputs": {
            "images": ["15", 0], "filename_prefix": "g1_viggle"}}
    return wf


def sampler_node_ids(viggle):
    """The node id(s) whose ``executing``/``executed`` events bracket sampling."""
    return ["14"] if viggle else ["10"]


def main(argv=None):
    parser = argparse.ArgumentParser(prog="g1_workflow",
                                     description="Build the official G1 workflow JSON")
    parser.add_argument("--viggle", type=int, default=0, choices=[0, 1])
    parser.add_argument("--out", default=None, help="write JSON here (else stdout)")
    parser.add_argument("--prompt", default=G1_PROMPT)
    args = parser.parse_args(argv)

    wf = build_workflow(args.viggle, prompt=args.prompt)
    text = json.dumps(wf, ensure_ascii=False, indent=2)
    if args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)) or ".", exist_ok=True)
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write(text)
        print("wrote", os.path.abspath(args.out))
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
