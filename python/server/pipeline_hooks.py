"""Pre-sampling pipeline hooks for the ZIV.AI backend (Step 4).

Reserved seam for Python-internal transforms that must run after the model
triple is loaded and before sampling (LoRA, MagCache, ...). Hooks never change
the IPC contract (see ``DOC/OPTIMIZATION.md``): the C# side only says *what* to
do, the Python side decides *how*.

A hook is a callable ``(model, clip, params) -> (model, clip)``. Returning
``None`` leaves both unchanged. This is a registration point only; no concrete
optimization is implemented here (anti-over-engineering, Step 4 scope).
"""

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
