"""通用插件 capability 调度（batch 3 · 纯 CPU，无 torch / comfy 导入）。

插件导出一个模块级可调用对象即视为暴露该 capability（批 3 已接线：``sampling_plan``）。
本模块提供：

- :func:`active_plugins`：注册表 ∩ 已启用 ∩ 可加载 的插件模块（按注册表顺序）；
- :func:`call`：遍历启用插件，返回**首个非 None** 的 capability 结果（legacy / 单次接管）；
- :func:`_seams_for` / :func:`call_chain`：**接缝系统**（S2）——按接缝名链式调用，patch 累积。

任何失败（导入 / 调用 / 返回值）都降级为 warning + 跳过，**永不抛异常**：插件绝不能拖垮任务。
纯 stdlib + :mod:`plugins.loader`，import 时无副作用（不 import torch / comfy）。
"""

import logging

from . import loader

_LOG = logging.getLogger("zivai.server")


def active_plugins():
    """返回当前启用且能加载的 ``(plugin_id, module)`` 列表（注册表顺序）。

    读注册表 → 逐个判启用（env > ``enabled_by_default``）→ import-by-file。加载失败 / 缺目录的
    启用插件记一条 warning 后跳过，不打断其余插件。
    """
    registry = loader.load_registry()
    active = []
    for plugin_id, entry in registry.items():
        if not loader.enabled(plugin_id, entry):
            continue
        module, status = loader.load_plugin(plugin_id)
        if module is None:
            _LOG.warning("plugin %r enabled but not loaded (%s); skipped", plugin_id, status)
            continue
        active.append((plugin_id, module))
    return active


def call(capability, context):
    """调用启用插件暴露的 ``capability``，返回首个非 None 结果（无则 None）。

    ``capability`` 是模块级可调用对象名（如 ``"sampling_plan"``）；未暴露该名字的插件跳过。
    插件抛异常时记 warning 并继续，绝不让插件错误冒泡到管线。
    """
    for plugin_id, module in active_plugins():
        fn = getattr(module, capability, None)
        if not callable(fn):
            continue
        try:
            result = fn(context)
        except Exception as exc:  # noqa: BLE001 - 插件错误必须被隔离
            _LOG.warning(
                "plugin %r capability %r failed (%s); skipped",
                plugin_id,
                capability,
                exc,
            )
            continue
        if result is not None:
            return result
    return None


# 旧 capability 名 → 接缝名（D6）。与 ``seams.LEGACY_CAPABILITY_MAP`` 同义；此处保留一份，
# 避免 dispatch（包内）在上层顶层 import seams 造成循环依赖。
_LEGACY_CAPABILITY_MAP = {"sampling_plan": "before_sample"}

# 旧函数名回退表：接缝名 → 旧函数名。新插件函数名 = 接缝名（D7）；旧插件只导出旧名时回退。
_LEGACY_FN_BY_SEAM = {"before_sample": "sampling_plan"}

# 插件在各接缝的 patch 里可用 ``cleanup`` 键注册收尾回调（callable）。链上多个插件的 cleanup
# 会被聚合进 ``ctx[CLEANUPS_KEY]`` 列表（D9），避免 ``update()`` 相互覆盖（复核 P1）。
# 键名须与 ``seams.CLEANUPS_KEY`` 一致（单测断言两者相等）。
CLEANUPS_KEY = "cleanups"


def _as_str_list(value):
    return [x.strip() for x in value if isinstance(x, str) and x.strip()] if isinstance(value, list) else []


def _seams_for(plugin_id, module, entry=None):
    """解析某插件挂哪些接缝（D2 / D10）。

    优先级（对**同一条** registry entry 做字段级回退；用户覆盖已由 loader 整条目合并完成）：
      1. ``entry["seams"]`` —— 数据声明（``plugins.json`` / ``plugins.user.json``，D2）；
      2. ``entry["capabilities"]`` —— 原始 capability 名，经 :data:`_LEGACY_CAPABILITY_MAP` 映射；
    两者都不中 → 空列表（插件不挂任何接缝）。``PLUGIN_META`` **不读**（D2：seams 只放数据文件）。

    ``entry`` 由调用方（:func:`call_chain`）一次性传入，避免每插件重读注册表（复核 P1.2）；
    缺省（``None``）时才自行读取，供独立测试 / 外部调用。

    ``module`` 当前**未参与解析**（D2 明确不读 ``PLUGIN_META.seams``）；保留以固定签名 / 供后续
    需要模块级信息的解析实现。
    """
    if entry is None:
        entry = loader.load_registry().get(plugin_id)
    if not isinstance(entry, dict):
        return []

    declared = _as_str_list(entry.get("seams"))
    if declared:
        return declared

    mapped = []
    for cap in _as_str_list(entry.get("capabilities")):
        seam = _LEGACY_CAPABILITY_MAP.get(cap)
        if seam and seam not in mapped:
            mapped.append(seam)
    return mapped


def _fn_for(module, seam):
    """取插件在接缝 ``seam`` 上的可调用对象：先按接缝名，再回退旧函数名（D6/D7）。"""
    fn = getattr(module, seam, None)
    if callable(fn):
        return fn
    legacy = _LEGACY_FN_BY_SEAM.get(seam)
    if legacy:
        fn = getattr(module, legacy, None)
        if callable(fn):
            return fn
    return None


def call_chain(seam, context):
    """按注册表顺序在接缝 ``seam`` 上**链式**调用插件，返回累积 patch 后的 ctx（D3）。

    - 顺序 = ``plugins.json`` 数组顺序（``active_plugins`` 保序），不做 priority。
    - 每个插件解析其接缝（:func:`_seams_for`）；不挂该接缝的跳过。
    - 传给插件的是 **ctx 副本**（防插件就地改）；插件返回 dict patch 则并入累积 ctx，下一个插件
      看到更新后的 ctx。
    - patch 里的 ``cleanup``（callable）不覆盖，而是**聚合**进 ``ctx[CLEANUPS_KEY]`` 列表（D9）。
    - 插件抛异常 / 返回非 dict → warning / 跳过，**永不抛**（与 :func:`call` 同隔离策略）。
    - **不就地修改调用方的 ctx**：入口先复制一份再累积（复核 P2.2）。

    本函数**不做白名单过滤**（那是 :func:`seams.apply` 的 reducer 职责）；返回值是「原始累积」，
    契约调用方应经 ``seams.apply`` 使用，而非直接信任本函数结果。
    """
    context = dict(context)
    registry = loader.load_registry()
    for plugin_id, module in active_plugins():
        entry = registry.get(plugin_id)
        if seam not in _seams_for(plugin_id, module, entry):
            continue
        fn = _fn_for(module, seam)
        if not callable(fn):
            _LOG.warning(
                "plugin %r declares seam %r but exposes no callable; skipped",
                plugin_id,
                seam,
            )
            continue
        try:
            patch = fn(dict(context))
        except Exception as exc:  # noqa: BLE001 - 插件错误必须被隔离
            _LOG.warning(
                "plugin %r seam %r failed (%s); skipped", plugin_id, seam, exc
            )
            continue
        if not isinstance(patch, dict):
            continue
        cleanup = patch.get("cleanup")
        if callable(cleanup):
            bucket = context.get(CLEANUPS_KEY)
            if not isinstance(bucket, list):
                bucket = context[CLEANUPS_KEY] = []
            bucket.append(cleanup)
            patch = {k: v for k, v in patch.items() if k != "cleanup"}
        context.update(patch)
    return context
