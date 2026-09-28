"""Pure pipeline helpers split out of ``pipeline.py`` (Z8 600-line budget).

Image / mask tensor IO, VAE-decode → PIL, output-path / seed / denoise resolution, the
progress emitter and temp-tree removal. Same-named functions are re-imported into
:mod:`pipeline`, so ``pipeline._load_mask_tensor`` / ``pipeline.to_pil`` … keep resolving
(the same re-import pattern used for the earlier ``resolution.py`` split, FROZEN 9C.5-D.6).

No torch / numpy / PIL import at module load: every heavy import stays inside the
function so importing this module is cheap and CPU-only.
"""

import datetime
import os
import random

import config


def _remove_tree(path):
    import shutil

    try:
        shutil.rmtree(path, ignore_errors=True)
    except Exception:
        pass


def vae_decode(vae, samples):
    """Pipeline stage: decode the sampled latent back to pixels."""
    return vae.decode(samples)


def to_pil(tensor):
    """Convert a decoded tensor to a PIL image plus (height, width)."""
    return _to_pil(tensor)


def save_png(image, output_path):
    """Pipeline stage: write the output as a new PNG file (Z24)."""
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    image.save(output_path)


def _load_image_tensor(path):
    import numpy as np
    import torch
    from PIL import Image

    image = Image.open(path).convert("RGB")
    array = np.asarray(image).astype(np.float32) / 255.0
    return torch.from_numpy(array)[None, ...]  # [1,H,W,3]


def _load_mask_tensor(path, binary=True):
    import numpy as np
    import torch
    from PIL import Image

    image = Image.open(path).convert("L")
    array = np.asarray(image).astype(np.float32) / 255.0
    if binary:
        array = (array >= 0.5).astype(np.float32)  # legacy: threshold a binary mask to 0 / 1
    # binary=False keeps the soft 0..1 ramp (user feather OR backend outpaint mask).
    return torch.from_numpy(array)[None, ...]  # [1,H,W]


def _mask_is_binary(mask):
    """True when every mask value is exactly 0 or 1 (a hard mask)."""
    import torch

    return bool(torch.all((mask == 0) | (mask == 1)))


def _resize_mask(mask, width, height, mode="nearest"):
    import torch

    if mask.shape[-1] == width and mask.shape[-2] == height:
        return mask
    resized = torch.nn.functional.interpolate(
        mask[:1, None], size=(height, width), mode=mode
    )
    return resized[0]


def _to_pil(tensor):
    import numpy as np
    from PIL import Image

    array = tensor.detach().cpu().float().clamp(0, 1).numpy()
    array = (array * 255.0).round().astype(np.uint8)
    image = Image.fromarray(array)
    return image, image.height, image.width


def _resolve_denoise(value):
    try:
        number = float(value)
    except (TypeError, ValueError):
        return 1.0
    return min(1.0, max(0.0, number))


def _resolve_seed(seed):
    try:
        value = int(seed)
    except (TypeError, ValueError):
        value = -1
    if value < 0:
        return random.randint(0, 0x7FFFFFFF)
    return value


def _resolve_output_path(requested, image_path):
    """Pick an output path that never overwrites the source (Z24 / SPEC §3.9)."""
    if requested:
        candidate = os.path.abspath(requested)
        if not image_path or candidate != os.path.abspath(image_path):
            return candidate
    if image_path:
        directory = os.path.dirname(os.path.abspath(image_path))
        stem = os.path.splitext(os.path.basename(image_path))[0]
    else:
        directory = config.OUTPUT_DIR
        os.makedirs(directory, exist_ok=True)
        stem = "zivai"

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    candidate = os.path.join(directory, "%s_ai_%s.png" % (stem, stamp))
    suffix = 1
    while os.path.exists(candidate):
        candidate = os.path.join(directory, "%s_ai_%s_%d.png" % (stem, stamp, suffix))
        suffix += 1
    return candidate


def _emit(on_progress, step, total, fraction, stage, message):
    if on_progress is not None:
        on_progress(step, total, fraction, stage, message)
