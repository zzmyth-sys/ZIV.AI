"""Instrumentation hooks for the ZIV.AI repro (kept separate; no repo edits)."""


def _shape(x):
    try:
        import torch

        if isinstance(x, torch.Tensor):
            return tuple(x.shape)
    except Exception:
        pass
    return getattr(x, "shape", type(x).__name__)


def _refs(cond):
    try:
        d = cond[0][1] if isinstance(cond, list) else None
        if isinstance(d, dict):
            r = d.get("reference_latents")
            if isinstance(r, list):
                return [tuple(t.shape) for t in r]
    except Exception as exc:  # noqa: BLE001
        return "err:%s" % exc
    return None


def install(pipeline, seams, plugin_sampling):
    # --- encode ---
    orig_encode = pipeline.encode_prompt

    def encode(*args, **kwargs):
        pos, neg, lat, mask = orig_encode(*args, **kwargs)
        print(
            "[repro] encode: latent=%s mask=%s refs=%s"
            % (_shape(lat), None if mask is None else _shape(mask), _refs(pos)),
            flush=True,
        )
        return pos, neg, lat, mask

    pipeline.encode_prompt = encode

    # --- seam force ---
    orig_apply = seams.apply

    def apply(anchor, ctx):
        out = orig_apply(anchor, ctx)
        if anchor == "before_sample":
            sig = out.get("sigmas")
            print(
                "[repro] seam before_sample: steps=%s sigmas=%s model=%s cleanups=%d "
                "skip_shift=%s"
                % (
                    out.get("steps"),
                    _shape(sig) if sig is not None else None,
                    type(out.get("model")).__name__,
                    len(out.get("cleanups") or []),
                    out.get("skip_shift"),
                ),
                flush=True,
            )
        return out

    seams.apply = apply

    # --- sampler path ---
    orig_sf = plugin_sampling.sample_from

    def sample_from(ctx, **kwargs):
        sig = ctx.get("sigmas")
        print(
            "[repro] sample_from: path=%s sigmas=%s steps=%s denoise=%s"
            % (
                "CUSTOM" if sig is not None else "LEGACY",
                _shape(sig) if sig is not None else None,
                kwargs.get("steps"),
                kwargs.get("denoise"),
            ),
            flush=True,
        )
        return orig_sf(ctx, **kwargs)

    plugin_sampling.sample_from = sample_from

    # --- plugin enable sanity ---
    try:
        from plugins import loader

        mod, status = loader.load_plugin("qwen21-viggle-6step")
        print(
            "[repro] plugin qwen21-viggle-6step: loaded=%s status=%s sampling_plan=%s"
            % (mod is not None, status, hasattr(mod, "sampling_plan")),
            flush=True,
        )
    except Exception as exc:  # noqa: BLE001
        print("[repro] plugin load check failed: %s" % exc, flush=True)

    print("[repro] instrumentation installed", flush=True)
