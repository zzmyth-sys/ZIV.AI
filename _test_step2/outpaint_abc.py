"""P1/bugfix · /扩图 A/B/C (real machine, GPU; user-run).

Same crop-outpaint canvas, same seed / steps / native size, three (or four) variants:

  A: NO mask        -> reference-conditioned edit on the grey-padded canvas (current A).
  B: soft mask      -> inpaint; feather=40  / grow=20 / blur=31 (current product B).
  C: soft mask wide -> inpaint; feather=128 / grow=32 / blur=96 (wider blend, fixes harsh seams).
  D (optional): soft mask (narrow) + the clean ORIGINAL node image as an extra reference,
                so the fill can match the source appearance (needs --original).
  E (optional): NO mask, the clean ORIGINAL node image as the main/reference (English prompt).
  F (optional): same as E but with the reference-workflow Chinese prompt.
                E/F need --original.
  G: NO mask on the grey-padded canvas, reference-workflow Chinese prompt.
  H: same as G but the prompt also pins the subject's size/position.

The soft mask mirrors `OutpaintMask.Generate` / `python/server/outpaint.py::build_mask`
(255 = grey/new region to regenerate, ramping to 0 over `feather` px into the source,
then grown by `grow` and Gaussian-blurred by `blur`). It is fed with mask_binary=False so
the backend keeps the soft ramp (the latent blend `out*mask + latent*(1-mask)`).

Outputs `outpaint_abc_{A,B,C[,D]}.png`, `outpaint_abc_sheet.png`, `outpaint_abc_result.json`
next to the image.

Usage:
  python outpaint_abc.py --image "<crop-canvas.png>" [--original "<node-source.png>"]
                         [--steps 30] [--seed 42] [--variants A,B,C]

Only 3-4 inferences. Z30: confirm the GPU is idle first.
"""

import argparse
import json
import os
import subprocess
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SERVER = os.path.join(REPO, "python", "server")
sys.path.insert(0, SERVER)

import numpy as np  # noqa: E402
from PIL import Image, ImageDraw, ImageFont, ImageFilter  # noqa: E402

DEFAULT_PROMPT = (
    "Extend the image to fill the blank canvas seamlessly and continue the scene; "
    "keep the existing image content unchanged."
)

# Reference-workflow style prompt (Qwen edit collection): widen, keep content.
CLEAN_PROMPT = "把图片左右扩宽成横图，保留原来的内容，人物大小和位置保持不变。"

# G/H work on the grey-padded canvas (no mask) with a Chinese prompt.
G_PROMPT = "把图片左右扩宽成横图，保留原来的内容。"
H_PROMPT = "把图片左右扩宽成横图，保留原来的内容，人物大小和位置保持不变。"

# I = the Lazybuxuexi workflow's outpaint, copied 1:1: scale the source to fit the target
# canvas, center it on a BLUE canvas (B=1), no mask, empty latent at the target size, and
# the workflow's built-in outpaint prompt (styles/lazy_styles.json).
OUTPAINT_PROMPT = (
    "Outpaint the image to fill the entire canvas. Replace all solid blue padded regions "
    "with coherent continuation of the scene. Keep the original subject and content "
    "unchanged outside the blue areas."
)

# M = same as the workflow prompt but worded for a GREY-padded canvas (no fill-colour change).
GREY_PROMPT = (
    "Outpaint the image to fill the entire canvas. Replace all solid grey padded regions "
    "with coherent continuation of the scene. Keep the original subject and content "
    "unchanged outside the grey areas."
)

# (feather, grow, blur) per soft-mask variant.
MASK_PARAMS = {
    "B": (40, 20, 31),
    "C": (128, 32, 96),
}


def log(*args):
    print(*args, flush=True)


def smi_used_mb():
    try:
        raw = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            stderr=subprocess.DEVNULL, timeout=5,
        )
        return int(raw.decode("utf-8", "ignore").strip().splitlines()[0].strip())
    except Exception:
        return None


def dilate(mask, radius):
    out = mask.copy()
    for shift in range(1, radius + 1):
        out[:, shift:] = np.maximum(out[:, shift:], mask[:, :-shift])
        out[:, :-shift] = np.maximum(out[:, :-shift], mask[:, shift:])
    horizontal = out.copy()
    for shift in range(1, radius + 1):
        out[shift:, :] = np.maximum(out[shift:, :], horizontal[:-shift, :])
        out[:-shift, :] = np.maximum(out[:-shift, :], horizontal[shift:, :])
    return out


