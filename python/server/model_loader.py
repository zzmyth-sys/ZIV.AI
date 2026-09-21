"""Model loading for the ZIV.AI inference backend (Step 2.2).

Loads the Qwen-Image-2.1 triple (DiT / text encoder / VAE) through ComfyUI's
in-process APIs, mirroring the verified scripts in `_test_step2/t1..t3`. No
inference logic lives here; sampling arrives in Step 2.3.

The ComfyUI source tree is imported lazily so that a cold `ping` (models not
loaded yet) stays fast and never touches `torch`.
"""

import os
import sys
import time

import config

# (start_fraction, done_fraction) per stage; the six loading frames the client
# observes are: dit@0.0, dit@0.33, te@0.33, te@0.66, vae@0.66, vae@1.0.
STAGE_FRACTIONS = {
    "dit": (0.0, 0.33),
    "te": (0.33, 0.66),
    "vae": (0.66, 1.0),
}

STAGES = ("dit", "te", "vae")

_environment_ready = False


def prepare_environment():
    """Make the ComfyUI source tree importable, then apply runtime defaults.

    Idempotent. The smart-memory flag must be set before the first
    `import comfy.model_management` (it reads the value into a module constant
    at import time), so it is applied here — before any `load_*` import.
    """
    global _environment_ready
    if _environment_ready:
        return
    root = config.COMFY_ROOT
    if root and os.path.isdir(root):
        if root not in sys.path:
            sys.path.insert(0, root)
        try:
            os.chdir(root)
        except OSError:
            pass
    _apply_runtime_defaults()
    _environment_ready = True


def _apply_runtime_defaults():
    if not config.DISABLE_SMART_MEMORY:
        return
    try:
        import comfy.cli_args as ca

        ca.args.disable_smart_memory = True
    except Exception:
        pass


def load_dit(path=None, on_loaded=None):
    """Load the diffusion model. Returns (model, elapsed_seconds)."""
    prepare_environment()
    import comfy.sd

    path = path or config.DIT_MODEL_PATH
    start = time.time()
    model = comfy.sd.load_diffusion_model(path)
    elapsed = time.time() - start
    _notify(on_loaded, "dit", elapsed)
    return model, elapsed


def load_text_encoder(path=None, on_loaded=None):
    """Load the Qwen-Image-2.1 text encoder. Returns (clip, elapsed_seconds)."""
    prepare_environment()
    import comfy.sd

    path = path or config.TEXT_ENCODER_PATH
    start = time.time()
    clip = comfy.sd.load_clip([path], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    elapsed = time.time() - start
    _notify(on_loaded, "te", elapsed)
    return clip, elapsed


def load_vae(path=None, on_loaded=None):
    """Load the Qwen-Image-2.1 VAE. Returns (vae, elapsed_seconds)."""
    prepare_environment()
    import comfy.sd
    import comfy.utils

    path = path or config.VAE_PATH
    start = time.time()
    state_dict = comfy.utils.load_torch_file(path)
    vae = comfy.sd.VAE(state_dict)
    elapsed = time.time() - start
    _notify(on_loaded, "vae", elapsed)
    return vae, elapsed


def _notify(on_loaded, sub_stage, elapsed):
    if on_loaded is not None:
        fraction = STAGE_FRACTIONS[sub_stage][1]
        on_loaded(sub_stage, fraction, elapsed)
