"""Qwen-Image-2.1 image-EDIT 6-step distilled acceleration (batch 3 plugin).

Capability ``sampling_plan`` (see DOC/INTERFACES.md §38): accelerates the maskless
image-EDIT path with the viggle-turbo v0.2.1 6-step distilled LoRA. It supplies:

- a patched ``model`` (the LoRA as a runtime side branch ``y = Wx + BAx``, never fused
  into the int8 weights: round-to-nearest / stochastic requantization is lossy here),
- a ``sigmas`` schedule built from the latent size (diffusers
  ``QwenImage21Pipeline(sigmas=nodes)``: FlowMatchEulerDiscreteScheduler with dynamic
  exponential shift, ``mu = calculate_shift(tokens, 256, 8192, 0.5, 0.9)``, final 0),
- ``skip_shift=True`` so the pipeline does not apply ModelSamplingAuraFlow on top.

Algorithm ported faithfully from the authoritative custom node
``viggle_turbo.py`` (ViggleTurboSigmas + ViggleTurboLora, incl. the int8 fused-MLP
``gate_up`` / ``out`` side branch, the device move of the LoRA tensors, and hook
removal in ``finally``).

Import-time side-effect free: torch / comfy are imported lazily inside the callables,
so loading this plugin never touches the heavy stack (loader contract).
"""

import json
import logging
import math
import os

PLUGIN_META = {
    "id": "qwen21-viggle-6step",
    "display_name": "Qwen-Image-2.1 Viggle Turbo 6-Step (edit)",
    "version": "0.1.0",
    "capabilities": ["sampling_plan"],
}

_LOG = logging.getLogger("zivai.server")

# LoRA registry id (Template/loras.json); resolved to a weight path via `loras`.
_LORA_ID = "qwen21-viggle-turbo-6step"
# Only the Qwen-Image-2.1 DiT has these LoRA targets / the 64ch /16 latent this schedule
# assumes; any other model id declines (an empty / absent id stays the registry default).
_SUPPORTED_MODEL_IDS = ("qwen-image-2.1",)
# viggle-turbo v0.2.1 student nodes (raw, unshifted). 6 nodes -> 6 sampling steps.
_SIGMA_NODES = (1.0, 0.9375, 0.875, 0.75, 0.5, 0.25)
_WRAPPER_KEY = "viggle_turbo_lora"
_SAMPLER_NAME = "euler"


def _lora_fwd(x, ab):
    import torch.nn.functional as F

    return F.linear(F.linear(x, ab[0].to(x.dtype)), ab[1].to(x.dtype))


def _add_hook(mod, ab):
    return mod.register_forward_hook(lambda m, inp, out: out + _lora_fwd(inp[0], ab))


def _add_mlp_hooks(mlp, gate, up, down):
    # Fused SwiGLU: gate_up = [gate_layer; proj], and `out` runs inside an int8/fp16
    # kernel that bypasses its hooks, so its branch is added to the MLP output from the
    # (LoRA'd) gate_up output.
    import torch
    import torch.nn.functional as F

    state = {}

    def gate_up_hook(m, inp, out):
        state["gu"] = out + torch.cat(
            [_lora_fwd(inp[0], gate), _lora_fwd(inp[0], up)], -1
        )
        return state["gu"]

    def mlp_hook(m, inp, out):
        g, u = state.pop("gu").chunk(2, -1)
        return out + _lora_fwd(F.silu(g) * u, down)

    return [
        mlp.gate_up.register_forward_hook(gate_up_hook),
        mlp.register_forward_hook(mlp_hook),
    ]


def _run_with_lora(lora, executor, *args, **kwargs):
    dm = executor.class_obj
    for ab in lora.values():
        if ab[0].device != args[0].device:
            ab[0], ab[1] = ab[0].to(args[0].device), ab[1].to(args[0].device)
    hooks = []
    for name, ab in lora.items():
        parent, _, leaf = name.rpartition(".")
        if not getattr(dm.get_submodule(parent), "fused", False):
            hooks.append(_add_hook(dm.get_submodule(name), ab))
        elif leaf == "out":
            hooks += _add_mlp_hooks(
                dm.get_submodule(parent),
                lora[parent + ".gate_layer"],
                lora[parent + ".proj"],
                ab,
            )
    try:  # the diffusion model is shared with other MODEL outputs; hooks must not outlive this call
        return executor(*args, **kwargs)
    finally:
        for hook in hooks:
            hook.remove()