def pad_outpaint_blue(image_path, tw, th, out_path):
    """Lazybuxuexi `_pad_outpaint_blue`: scale to fit, center, fill the rest BLUE."""
    img = Image.open(image_path).convert("RGB")
    iw, ih = img.size
    tw, th = max(32, tw // 32 * 32), max(32, th // 32 * 32)
    scale = min(float(tw) / iw, float(th) / ih)
    nw, nh = max(1, round(iw * scale)), max(1, round(ih * scale))
    img = img.resize((nw, nh), Image.LANCZOS)
    canvas = Image.new("RGB", (tw, th), (0, 0, 255))
    canvas.paste(img, (max(0, (tw - nw) // 2), max(0, (th - nh) // 2)))
    canvas.save(out_path)
    log("  [I] blue pad %dx%d -> %dx%d (source %dx%d)" % (iw, ih, tw, th, nw, nh))
    return out_path


def recolor_pad_blue(image_path, out_path):
    """J: keep the canvas's 1:1 placement, just turn the grey padding BLUE."""
    a = np.asarray(Image.open(image_path).convert("RGB")).copy()
    grey = (a[..., 0] == 128) & (a[..., 1] == 128) & (a[..., 2] == 128)
    a[grey] = [0, 0, 255]
    Image.fromarray(a).save(out_path)
    return out_path


def build_soft_mask(image_path, feather, grow, blur, out_path):
    """255 over the grey padding, ramping to 0 into the source (mirrors OutpaintMask)."""
    arr = np.asarray(Image.open(image_path).convert("RGB")).astype(np.int16)
    grey = (arr[..., 0] == 128) & (arr[..., 1] == 128) & (arr[..., 2] == 128)
    ys, xs = np.where(~grey)
    if len(xs) == 0:
        return None, 1.0
    x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1
    h, w = grey.shape
    sw, sh = x1 - x0, y1 - y0
    mask = np.ones((h, w), dtype=np.float32)
    top_pad, bottom_pad = y0 > 0, y1 < h
    left_pad, right_pad = x0 > 0, x1 < w
    if feather * 2 < sw and feather * 2 < sh:
        ii = np.arange(sh, dtype=np.float32)[:, None]
        jj = np.arange(sw, dtype=np.float32)[None, :]
        dt = ii if top_pad else np.full((sh, 1), float(sh), dtype=np.float32)
        db = (sh - ii) if bottom_pad else np.full((sh, 1), float(sh), dtype=np.float32)
        dl = jj if left_pad else np.full((1, sw), float(sw), dtype=np.float32)
        dr = (sw - jj) if right_pad else np.full((1, sw), float(sw), dtype=np.float32)
        d = np.minimum(np.minimum(dt, db), np.minimum(dl, dr))
        v = np.clip((feather - d) / float(feather), 0.0, 1.0)
        patch = v * v
    else:
        patch = np.zeros((sh, sw), dtype=np.float32)
    mask[y0:y1, x0:x1] = patch
    if grow > 0:
        mask = dilate(mask, grow)
    if blur > 0:
        m8 = Image.fromarray((np.clip(mask, 0.0, 1.0) * 255.0).astype(np.uint8), "L")
        m8 = m8.filter(ImageFilter.GaussianBlur(blur))
        mask = np.asarray(m8).astype(np.float32) / 255.0
    Image.fromarray((np.clip(mask, 0.0, 1.0) * 255.0).astype(np.uint8), "L").save(out_path)
    return out_path, float(grey.mean())


def run_once(model, clip, vae, image_path, prompt, width, height, steps, seed,
             mask_path, out_path, additional_images=None):
    import pipeline

    import torch

    torch.cuda.reset_peak_memory_stats()
    request = {
        "prompt": prompt,
        "image_path": image_path,
        "output_path": out_path,
        "steps": steps,
        "seed": seed,
        "denoise": 1.0,
        "resolution": {"mode": "explicit", "width": width, "height": height, "max_pixels": 4700000},
    }
    if mask_path:
        request["mask_path"] = mask_path
    if additional_images:
        request["additional_images"] = additional_images
    t0 = time.time()
    try:
        # mask_binary=False keeps the soft ramp (same as the product's inpaint path).
        result = pipeline.run(model, clip, vae, request, mask_binary=False)
        return {
            "ok": True,
            "output": out_path,
            "width": result.get("width"),
            "height": result.get("height"),
            "wall_s": round(time.time() - t0, 2),
            "pipeline_ms": result.get("duration_ms"),
            "peak_alloc_mb": round(torch.cuda.max_memory_allocated() / (1024.0 ** 2), 1),
            "smi_used_mb": smi_used_mb(),
        }
    except Exception as exc:  # noqa: BLE001
        traceback.print_exc()
        return {"ok": False, "error": repr(exc), "wall_s": round(time.time() - t0, 2)}


def run_ref_res_once(model, clip, vae, image_path, prompt, out_w, out_h, ref_res,
                     steps, seed, out_path):
    """K: reference resampled to ~ref_res^2 area (official node style), target latent = canvas."""
    import math as _math

    import comfy.sample
    import comfy.utils
    import comfy.model_management as mm
    import node_helpers
    import torch
    from comfy_extras.nodes_model_advanced import ModelSamplingAuraFlow

    import config
    import models
    import pipeline

    sampler = models.resolve_sampler(None)
    if sampler.get("type", "auraflow") == "auraflow":
        shift = float(sampler.get("shift", config.AURAFLOW_SHIFT))
        model = ModelSamplingAuraFlow().patch_aura(model, shift)[0]

    src = pipeline._load_image_tensor(image_path)          # [1,H,W,3]
    s = src[:1].movedim(-1, 1)                             # [1,3,H,W]
    ratio = float(s.shape[3]) / float(s.shape[2])
    rw = max(32, round(_math.sqrt(ref_res * ref_res * ratio) / 32) * 32)
    rh = max(32, round(_math.sqrt(ref_res * ref_res / ratio) / 32) * 32)
    resized = comfy.utils.common_upscale(s, rw, rh, "lanczos", "disabled").movedim(1, -1)
    rgb = resized[..., :3].contiguous()
    ref_latents = [vae.encode(resized.contiguous())]
    pos = clip.encode_from_tokens_scheduled(
        clip.tokenize(prompt or "", images=[rgb], keep_vision=False, prevent_empty_text=True))
    neg = clip.encode_from_tokens_scheduled(
        clip.tokenize("", images=[rgb], keep_vision=False, prevent_empty_text=True))
    pos = node_helpers.conditioning_set_values(pos, {"reference_latents": ref_latents}, append=True)
    neg = node_helpers.conditioning_set_values(neg, {"reference_latents": ref_latents}, append=True)

    lw = max(32, int(out_w) // 16 * 16)
    lh = max(32, int(out_h) // 16 * 16)
    latent = torch.zeros([1, 64, lh // 16, lw // 16], device=mm.intermediate_device())
    noise = comfy.sample.prepare_noise(latent, seed)
    torch.cuda.reset_peak_memory_stats()
    t0 = time.time()
    samples = pipeline.sample(
        model, pos, neg, latent, noise, steps, 1.0, None, seed, None,
        sampler_name=sampler.get("sampler_name"), scheduler=sampler.get("scheduler"),
        cfg=float(sampler.get("cfg", 1.0)))
    decoded = pipeline.vae_decode(vae, samples)
    image, height, width = pipeline.to_pil(decoded[0])
    pipeline.save_png(image, out_path)
    return {
        "ok": True, "output": out_path, "width": width, "height": height,
        "ref": [rw, rh], "wall_s": round(time.time() - t0, 2),
        "peak_alloc_mb": round(torch.cuda.max_memory_allocated() / (1024.0 ** 2), 1),
        "smi_used_mb": smi_used_mb(),
    }


def sheet(images, out_path):
    font = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 24)
    h, bar = 720, 40
    tiles = []
    for label, im in images:
        w = max(1, round(im.width * h / im.height))
        tiles.append((label, im.resize((w, h), Image.LANCZOS)))
    total_w = sum(im.width for _, im in tiles) + 10 * (len(tiles) - 1)
    out = Image.new("RGB", (total_w, h + bar), (24, 24, 24))
    draw = ImageDraw.Draw(out)
    x = 0
    for label, im in tiles:
        out.paste(im, (x, bar))
        draw.text((x + 8, 8), label, fill=(240, 240, 240), font=font)
        x += im.width + 10
    out.save(out_path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--image", required=True, help="the grey-padded crop canvas PNG")
    ap.add_argument("--original", default=None, help="clean node source (enables variant D)")
    ap.add_argument("--prompt", default=DEFAULT_PROMPT)
    ap.add_argument("--steps", type=int, default=30)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--variants", default="A,B,C", help="subset of A,B,C,D")
    args = ap.parse_args()

    src = os.path.abspath(args.image)
    if not os.path.isfile(src):
        raise SystemExit("image not found: %s" % src)
    variants = [v.strip().upper() for v in args.variants.split(",") if v.strip()]
    original = os.path.abspath(args.original) if args.original else None
    if any(v in variants for v in ("D", "E", "F", "I")) and (not original or not os.path.isfile(original)):
        raise SystemExit("variants D/E/F/I need --original <clean image>")

    out_dir = os.path.dirname(src)
    width, height = Image.open(src).size
    tag = os.path.join(out_dir, "outpaint_abc")

    masks = {}
    grey_frac = None
    for v in ("B", "C"):
        if v in variants:
            p = "%s_mask%s.png" % (tag, v)
            _, grey_frac = build_soft_mask(src, *MASK_PARAMS[v], p)
            masks[v] = p
    if "D" in variants:
        p = "%s_maskD.png" % tag
        _, grey_frac = build_soft_mask(src, *MASK_PARAMS["B"], p)
        masks["D"] = p

    log("== plan ==")
    log("  image : %s (%dx%d)" % (src, width, height))
    log("  prompt: %s" % args.prompt)
    log("  steps/seed: %d / %d" % (args.steps, args.seed))
    log("  variants : %s" % variants)
    log("  grey fraction (padding): %s" % grey_frac)
    log("  smi_before_mb: %s" % smi_used_mb())

    log("== loading models ==")
    import model_loader  # noqa: E402

    model, _ = model_loader.load_dit()
    clip, _ = model_loader.load_text_encoder()
    vae, _ = model_loader.load_vae()

    results = {}
    for v in variants:
        log("== %s ==" % v)
        if v in ("E", "F"):
            # Reference-workflow style: the clean original is the main image, no mask.
            main = original
            prompt = CLEAN_PROMPT if v == "F" else args.prompt
            res = run_once(
                model, clip, vae, main, prompt, width, height, args.steps, args.seed,
                None, "%s_%s.png" % (tag, v),
            )
        elif v in ("G", "H"):
            # Grey-padded canvas (no mask) with a Chinese prompt.
            prompt = G_PROMPT if v == "G" else H_PROMPT
            res = run_once(
                model, clip, vae, src, prompt, width, height, args.steps, args.seed,
                None, "%s_%s.png" % (tag, v),
            )
        elif v == "I":
            # Lazybuxuexi workflow outpaint: blue-pad the clean original, no mask.
            pad = pad_outpaint_blue(original, width, height, "%s_Ipad.png" % tag)
            res = run_once(
                model, clip, vae, pad, OUTPAINT_PROMPT, width, height,
                args.steps, args.seed, None, "%s_I.png" % tag,
            )
        elif v == "J":
            # 1:1 placement kept; only the padding colour + prompt come from the workflow.
            blue = recolor_pad_blue(src, "%s_Jpad.png" % tag)
            res = run_once(
                model, clip, vae, blue, OUTPAINT_PROMPT, width, height,
                args.steps, args.seed, None, "%s_J.png" % tag,
            )
        elif v == "K":
            # J's blue 1:1 canvas, but the reference is resampled to ~1K (official node style).
            blue = recolor_pad_blue(src, "%s_Jpad.png" % tag)
            res = run_ref_res_once(
                model, clip, vae, blue, OUTPAINT_PROMPT, width, height,
                1024, args.steps, args.seed, "%s_K.png" % tag,
            )
        elif v == "L":
            # Generate at 1K (max side 1024, proportional), blue 1:1 canvas, workflow prompt.
            sc = 1024.0 / float(max(width, height))
            ow = max(32, int(round(width * sc)) // 16 * 16)
            oh = max(32, int(round(height * sc)) // 16 * 16)
            blue = recolor_pad_blue(src, "%s_Jpad.png" % tag)
            log("  [L] target %dx%d (from %dx%d)" % (ow, oh, width, height))
            res = run_once(
                model, clip, vae, blue, OUTPAINT_PROMPT, ow, oh,
                args.steps, args.seed, None, "%s_L.png" % tag,
            )
        elif v == "M":
            # Keep the GREY canvas as-is; only the prompt + no-mask change.
            res = run_once(
                model, clip, vae, src, GREY_PROMPT, width, height,
                args.steps, args.seed, None, "%s_M.png" % tag,
            )
        else:
            mask_path = masks.get(v)
            extra = [original] if v == "D" else None
            res = run_once(
                model, clip, vae, src, args.prompt, width, height, args.steps, args.seed,
                mask_path, "%s_%s.png" % (tag, v), additional_images=extra,
            )
        results[v] = res
        log("  %s" % json.dumps(res, ensure_ascii=False))

    try:
        tiles = [("INPUT", Image.open(src).convert("RGB"))]
        for v in variants:
            p = "%s_%s.png" % (tag, v)
            if results.get(v, {}).get("ok") and os.path.isfile(p):
                tiles.append((v, Image.open(p).convert("RGB")))
        sheet(tiles, tag + "_sheet.png")
        log("wrote %s" % (tag + "_sheet.png"))
    except Exception:  # noqa: BLE001
        traceback.print_exc()

    summary = {
        "image": src, "original": original, "width": width, "height": height,
        "prompt": args.prompt, "steps": args.steps, "seed": args.seed,
        "variants": variants, "grey_fraction": grey_frac, "results": results,
    }
    with open(tag + "_result.json", "w", encoding="utf-8") as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=2)
    log("wrote %s" % (tag + "_result.json"))


if __name__ == "__main__":
    main()
