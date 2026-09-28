"""通用插件 capability 调度（batch 3 · 纯 CPU，无 torch / comfy 导入）。

插件导出一个模块级可调用对象即视为暴露该 capability（批 3 已接线：``sampling_plan``）。
本模块只做两件事：

- :func:`active_plugins`：注册表 ∩ 已启用 ∩ 可加载 的插件模块（按注册表顺序）；
- :func:`call`：遍历启用插件，返回**首个非 None** 的 capability 结果。

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
