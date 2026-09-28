"""batch-3 capability seam for the sampling stage (split out of ``pipeline.py``, Z8).

Builds the read-only plugin context, resolves a ``sampling_plan``, and runs the sampler
with the plan's explicit sigmas when one is supplied. Every plugin reply is normalised
here, so a malformed plugin can never raise inside the pipeline.

CPU-only at import: ``plugins.dispatch`` and torch / comfy are imported lazily (inside
the functions), matching the loader contract that importing a plugin never touches the
heavy stack.
"""

import logging

import config

_LOG = logging.getLogger("zivai.server")


def build_context(*, op, model, clip, vae, latent, mask, prompt, image_path, mask_path,
                  steps, denoise, seed, cfg, sampler_preset, model_id):
    """Read-only context handed to a plugin's ``sampling_plan`` capability."""
    return {
        "op": op,
        "model": model,
        "clip": clip,
        "vae": vae,
        "latent": latent,
        "mask": mask,
        "prompt": prompt,
        "image_path": image_path,
        "mask_path": mask_path,
        "steps": steps,
        "denoise": denoise,
        "seed": seed,
        "cfg": cfg,
        "sampler_preset": sampler_preset,
        "model_id": model_id,
    }


def resolve_plan(context):
    """First non-None enabled-plugin ``sampling_plan``, normalised to a dict or ``None``.

    A capability may decline (``None``); anything else must be a plan dict, or a malformed
    plugin return would raise on ``plan.get(...)`` downstream, outside any guard.
    """
    from plugins import dispatch

    plan = dispatch.call("sampling_plan", context)
    return plan if isinstance(plan, dict) else None


def plan_sigmas(plan):
    """The plan's explicit sigmas, or ``None`` for the legacy scheduler path."""
    return plan.get("sigmas") if isinstance(plan, dict) else None


def plan_steps(plan, default):
    """The step count to report for progress (a plan may pin its own, e.g. 6)."""
    if not isinstance(plan, dict) or not plan.get("steps"):
        return default
    try:
        return max(1, int(plan["steps"]))
    except (TypeError, ValueError):
        return default


def plan_model(plan, model):
    """The plan's patched model, or the original when the plan ships a falsy one."""
    if not isinstance(plan, dict):
        return model
    return plan.get("model") or model


def applies_shift(plan, sigmas):
    """True when the pipeline must still apply ``ModelSamplingAuraFlow`` for this plan.

    Fail-safe: a plan that ships sigmas but omits ``skip_shift`` defaults to skipping the
    shift, because the sigmas already bake in their own schedule (no double shift).
    """
    if not isinstance(plan, dict):
        return True
    if "skip_shift" in plan:
        return not bool(plan.get("skip_shift"))
    return sigmas is None


def sample(*, plan, sigmas, model, noise, positive, negative, latent, mask, seed,
           callback, steps, denoise, cfg, sampler, legacy_sample):
    """Run one sampler pass: the plan's sigmas via ``sample_custom``, else the legacy path."""
    if sigmas is not None:
        import comfy.sample
        import comfy.samplers

        return comfy.sample.sample_custom(
            model,
            noise,
            plan.get("cfg", cfg),
            comfy.samplers.sampler_object(plan.get("sampler_name") or config.SAMPLER_NAME),
            sigmas,
            positive,
            negative,
            latent,
            noise_mask=mask,
            callback=callback,
            disable_pbar=True,
            seed=seed,
        )
    return legacy_sample(
        model, positive, negative, latent, noise, steps, denoise, mask, seed, callback,
        sampler_name=sampler.get("sampler_name"),
        scheduler=sampler.get("scheduler"),
        cfg=cfg,
    )


def cleanup(plan):
    """Run a plan's ``cleanup`` callable; never raises.

    ``run()`` retries ``_run_once`` per resolution fallback, so the plugin's model hooks
    must be torn down here even when the sampler raised (OOM) — no leak / double-apply.
    """
    if not isinstance(plan, dict):
        return
    fn = plan.get("cleanup")
    if not callable(fn):
        return
    try:
        fn()
    except Exception as exc:  # noqa: BLE001 - cleanup must never fail the task
        _LOG.warning("plugin cleanup failed: %s", exc)


def sample_from(ctx, noise, positive, negative, latent, mask, seed, callback, steps,
                denoise, legacy_sample):
    """S3：在接缝 ctx 上执行一次采样（``pipeline`` 的 ``before_sample`` 执行面）。

    等价旧的 ``sample(plan=…, sigmas=…, cfg=cfg, sampler=sampler)``：ctx 已由 ``seams.apply`` 施力并
    归一（``sigmas`` 合法或 None、``steps`` 归一），故这里只做 ``plan = ctx if sigmas else None`` 的
    复自适应并转发 :func:`sample`。``cfg`` / ``sampler`` 取自 ctx（旧路径同源）。
    """
    sigmas = ctx.get("sigmas")
    return sample(
        plan=ctx if sigmas is not None else None, sigmas=sigmas, model=ctx.get("model"),
        noise=noise, positive=positive, negative=negative, latent=latent, mask=mask,
        seed=seed, callback=callback, steps=steps, denoise=denoise, cfg=ctx.get("cfg"),
        sampler=ctx.get("sampler_preset") or {}, legacy_sample=legacy_sample,
    )


def report_steps(ctx, default):
    """S3：接缝 ctx 的进度步数（旧 ``plan_steps(plan, steps)`` 的 ctx 版）。"""
    return plan_steps(ctx if ctx.get("sigmas") is not None else None, default)


def cleanup_ctx(ctx):
    """S3：接缝 ctx 的收尾（旧 ``cleanup(plan)`` 的 ctx 版）：单个 ``cleanup`` + 聚合列表，永不抛。"""
    from seams import collect_cleanup

    for fn in collect_cleanup(ctx):
        try:
            fn()
        except Exception as exc:  # noqa: BLE001 - cleanup must never fail the task
            _LOG.warning("plugin cleanup failed: %s", exc)