def _load_lora(path, strength=1.0):
    """Load the diffusers-format LoRA as ``{module_name: [A, B * scale]}`` (CPU)."""
    import comfy.utils

    sd, meta = comfy.utils.load_torch_file(
        path, safe_load=True, return_metadata=True
    )
    cfg = json.loads((meta or {}).get("lora_adapter_metadata", "{}"))
    scale = strength * cfg.get("transformer.lora_alpha", 1) / cfg.get("transformer.r", 1)
    return {
        k.removeprefix("transformer.").removesuffix(".lora_A.weight"): [
            sd[k],
            sd[k.replace("lora_A", "lora_B")] * scale,
        ]
        for k in sd
        if k.endswith(".lora_A.weight")
    }


def _build_sigmas(latent):
    """Pipeline sigmas from the sampling latent's token count (viggle-turbo schedule)."""
    import torch

    tokens = round(latent.shape[-2]) * round(latent.shape[-1])
    mu = 0.5 + (0.9 - 0.5) * (tokens - 256) / (8192 - 256)
    nodes = torch.tensor([float(x) for x in _SIGMA_NODES], dtype=torch.float64)
    sigmas = math.exp(mu) / (math.exp(mu) + (1 / nodes - 1))
    return torch.cat([sigmas, sigmas.new_zeros(1)]).float()


def _install_model(model, lora):
    """Return ``(patched_model, cleanup)``; ``cleanup`` removes the wrapper in ``finally``."""
    import comfy.patcher_extension

    patched = model.clone()
    patched.add_wrapper_with_key(
        comfy.patcher_extension.WrappersMP.DIFFUSION_MODEL,
        _WRAPPER_KEY,
        lambda executor, *a, **kw: _run_with_lora(lora, executor, *a, **kw),
    )

    def cleanup():
        try:
            patched.remove_wrappers_with_key(
                comfy.patcher_extension.WrappersMP.DIFFUSION_MODEL, _WRAPPER_KEY
            )
        except Exception as exc:  # noqa: BLE001 - cleanup must never fail the task
            _LOG.warning("qwen21-viggle-6step: wrapper cleanup failed (%s)", exc)

    return patched, cleanup


def sampling_plan(context):
    """Capability entry: accelerate a maskless image edit, else decline (``None``).

    Engages only for ``op == "inpaint"`` with a source image, no mask and
    ``denoise == 1.0`` (a full-strength edit). Outpaint / t2i / masked / partial-denoise
    requests always decline, so those paths stay behaviorally unchanged.

    Note: the predicate does NOT inspect ``additional_images`` (multi-image control) or
    whether the request already applied a LoRA through ``pipeline_hooks`` — enabling the
    plugin stacks this 6-step distill LoRA / schedule on top of those. It is opt-in and
    off by default, so the interaction is documented rather than auto-excluded.
    """
    if not isinstance(context, dict):
        return None
    if str(context.get("op") or "").strip().lower() != "inpaint":
        return None
    model_id = str(context.get("model_id") or "").strip().lower()
    if model_id and model_id not in _SUPPORTED_MODEL_IDS:
        return None
    if not context.get("image_path"):
        return None
    if context.get("mask") is not None:
        return None
    try:
        if not math.isclose(float(context.get("denoise")), 1.0, rel_tol=1e-9, abs_tol=0.0):
            return None
    except (TypeError, ValueError):
        return None

    latent = context.get("latent")
    model = context.get("model")
    if latent is None or model is None:
        return None

    try:
        import loras

        lora_path = loras.resolve_path(_LORA_ID)
    except Exception as exc:  # noqa: BLE001 - never fatal
        _LOG.warning("qwen21-viggle-6step: LoRA registry unavailable (%s); declining", exc)
        return None
    if not lora_path or not os.path.isfile(lora_path):
        _LOG.warning(
            "qwen21-viggle-6step: LoRA unresolved/missing (%s); declining", lora_path
        )
        return None

    try:
        lora = _load_lora(lora_path)
        sigmas = _build_sigmas(latent)
        patched, cleanup = _install_model(model, lora)
    except Exception as exc:  # noqa: BLE001 - degrade to the legacy path
        _LOG.warning("qwen21-viggle-6step: setup failed (%s); declining", exc)
        return None

    return {
        "model": patched,
        "skip_shift": True,
        "sigmas": sigmas,
        "sampler_name": _SAMPLER_NAME,
        "steps": len(_SIGMA_NODES),
        "cleanup": cleanup,
    }
