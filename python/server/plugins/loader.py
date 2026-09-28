"""统一插件加载器（batch 1 · 纯 CPU，无 torch / comfy 导入）。

插件 = ``Template/plugins.json`` 里登记的一个 Python 包目录。本模块负责：读注册表、解析目录、
检查声明依赖、以及用唯一模块名把插件 import-by-file 加载进来（与 TE-Speed 同手法）。任何失败都
降级为 ``None`` + warning，插件缺失 / 损坏绝不失败任务。

约定（「插件标准形式」见 DOC/INTERFACES.md）：
- 目录 + 入口文件（默认 ``__init__.py``，可由 entry 覆盖）；
- 以 ``spec_from_file_location`` 独立命名空间导入，import 时无副作用；
- 依赖只在 registry 的 ``deps`` 里声明（Python import 名），本模块只检查、不安装。
"""

import importlib.util
import json
import logging
import os
import sys

import config

_LOG = logging.getLogger("zivai.server")

# 已加载模块，按插件 id 缓存；执行插件代码代价高且可安全复用。
_MODULES = {}

# 用户覆盖文件：与内置 ``plugins.json`` 同目录，整条目覆盖（见 :func:`load_registry`）。
USER_FILE_NAME = "plugins.user.json"


def _read_entries(registry_path):
    """读一个注册表文件为 ``[(id, entry), ...]``（保序）；缺失 / 损坏 → ``[]``。

    与 :func:`load_registry` 共用同一解析规则；返回列表（而非 dict）以保留声明顺序。
    """
    if not registry_path or not os.path.isfile(registry_path):
        return []

    try:
        with open(registry_path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, ValueError) as exc:
        _LOG.warning("plugins registry unreadable (%s): %s", registry_path, exc)
        return []

    entries = data.get("plugins") if isinstance(data, dict) else None
    if not isinstance(entries, list):
        return []

    parsed = []
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        plugin_id = entry.get("id")
        if isinstance(plugin_id, str) and plugin_id.strip():
            parsed.append((plugin_id.strip(), entry))
    return parsed


def load_registry(path=None):
    """读注册表为 ``{id: entry}``；缺失 / 损坏 → ``{}``（镜像 :func:`models.load_registry`，不抛）。

    默认（``path is None``）：读内置 ``config.PLUGINS_REGISTRY_PATH``，再合并同目录的
    ``plugins.user.json``。合并 = **整条目覆盖**（用户条目胜出，**不**继承内置字段）+ 新 id 追加在
    内置之后（按用户声明顺序）。**内置文件缺失 / 损坏 → ``{}``，且不读用户文件**（内置是权威；
    缺内置时用户覆盖无意义，避免半套注册表）。

    显式传入 ``path``（测试 / 调用方指定）：只读该文件，**不**合并同目录用户文件。
    """
    if path is not None:
        return dict(_read_entries(path))

    built_in = _read_entries(config.PLUGINS_REGISTRY_PATH)
    if not built_in:
        if not config.PLUGINS_REGISTRY_PATH or not os.path.isfile(
            config.PLUGINS_REGISTRY_PATH
        ):
            _LOG.warning("plugins registry not found: %s", config.PLUGINS_REGISTRY_PATH)
        return {}

    user_path = os.path.join(
        os.path.dirname(config.PLUGINS_REGISTRY_PATH), USER_FILE_NAME
    )
    user = _read_entries(user_path)

    registry = dict(built_in)
    for plugin_id, entry in user:
        registry[plugin_id] = entry
    return registry


def resolve_dir(entry):
    """插件目录的绝对路径：env ``ZIV_AI_PLUGIN_<ID>_DIR`` > ``entry['dir']``（相对程序目录）。

    相对路径以 ``config.PLUGINS_BASE_DIR`` 为基准（App 运行时 = C# 注入的程序目录；独立运行 =
    仓库根 / 发布后的程序目录），与 C# ``PluginRegistry.ResolveDirectory`` 对齐；绝对路径原样返回。
    无效 / 缺失 → 空串。
    """
    plugin_id = entry.get("id") if isinstance(entry, dict) else None
    if plugin_id:
        override = os.environ.get(config.plugin_env_name(plugin_id) + "_DIR", "").strip()
        if override:
            return os.path.abspath(override)

    directory = entry.get("dir") if isinstance(entry, dict) else None
    if not isinstance(directory, str) or not directory.strip():
        return ""
    directory = directory.strip()
    if os.path.isabs(directory):
        return os.path.abspath(directory)
    return os.path.abspath(os.path.join(config.PLUGINS_BASE_DIR, directory))


def enabled(plugin_id, entry=None):
    """插件是否启用：env 优先，否则注册表 ``enabled_by_default``（读注册表当 ``entry`` 缺省）。"""
    if entry is None:
        entry = load_registry().get(plugin_id)
    return config.plugin_enabled(plugin_id, entry)


def check_deps(entry):
    """返回声明了但无法 import 的依赖名（空列表 = 全部满足）。只检查，不安装。"""
    deps = entry.get("deps") if isinstance(entry, dict) else None
    if not isinstance(deps, list):
        return []

    missing = []
    for dep in deps:
        if not isinstance(dep, str) or not dep.strip():
            continue
        name = dep.strip()
        try:
            found = importlib.util.find_spec(name)
        except (ImportError, ValueError):
            found = None
        if found is None:
            missing.append(name)
    return missing


def _module_name(plugin_id):
    normalized = "".join(
        ch if (ch.isascii() and ch.isalnum()) else "_" for ch in (plugin_id or "")
    ).lower()
    return "zivai_plugin_" + normalized


def load_plugin(plugin_id):
    """加载插件包，返回 ``(module | None, status)``；**永不抛异常**。"""
    cached = _MODULES.get(plugin_id)
    if cached is not None:
        return cached, "loaded"

    entry = load_registry().get(plugin_id)
    if entry is None:
        _LOG.warning("unknown plugin id %r; not loaded", plugin_id)
        return None, "unknown_plugin"

    directory = resolve_dir(entry)
    if not directory or not os.path.isdir(directory):
        _LOG.warning("plugin %r dir missing: %s", plugin_id, directory)
        return None, "missing_dir"

    entry_file = entry.get("entry") or "__init__.py"
    init_py = os.path.join(directory, entry_file)
    if not os.path.isfile(init_py):
        _LOG.warning("plugin %r entry missing: %s", plugin_id, init_py)
        return None, "missing_entry"

    name = _module_name(plugin_id)
    try:
        spec = importlib.util.spec_from_file_location(
            name, init_py, submodule_search_locations=[directory]
        )
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        spec.loader.exec_module(module)
    except Exception as exc:  # pragma: no cover - 由损坏插件触发
        _LOG.warning("plugin %r failed to load (%s); skipped", plugin_id, exc)
        sys.modules.pop(name, None)
        return None, "load_failed:%s" % (type(exc).__name__,)

    _MODULES[plugin_id] = module
    return module, "loaded"
