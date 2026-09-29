"""数据驱动接缝系统（seam system）· S1 地基（纯 CPU，无 torch / comfy 顶层 import）。

「接缝」= 管线里数据流输入 / 输出明确的固定锚点。插件在某个接缝上注册一个与接缝**同名**的
模块级可调用对象（如 ``before_encode(ctx)``），返回一个 **patch dict**；本模块的 reducer 把
patch 按**白名单**并入 ctx，超出白名单的键被忽略，让插件无法把管线改乱。

S1 建骨架：reducer 只做 patch 合并。S3 把 ``before_sample`` 的**施力**（AuraFlow ``patch_aura`` /
``sigmas`` 直采）从 ``pipeline.py`` 迁入对应 reducer；其余 5 个接缝仍只合并（S4），且**不 import**
torch / comfy（顶层保持 CPU-only，重型 import 延迟到函数内）。

契约（权威见 ``DOC/INTERFACES.md`` 尾部「接缝系统」段）：

- :data:`SEAMS`：6 个固定锚点（``before_save`` / ``after_save`` 明确不纳入）。
- :data:`LEGACY_CAPABILITY_MAP`：旧 capability 名 → 接缝名（``sampling_plan`` →
  ``before_sample``），长期保留（D6）。
- ``_apply_<seam>(ctx, patch) -> dict``：每接缝一个 reducer，白名单合并、返回新 ctx。
- :func:`apply`：**唯一对外入口**，也是**唯一白名单边界**（D9 / 复核 P2.1）。
- :func:`collect_cleanup`：从 ctx 抽出 ``cleanup`` 键供 ``_run_once`` 的 ``finally`` 逆序执行
  （D9）。

分层：本模块依赖 :mod:`plugins.dispatch`（提供链式调用 ``call_chain``）；为避开
``seams``（顶层）↔ ``plugins.dispatch``（包内）的循环 import，import 放在函数内延迟执行。
"""

import logging

import config
import sampling_policy

_LOG = logging.getLogger("zivai.server")

# 6 个固定接缝锚点（顺序即文档展示顺序，不表示执行优先级）。
SEAMS = (
    "before_encode",
    "after_encode",
    "before_sample",
    "after_sample",
    "before_decode",
    "after_decode",
)

# 旧 capability 名 → 新接缝名。长期保留（D6）：旧插件不改也能被路由到 before_sample。
LEGACY_CAPABILITY_MAP = {"sampling_plan": "before_sample"}

# 链上聚合的 cleanup 列表键（D9）。须与 ``plugins.dispatch.CLEANUPS_KEY`` 一致（单测断言）。
CLEANUPS_KEY = "cleanups"

# 每接缝允许被插件覆盖的键（白名单）。非白名单键一律忽略，防止插件乱写 ctx。
_WHITELIST = {
    "before_encode": ("prompt", "image_path", "mask_path", "additional_images", "spec", CLEANUPS_KEY),
    "after_encode": ("positive", "negative", "latent", "mask", CLEANUPS_KEY),
    "before_sample": (
        "model",
        "skip_shift",
        "sigmas",
        "sampler_name",
        "scheduler",
        "cfg",
        "steps",
        "cleanup",
        CLEANUPS_KEY,
    ),
    "after_sample": ("samples", CLEANUPS_KEY),
    "before_decode": ("vae", "samples", CLEANUPS_KEY),
    "after_decode": ("image", "width", "height", CLEANUPS_KEY),
}


def _merge(ctx, patch, allowed):
    """把 ``patch`` 中 ``allowed`` 白名单内的键并入 ``ctx`` 的**副本**；不改 ``ctx``。"""
    merged = dict(ctx)
    if not isinstance(patch, dict):
        return merged
    for key in allowed:
        if key in patch:
            merged[key] = patch[key]
    return merged


def _apply_before_encode(ctx, patch):
    return _merge(ctx, patch, _WHITELIST["before_encode"])


def _apply_after_encode(ctx, patch):
    return _merge(ctx, patch, _WHITELIST["after_encode"])


def _fit_sigmas(sigmas, steps):
    """校验 plan sigmas：合法（1-D、非空）原样返回；非法 → warning + ``None``（走 legacy 采样器）。

    采样步数由 ``len(sigmas)`` 决定（``sample_custom`` 语义），**不比对** ``steps``：diffusers 惯例是
    ``N`` 节点 sigmas + 末尾 0 = ``N+1`` 个元素（如 Viggle ``steps=6`` ↔ 7 个 sigmas），比对会误吞。
    ``steps`` 仅作进度显示（:func:`plugin_sampling.report_steps`），不参与本校验。
    """
    if sigmas is None:
        return None
    shape = getattr(sigmas, "shape", None)
    if shape is None or len(shape) != 1:
        _LOG.warning(
            "before_sample: sigmas is not a 1-D tensor (shape=%s); ignoring sigmas", shape
        )
        return None
    try:
        if int(shape[0]) <= 0:
            _LOG.warning("before_sample: sigmas is empty; ignoring sigmas")
            return None
    except (TypeError, ValueError):
        _LOG.warning("before_sample: sigmas length %r is not an int; ignoring sigmas", shape[0])
        return None
    return sigmas


def _extract_plugin_patch(ctx, working):
    """Heuristic for the plugin layer: whitelisted keys that ``working`` changed vs ``ctx``.

    ``dispatch.call_chain`` returns the **accumulated ctx** (not a sparse plugin patch), so a
    key counts as plugin-set only when it is new or differs from the ctx value. ``sampler_name``
    / ``scheduler`` are excluded on purpose (kept inert, matching the legacy path).
    """
    keys = ("model", "skip_shift", "sigmas", "cfg", "steps")
    out = {}
    for key in keys:
        if key not in working:
            continue
        if key not in ctx:
            out[key] = working[key]
            continue
        try:
            if working[key] != ctx[key]:
                out[key] = working[key]
        except Exception:  # noqa: BLE001 - a tensor / opaque == may not return a bool
            if working[key] is not ctx[key]:
                out[key] = working[key]
    return out


