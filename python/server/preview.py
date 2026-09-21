"""Latent preview emission for the ZIV.AI backend (Step 2.3).

Wraps ComfyUI's ``latent_preview`` previewer: the denoised latent ``x0`` seen
in the sampler callback is decoded into a small JPEG which the client receives
as a ``preview`` control frame followed by a ``0x02`` binary frame
(see ``contracts/ipc-protocol.md`` §3.5).

Imports of ComfyUI / torch stay inside the functions so a cold ``ping`` never
touches the heavy stack.
"""

import io

import config

JPEG_FORMAT = "JPEG"
JPEG_QUALITY = 85


def get_previewer(model):
    """Return a ComfyUI previewer for ``model``, or ``None`` if unavailable.

    ComfyUI defaults to ``--preview-method none``; enable ``auto`` (which
    resolves to Latent2RGB when the latent format exposes RGB factors) so the
    lightweight previewer works without a TAESD checkpoint.
    """
    import latent_preview
    from comfy.cli_args import LatentPreviewMethod, args

    if args.preview_method == LatentPreviewMethod.NoPreviews:
        args.preview_method = LatentPreviewMethod.Auto
    if args.preview_size is None or args.preview_size < 1:
        args.preview_size = config.PREVIEW_SIZE

    latent_format = getattr(getattr(model, "model", None), "latent_format", None)
    load_device = getattr(model, "load_device", None)
    if latent_format is None or load_device is None:
        return None

    try:
        return latent_preview.get_previewer(load_device, latent_format)
    except Exception:
        return None


def encode_jpeg(previewer, x0):
    """Decode ``x0`` to a JPEG byte string, or ``None`` when not previewable."""
    if previewer is None:
        return None
    if getattr(x0, "is_nested", False):
        x0 = x0.tensors[0]
    result = previewer.decode_latent_to_preview_image(JPEG_FORMAT, x0)
    if not result:
        return None
    image = result[1]
    if image is None:
        return None
    buffer = io.BytesIO()
    image.save(buffer, format=JPEG_FORMAT, quality=JPEG_QUALITY)
    return buffer.getvalue()
