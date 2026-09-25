"""Model loading for the ZIV.AI inference backend (Step 2.2).

Loads the Qwen-Image-2.1 triple (DiT / text encoder / VAE) through ComfyUI's
in-process APIs, mirroring the verified scripts in `_test_step2/t1..t3`. No
inference logic lives here; sampling arrives in Step 2.3.

The ComfyUI source tree is imported lazily so that a cold `ping` (models not
loaded yet) stays fast and never touches `torch`.
"""

import importlib.util
import logging
import os
import sys
import time

import config
import vram_probe

_LOG = logging.getLogger("zivai.server")

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
    _enable_dynamic_vram()
    vram_probe.install()
    _environment_ready = True


def _apply_runtime_defaults():
    try:
        import comfy.cli_args as ca
    except Exception:
        return
    if config.DISABLE_SMART_MEMORY:
        ca.args.disable_smart_memory = True
    if config.SAGE_ATTENTION:
        if importlib.util.find_spec("sageattention") is None:
            _LOG.warning(
                "ZIV_AI_SAGE_ATTENTION is set but the `sageattention` package is "
                "missing; keeping SageAttention disabled"
            )
        else:
            ca.args.use_sage_attention = True


def _dynamic_vram_supported():
    import comfy.model_management as mm

    if mm.is_nvidia():
        return True
    if mm.is_amd() and mm.rocm_version >= (7, 14):
        return True
    return False


def _enable_dynamic_vram():
    """Mirror ComfyUI/main.py's DynamicVRAM bootstrap.

    ``aimdo_enabled`` and ``model_patcher.CoreModelPatcher`` are set only by the
    official server entry point, so loading ComfyUI in-process silently keeps
    the legacy ModelPatcher, whose coarse offloading overflows a 16 GB card.
    """
    if not config.DYNAMIC_VRAM:
        return
    try:
        import comfy.cli_args as ca
        from comfy.cli_args import enables_dynamic_vram

        if not (ca.args.enable_dynamic_vram or enables_dynamic_vram()):
            return

        # control.init() must precede any import of comfy_aimdo.host_buffer:
        # host_buffer binds `lib = control.lib` at import time, so importing
        # comfy.model_management first would freeze that reference to None.
        import comfy_aimdo.control as control

        headroom = None if ca.args.reserve_vram is None else int(ca.args.reserve_vram * 1024 ** 3)
        try:
            control.init(simple_vram_headroom=headroom, nvml_pressure=not ca.args.disable_nvml_pressure)
        except TypeError:
            try:
                control.init(simple_vram_headroom=headroom)
            except TypeError:
                control.init()

        import comfy.memory_management as cmm
        import comfy.model_management as mm
        import comfy.model_patcher as mp

        if not (ca.args.enable_dynamic_vram or _dynamic_vram_supported()):
            return
        if not ca.args.enable_dynamic_vram and mm.torch_version_numeric < (2, 8):
            _LOG.warning(
                "DynamicVRAM needs PyTorch >= 2.8; keeping the legacy ModelPatcher"
            )
            return

        try:
            initialized = control.init_devices(
                (d.index, int(ca.args.vram_headroom * 1024 ** 3))
                for d in mm.get_all_torch_devices()
            )
        except TypeError:
            initialized = control.init_devices(
                d.index for d in mm.get_all_torch_devices()
            )

        if not initialized:
            _LOG.warning("comfy-aimdo unavailable; keeping the legacy ModelPatcher")
            return
        control.set_log_info()
        mp.CoreModelPatcher = mp.ModelPatcherDynamic
        cmm.aimdo_enabled = True
        _LOG.info("DynamicVRAM support detected and enabled")
    except Exception as exc:
        _LOG.warning("DynamicVRAM init failed (%s); keeping the legacy ModelPatcher", exc)


def load_dit(path=None, on_loaded=None):
    """Load the diffusion model. Returns (model, elapsed_seconds)."""
    prepare_environment()
    import comfy.sd

    path = path or config.DIT_MODEL_PATH
    start = time.time()
    vram_probe.stage("load_dit BEG")
    model = comfy.sd.load_diffusion_model(path)
    vram_probe.stage("load_dit END")
    elapsed = time.time() - start
    _notify(on_loaded, "dit", elapsed)
    return model, elapsed


def load_text_encoder(path=None, on_loaded=None):
    """Load the Qwen-Image-2.1 text encoder. Returns (clip, elapsed_seconds)."""
    prepare_environment()
    import comfy.sd

    path = path or config.TEXT_ENCODER_PATH
    start = time.time()
    vram_probe.stage("load_te BEG")
    clip = comfy.sd.load_clip([path], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    vram_probe.stage("load_te END")
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
    vram_probe.stage("load_vae BEG")
    state_dict = comfy.utils.load_torch_file(path)
    vae = comfy.sd.VAE(state_dict)
    vram_probe.stage("load_vae END")
    elapsed = time.time() - start
    _notify(on_loaded, "vae", elapsed)
    return vae, elapsed


def _notify(on_loaded, sub_stage, elapsed):
    if on_loaded is not None:
        fraction = STAGE_FRACTIONS[sub_stage][1]
        on_loaded(sub_stage, fraction, elapsed)
