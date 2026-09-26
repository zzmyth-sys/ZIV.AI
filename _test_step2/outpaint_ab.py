"""P1 · /扩图 mask-strategy A/B (real machine, GPU; user-run).

Same crop-outpaint canvas, same seed / steps / native size, two variants:

  A (reference-aligned): NO mask  -> QW21edit reference-conditioned edit on the canvas.
  B (official-ish)     : binary mask -> inpaint; mask = the gray padding region, grown a
                          few px into the source, hard 0 / 255 (nearest resize).

Outputs `outpaint_ab_A.png`, `outpaint_ab_B.png`, `outpaint_ab_sheet.png`,
`outpaint_ab_result.json` next to this script (or under the image's folder).

Usage:
  python outpaint_ab.py --image "<crop-canvas.png>" [--steps 30] [--seed 42] [--grow 6]

Only 2 inferences. Z30: confirm the GPU is idle first.
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
from PIL import Image, ImageDraw, ImageFont  # noqa: E402

DEFAULT_PROMPT = (
    "Extend the image to fill the blank canvas seamlessly and continue the scene; "
    "keep the existing image content unchanged."
)


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


def dilate(mask_u8, radius):
    """Separable rectangular max-dilation of the 255-region (binary grow)."""
    out = mask_u8.copy()
    for shift in range(1, radius + 1):
        out[:, shift:] = np.maximum(out[:, shift:], mask_u8[:, :-shift])
        out[:, :-shift] = np.maximum(out[:, :-shift], mask_u8[:, shift:])
    horizontal = out.copy()
    for shift in range(1, radius + 1):
        out[shift:, :] = np.maximum(out[shift:, :], horizontal[:-shift, :])
        out[:-shift, :] = np.maximum(out[:-shift, :], horizontal[shift:, :])
    return out


def build_binary_mask(image_path, grow, out_path):
    """255 where the canvas is exact mid-gray (the outpaint padding), grown by `grow` px."""
    arr = np.asarray(Image.open(image_path).convert("RGB"))
    grey = ((arr[..., 0] == 128) & (arr[..., 1] == 128) & (arr[..., 2] == 128))
    mask = (grey.astype(np.uint8) * 255)
    if grow > 0:
        mask = dilate(mask, grow)
    Image.fromarray(mask, "L").save(out_path)
    return out_path, float(grey.mean())


def run_once(model, clip, vae, image_path, prompt, width, height, steps, seed, mask_path, out_path):
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
        "resolution": {
            "mode": "explicit",
            "width": width,
            "height": height,
            "max_pixels": 4700000,
        },
    }
    if mask_path:
        request["mask_path"] = mask_path
    t0 = time.time()
    try:
        result = pipeline.run(model, clip, vae, request)
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


def sheet(images, out_path):
    font = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 24)
    h = 720
    bar = 40
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
    ap.add_argument("--prompt", default=DEFAULT_PROMPT)
    ap.add_argument("--steps", type=int, default=30)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--grow", type=int, default=6)
    args = ap.parse_args()

    src = os.path.abspath(args.image)
    if not os.path.isfile(src):
        raise SystemExit("image not found: %s" % src)
    out_dir = os.path.dirname(src)
    width, height = Image.open(src).size
    tag = os.path.join(out_dir, "outpaint_ab")

    mask_path = tag + "_maskB.png"
    _, grey_frac = build_binary_mask(src, args.grow, mask_path)

    log("== plan ==")
    log("  image : %s (%dx%d)" % (src, width, height))
    log("  prompt: %s" % args.prompt)
    log("  steps/seed/grow: %d / %d / %d" % (args.steps, args.seed, args.grow))
    log("  grey fraction (padding): %.3f" % grey_frac)
    log("  smi_before_mb: %s" % smi_used_mb())

    log("== loading models ==")
    import model_loader  # noqa: E402

    model, _ = model_loader.load_dit()
    clip, _ = model_loader.load_text_encoder()
    vae, _ = model_loader.load_vae()

    log("== A (no mask) ==")
    a = run_once(model, clip, vae, src, args.prompt, width, height, args.steps, args.seed, None, tag + "_A.png")
    log("  %s" % json.dumps(a, ensure_ascii=False))

    log("== B (binary mask) ==")
    b = run_once(model, clip, vae, src, args.prompt, width, height, args.steps, args.seed, mask_path, tag + "_B.png")
    log("  %s" % json.dumps(b, ensure_ascii=False))

    try:
        tiles = [("INPUT", Image.open(src).convert("RGB"))]
        if a.get("ok"):
            tiles.append(("A no-mask", Image.open(tag + "_A.png").convert("RGB")))
        if b.get("ok"):
            tiles.append(("B binary-mask", Image.open(tag + "_B.png").convert("RGB")))
        sheet(tiles, tag + "_sheet.png")
        log("wrote %s" % (tag + "_sheet.png"))
    except Exception:  # noqa: BLE001
        traceback.print_exc()

    summary = {
        "image": src, "width": width, "height": height, "prompt": args.prompt,
        "steps": args.steps, "seed": args.seed, "grow": args.grow,
        "grey_fraction": round(grey_frac, 4), "A": a, "B": b,
    }
    with open(tag + "_result.json", "w", encoding="utf-8") as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=2)
    log("wrote %s" % (tag + "_result.json"))


if __name__ == "__main__":
    main()