def _sampler_preset_to_model_entry(preset):
    """Map the resolved ``sampler_preset`` onto the policy's model_profile layer shape.

    Key rename ``type`` -> ``sampler_type``; ``None`` values are treated as "not declared".
    """
    if not isinstance(preset, dict):
        return None
    return {
        "sampler_type": preset.get("type"),
        "shift": preset.get("shift"),
        "sampler_name": preset.get("sampler_name"),
        "scheduler": preset.get("scheduler"),
        "cfg": preset.get("cfg"),
    }


def _apply_before_sample(ctx, patch):
    """S3：``before_sample`` reducer = 白名单合并 **+ 施力**（等价旧 ``pipeline`` 直调）。

    Step 2：决策（steps / cfg / sampler 块 / sigmas 让位 / AuraFlow shift）委托
    :func:`sampling_policy.resolve`；本 reducer 只保留**执行**（白名单合并、``patch_aura``
    调用、ctx 落地）。等价重构，行为与旧实现逐字节一致。
    """
    merged = _merge(ctx, patch, _WHITELIST["before_sample"])
    base_model = ctx.get("model")
    if not merged.get("model"):
        merged["model"] = base_model

    plugin_patch = _extract_plugin_patch(ctx, patch)
    raw_sigmas = plugin_patch.get("sigmas")
    if raw_sigmas is not None and _fit_sigmas(
        raw_sigmas, plugin_patch.get("steps", ctx.get("steps"))
    ) is None:
        plugin_patch = {key: value for key, value in plugin_patch.items() if key != "sigmas"}

    resolved = sampling_policy.resolve(
        model_entry=_sampler_preset_to_model_entry(ctx.get("sampler_preset")),
        payload={key: ctx[key] for key in ("steps",) if key in ctx},
        plugin_patch=plugin_patch,
    )

    merged["steps"] = resolved.steps
    merged["sigmas"] = resolved.sigmas
    merged["cfg"] = resolved.cfg

    new_preset = dict(ctx.get("sampler_preset") or {})
    new_preset["sampler_name"] = resolved.sampler_name
    new_preset["scheduler"] = resolved.scheduler
    new_preset["cfg"] = resolved.cfg
    if resolved.shift is None:
        new_preset.pop("shift", None)
    else:
        new_preset["shift"] = resolved.shift
    merged["sampler_preset"] = new_preset

    if resolved.shift is not None and new_preset.get("type") == "auraflow":
        from comfy_extras.nodes_model_advanced import ModelSamplingAuraFlow

        merged["model"] = ModelSamplingAuraFlow().patch_aura(merged["model"], resolved.shift)[0]
    return merged


def _apply_after_sample(ctx, patch):
    return _merge(ctx, patch, _WHITELIST["after_sample"])


def _apply_before_decode(ctx, patch):
    return _merge(ctx, patch, _WHITELIST["before_decode"])


def _apply_after_decode(ctx, patch):
    merged = _merge(ctx, patch, _WHITELIST["after_decode"])
    # 尺寸一致性：以 image.size 为准（复核 P2.3）。image 缺失时保留 patch / ctx 的 width/height。
    image = merged.get("image")
    size = getattr(image, "size", None)
    if size is not None and len(size) == 2:
        merged["width"], merged["height"] = size
    return merged


_APPLIERS = {
    "before_encode": _apply_before_encode,
    "after_encode": _apply_after_encode,
    "before_sample": _apply_before_sample,
    "after_sample": _apply_after_sample,
    "before_decode": _apply_before_decode,
    "after_decode": _apply_after_decode,
}


def apply(anchor, ctx):
    """在接缝 ``anchor`` 上跑链路并把 patch 施加到 ``ctx``，返回**新 ctx**。

    这是接缝系统的**唯一对外入口**，也是**唯一白名单边界**：``dispatch.call_chain`` 只做
    「按注册表顺序累积 patch（不含过滤）」，白名单由本函数对应的 reducer 保证。若调用方只想用
    低层原语，可自行调 ``dispatch.call_chain``，但那不是契约面。

    ``anchor`` 必须是 :data:`SEAMS` 之一；否则抛 ``ValueError``（**开发者错误**，不是插件错误，
    故不吞异常）。

    ``ctx`` 不被就地修改：先复制，再交给链路累积 patch，最后经 reducer 合并。
    """
    if anchor not in SEAMS:
        raise ValueError("unknown seam %r; expected one of %s" % (anchor, ", ".join(SEAMS)))

    from plugins import dispatch

    working = dict(ctx)
    working = dispatch.call_chain(anchor, working)
    return _APPLIERS[anchor](dict(ctx), working)


def collect_cleanup(ctx):
    """从 ``ctx`` 抽出 cleanup 回调返回列表；**不改** ``ctx``。

    - ``ctx[CLEANUPS_KEY]``（链上多插件聚合，D9）里的 callable 全部返回；
    - 兼容旧的单个 ``ctx["cleanup"]``（若为 callable 也返回）。
    两项都没有 → 空列表。``pipeline._run_once`` 在 ``finally`` 里**逆序**执行本列表，保证 OOM
    重试 / 采样异常时插件装的 side-branch 也被拆掉（D9）。
    """
    if not isinstance(ctx, dict):
        return []
    result = []
    single = ctx.get("cleanup")
    if callable(single):
        result.append(single)
    bucket = ctx.get(CLEANUPS_KEY)
    if isinstance(bucket, list):
        result.extend(fn for fn in bucket if callable(fn))
    return result