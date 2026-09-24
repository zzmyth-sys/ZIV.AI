"""Pre-sampling pipeline hooks for the ZIV.AI backend (Step 4).

Reserved seam for Python-internal transforms that must run after the model
triple is loaded and before sampling (LoRA, MagCache, ...). Hooks never change
the IPC contract (see ``DOC/OPTIMIZATION.md``): the C# side only says *what* to
do, the Python side decides *how*.

A hook is a callable ``(model, clip, params) -> (model, clip)``. Returning
``None`` leaves both unchanged.
"""

import logging

_LOG = logging.getLogger("zivai.server")

_pre_sampling_hooks = []
_applied = 0


def register_pre_sampling_hook(fn):
    """Register a pre-sampling hook; returns the hook for convenience."""
    if not callable(fn):
        raise ValueError("pre-sampling hook must be callable")
    _pre_sampling_hooks.append(fn)
    return fn


def clear_pre_sampling_hooks():
    """Drop all registered hooks (called once per submit)."""
    _pre_sampling_hooks[:] = []


def pre_sampling_hook_count():
    return len(_pre_sampling_hooks)


def applied_hook_count():
    return _applied


def apply_pre_sampling_hooks(model, clip, params):
    """Run every registered hook in order; returns the (possibly new) pair."""
    global _applied
    for hook in list(_pre_sampling_hooks):
        result = hook(model, clip, params)
        _applied += 1
        if result is not None:
            model, clip = result
    return model, clip


def make_lora_hook(lora_path, strength_model=1.0, strength_clip=1.0):
    """Build a pre-sampling hook that applies a LoRA to ``(model, clip)`` (Step 8-1).

    ComfyUI / torch are imported lazily (they only exist in the inference process).
    A missing package or a failed load logs a warning and leaves the pair unchanged,
    so a bad LoRA never fails the whole task.

    NOTE: the real load path is delivered as code but is **not GPU-verified** (Z29/Z30).
    """
    def hook(model, clip, params):
        if not lora_path:
            return model, clip

        try:
            import comfy.sd
            import comfy.utils
        except Exception as exc:  # pragma: no cover - exercised on the GPU host
            _LOG.warning("LoRA requested but ComfyUI is unavailable: %s", exc)
            return model, clip

        # Step 8-2: strengths are nullable upstream; preserve an explicit 0, default absent to 1.0.
        model_strength = 1.0 if strength_model is None else float(strength_model)
        clip_strength = 1.0 if strength_clip is None else float(strength_clip)
        try:
            lora = comfy.utils.load_torch_file(lora_path, safe_load=True)
            model, clip = comfy.sd.load_lora_for_models(
                model, clip, lora, model_strength, clip_strength
            )
            _LOG.info(
                "applied LoRA %s (model=%.2f, clip=%.2f)",
                lora_path,
                model_strength,
                clip_strength,
            )
        except Exception as exc:  # pragma: no cover - exercised on the GPU host
            _LOG.warning("LoRA apply failed (%s): %s", lora_path, exc)
        return model, clip

    return hook
