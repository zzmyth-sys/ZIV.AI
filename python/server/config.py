import configparser
import logging
import os
import sys

# ---- ComfyUI 源码树（in-process 管线的根）----
# 优先级：
#   1) env ZIV_AI_COMFY_ROOT（settings.ini [backend] comfy_root）。
#   2) 开发默认路径（GitHub 收尾步统一清理）。
#   3) 从 python_exe（= 正在运行的解释器 sys.executable）反推 <exe 目录>/../ComfyUI。
# 三者都无效时返回配置值 / 默认值；由 model_loader.prepare_environment 抛可读异常。
# 开发默认值：由本文件位置推导（<repo>/Comfyui/ComfyUI），不含机器绝对路径；
# 保持 A10 优先级链 env → 默认 → 反推 不变（开发机解析到同一目录）。
_HERE = os.path.dirname(os.path.abspath(__file__))
_COMFY_ROOT_DEV_DEFAULT = os.path.abspath(
    os.path.join(_HERE, os.pardir, os.pardir, "Comfyui", "ComfyUI")
)


def _resolve_comfy_root():
    configured = (os.environ.get("ZIV_AI_COMFY_ROOT") or "").strip()
    for candidate in (configured, _COMFY_ROOT_DEV_DEFAULT):
        if candidate and os.path.isdir(candidate):
            return candidate

    # 反推：标准 ComfyUI 便携版布局 <便携根>/python_embeded/python.exe + <便携根>/ComfyUI
    if sys.executable:
        derived = os.path.abspath(
            os.path.join(os.path.dirname(sys.executable), os.pardir, "ComfyUI")
        )
        if os.path.isdir(derived):
            return derived

    return configured or _COMFY_ROOT_DEV_DEFAULT


COMFY_ROOT = _resolve_comfy_root()

MODEL_ROOT = os.environ.get("ZIV_AI_MODEL_ROOT", "").strip() or (
    # A10 修订（2026-09-30）：未显式设置时由 ComfyUI 树推导 <comfy_root>/models；
    # 推导目录不存在则保留旧开发期兜底（默认模型源不因发现失败而丢失）。
    os.path.join(COMFY_ROOT, "models")
    if os.path.isdir(os.path.join(COMFY_ROOT, "models"))
    else r"C:\AI\ComfyUI_PIC\ComfyUI\models"  # 开发期兜底；生产建议用 env 或 models.json
)

DIT_MODEL_PATH = os.path.join(
    MODEL_ROOT, "diffusion_models", "image2", "qwen_image_2.1_int8_convrot.safetensors"
)
TEXT_ENCODER_PATH = os.path.join(
    MODEL_ROOT,
    "text_encoders",
    "qwen3vl_8b_int8_convrot.safetensors",
)
VAE_PATH = os.path.join(MODEL_ROOT, "vae", "qwen_image_2.1_vae_bf16.safetensors")

MODEL_NAME = "qwen-image-2.1"

PIPE_NAME = "zivai.infer.v1"
PIPE_PATH = r"\\.\pipe\zivai.infer.v1"

PROTOCOL_VERSION = "0.9"
BACKEND_VERSION = "0.4.0"

CONNECT_TIMEOUT_S = 15.0
MAX_FRAME_BYTES = 256 * 1024 * 1024

# ---- 推理默认值（Step 2.3；采样配置 Step 4 修正）----
# 目标分辨率（面积口径，保持输入纵横比）：Qwen-Image-2.1 推荐 1024。
# 可用环境变量覆盖（测试用；见 RESOLUTION_FALLBACK）。
MAX_RESOLUTION = int(os.environ.get("ZIV_AI_MAX_RESOLUTION", "1024"))
# 旧字段：保留以兼容，实际采样目标分辨率改由 MAX_RESOLUTION 决定。
DEFAULT_RESOLUTION = MAX_RESOLUTION
DEFAULT_STEPS = int(os.environ.get("ZIV_AI_DEFAULT_STEPS", "25"))
# OOM 降级：从 MAX_RESOLUTION 起，逐级回退到这些分辨率（面积口径）。
RESOLUTION_FALLBACK = [
    int(x)
    for x in os.environ.get("ZIV_AI_RESOLUTION_FALLBACK", "1024,768,640").split(",")
    if x.strip()
]
# 分辨率口径（Step 6）：side = 目标长边（保持纵横比，令 max(w,h)=value）；
# area = 目标面积（保持纵横比，令 w*h≈value²，边长≈sqrt(value²·ratio)）。
# 兼容规则：显式设置 ZIV_AI_MAX_RESOLUTION 会强制回 area（保留 Step 4 行为）；
# 否则默认 side。ZIV_AI_RESOLUTION_MODE 可显式覆盖（env 优先于兼容规则）。
_resolution_mode_env = os.environ.get("ZIV_AI_RESOLUTION_MODE", "").strip()
if _resolution_mode_env:
    RESOLUTION_MODE = _resolution_mode_env
elif os.environ.get("ZIV_AI_MAX_RESOLUTION", "").strip():
    RESOLUTION_MODE = "area"
else:
    RESOLUTION_MODE = "side"
# side 口径下的目标长边。
RESOLUTION_SIDE = int(os.environ.get("ZIV_AI_RESOLUTION_SIDE", "1536"))
# side 口径的 OOM 降级：从 RESOLUTION_SIDE 起逐级回退（上界为 RESOLUTION_SIDE）。
RESOLUTION_SIDE_FALLBACK = [
    int(x)
    for x in os.environ.get(
        "ZIV_AI_RESOLUTION_SIDE_FALLBACK", "1536,1280,1024,768,640"
    ).split(",")
    if x.strip()
]
# 采样配置（社区收集 + Step 4 实测）：AuraFlow shift=3.1 / euler / simple / cfg=1.0
AURAFLOW_SHIFT = float(os.environ.get("ZIV_AI_AURAFLOW_SHIFT", "3.1"))
SAMPLER_NAME = os.environ.get("ZIV_AI_SAMPLER", "euler")
SCHEDULER_NAME = os.environ.get("ZIV_AI_SCHEDULER", "simple")
# cfg==1.0 时跳过负提示词 encode（优化 §10.2.2）：采样器在 cfg≈1 时丢弃 uncond
# （comfy/samplers.py:610 `isclose(cond_scale,1.0) -> uncond_=None`），故负向条件编码
# （含视觉塔处理）是纯浪费。默认开；置 0 / false 回退到旧行为。
SKIP_NEGATIVE_AT_CFG1 = os.environ.get("ZIV_AI_SKIP_NEGATIVE", "1") not in ("", "0", "false", "False")
# 仅供测试：置位时把首个候选分辨率伪装成 OOM，以验证降级路径（不影响生产）。
FORCE_OOM = os.environ.get("ZIV_AI_FORCE_OOM", "") not in ("", "0", "false", "False")
# 预览降频：1 = 每步发送，N>1 = 每 N 步发送一帧（契约 §3.5 允许降频）
PREVIEW_EVERY = 1
# ComfyUI latent 预览图的最大边长（latent 分辨率天然很小）
PREVIEW_SIZE = 512

# 无主图（t2i）时的输出目录：程序目录 output/（SPEC §3.9）；可用环境变量覆盖
OUTPUT_DIR = os.environ.get(
    "ZIV_AI_OUTPUT_DIR",
    os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "output"),
)

# ---- LoRA 注册表（Step 8-1）----
# 数据文件 Template/loras.json：commands.json 的 lora.path 用 id 引用它，Python 侧解析为权重路径。
# 默认仓库根 Template/loras.json；可用环境变量 ZIV_AI_LORA_REGISTRY 覆盖。
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
LORA_REGISTRY_PATH = os.environ.get(
    "ZIV_AI_LORA_REGISTRY",
    os.path.join(REPO_ROOT, "Template", "loras.json"),
)

# ---- LoRA 根目录（设置窗口 [models] lora_root）----
# 可选：loras.json / 命令里的**相对** LoRA 路径以此目录为基准拼接（绝对路径原样）。
# 空 = 未配置（向后兼容）：无基准时相对路径按原样透传（禁止 validate 时则报错）。
# 由 C# 注入 env ZIV_AI_LORA_ROOT（AddIfSet：非空即注入，不要求目录存在）。
LORA_ROOT = os.environ.get("ZIV_AI_LORA_ROOT", "").strip()

# ---- 模型注册表（Step 8-2）----
# 数据文件 Template/models.json：每模型含路径 / 分辨率档位 / 采样预设；加模型 = 加一条。
# 优先级：环境变量 > models.json > 本文件的代码默认。默认仓库根 Template/models.json。
MODELS_REGISTRY_PATH = os.environ.get(
    "ZIV_AI_MODELS_REGISTRY",
    os.path.join(REPO_ROOT, "Template", "models.json"),
)

# ---- TE-Speed Qwen Image 2.1 加速（默认关；A/B 实测见 DOC/OPTIMIZATION.md §1）----
# 第三方 Cython 节点（custom_nodes/TE-Speed-QwenImage21，闭源 .pyd、无 LICENSE）。
# A/B 实测（side 1024 / 2048，30 步）：端到端 +12.3% / +15.6%，低于 30% 门槛，故默认关。
# 开启：ZIV_AI_TE_SPEED=1。节点缺失 / 补丁失败时自动跳过（不致命）。
TE_SPEED_ENABLED = os.environ.get("ZIV_AI_TE_SPEED", "0") not in ("", "0", "false", "False")
TE_SPEED_MODE = os.environ.get("ZIV_AI_TE_SPEED_MODE", "te_predictor")  # te_predictor | speed
TE_SPEED_REUSE_THRESHOLD = float(os.environ.get("ZIV_AI_TE_SPEED_THRESHOLD", "0.06"))
TE_SPEED_ERROR_LIMIT = float(os.environ.get("ZIV_AI_TE_SPEED_ERROR_LIMIT", "0.08"))
TE_SPEED_ATTENTION = os.environ.get("ZIV_AI_TE_SPEED_ATTENTION", "kitchen_int8")
TE_SPEED_VERBOSE = os.environ.get("ZIV_AI_TE_SPEED_VERBOSE", "") not in ("", "0", "false", "False")
# 节点目录（可覆盖；默认随 ComfyUI）。
TE_SPEED_NODE_DIR = os.environ.get(
    "ZIV_AI_TE_SPEED_NODE_DIR",
    os.path.join(COMFY_ROOT, "custom_nodes", "TE-Speed-QwenImage21"),
)

# ---- WD14 Tagger（L1 Python 侧打标；外置模块，CPU，不占 GPU）----
# 外部节点 custom_nodes/comfyui-wd14-tagger 提供 ONNX 模型（wd-vit-tagger-v3）与参考实现；
# ZIV.AI 侧 tagger.py 复刻其推理核心（路线 A，绕开节点对 PromptServer / web 的依赖）。
# 默认开；onnxruntime 缺失 / 模型缺失 / 推理失败时静默跳过（返回空列表，不致命）。
# WD14 Tagger 配置（L1 seam；未接 IPC。见 tagger.py 顶部说明）
TAGGER_ENABLED = os.environ.get("ZIV_AI_TAGGER", "1") not in ("", "0", "false", "False")
TAGGER_MODEL_DIR = os.environ.get(
    "ZIV_AI_TAGGER_MODEL_DIR",
    os.path.join(COMFY_ROOT, "custom_nodes", "comfyui-wd14-tagger", "models"),
)
TAGGER_MODEL = os.environ.get("ZIV_AI_TAGGER_MODEL", "wd-vit-tagger-v3")
TAGGER_THRESHOLD = float(os.environ.get("ZIV_AI_TAGGER_THRESHOLD", "0.35"))
TAGGER_CHARACTER_THRESHOLD = float(os.environ.get("ZIV_AI_TAGGER_CHARACTER_THRESHOLD", "0.85"))

# ---- 插件注册表（batch 1 · 统一插件架构）----
# 数据文件 Template/plugins.json：可选「可执行插件」的元数据（id / dir / entry / deps /
# enabled_by_default ...）。启用状态存在 settings.ini [plugins]（键 = 原始插件 id），经 C# 注入
# env ZIV_AI_PLUGIN_<ID> 到达 Python；env 名由 plugin_env_name() 规范化，必须与 C# 同规则。
# 默认仓库根 Template/plugins.json；可用环境变量 ZIV_AI_PLUGINS_REGISTRY 覆盖。
PLUGINS_REGISTRY_PATH = os.environ.get(
    "ZIV_AI_PLUGINS_REGISTRY",
    os.path.join(REPO_ROOT, "Template", "plugins.json"),
)

# 插件相对目录（entry['dir']）的基准目录。App 启动后端时由 C# 注入 ZIV_AI_PLUGINS_BASE_DIR =
# 程序目录（与 C# PluginRegistry.ResolveDirectory 的 AppContext.BaseDirectory 同一值）；
# 与 PLUGINS_REGISTRY_PATH 同理：Python 独立运行（无 env）回退仓库根 / 发布后的程序目录。
PLUGINS_BASE_DIR = os.environ.get("ZIV_AI_PLUGINS_BASE_DIR") or REPO_ROOT


def plugin_env_name(plugin_id):
    """插件 id → 环境变量名 ``ZIV_AI_PLUGIN_<ID>``。

    规则（必须与 C# ``PluginRegistry.EnvName`` 一致）：不在 ``[A-Za-z0-9]`` 的字符一律变 ``_``，
    然后整体大写；``pose-map`` → ``ZIV_AI_PLUGIN_POSE_MAP``。
    """
    normalized = "".join(
        ch if (ch.isascii() and ch.isalnum()) else "_" for ch in (plugin_id or "")
    )
    return "ZIV_AI_PLUGIN_" + normalized.upper()


_PLUGIN_STATE_CACHE = {}  # settings.ini path -> (mtime, {plugin_id: bool})


def _settings_ini_path():
    """settings.ini 的绝对路径：env ``ZIV_AI_SETTINGS_PATH``（C# 注入）> 插件基准目录。"""
    return os.environ.get("ZIV_AI_SETTINGS_PATH") or os.path.join(
        PLUGINS_BASE_DIR, "settings.ini"
    )


def _read_plugin_states(path):
    """读 settings.ini ``[plugins]`` → ``{id: bool}``；缺失 / 读失败 → ``{}``（回退 env）。

    ``optionxform=str`` 保留插件 id 大小写（与 C# ``PluginRegistry`` 的 Ordinal 一致）；
    结果按文件 mtime 缓存，文件未变不重读。坏 ini 绝不抛（异常一律回退 env）。
    """
    try:
        mtime = os.path.getmtime(path)
    except OSError:
        return {}
    cached = _PLUGIN_STATE_CACHE.get(path)
    if cached is not None and cached[0] == mtime:
        return cached[1]
    states = {}
    parser = configparser.ConfigParser()
    parser.optionxform = str
    try:
        with open(path, "r", encoding="utf-8", errors="ignore") as fh:
            parser.read_string(fh.read())
        if parser.has_section("plugins"):
            for key, value in parser.items("plugins"):
                norm = value.strip().lower()
                if norm in ("1", "true", "yes", "on"):
                    states[key] = True
                elif norm in ("0", "false", "no", "off"):
                    states[key] = False
    except Exception:  # noqa: BLE001 - 坏 ini 回退 env，绝不抛
        states = {}
    _PLUGIN_STATE_CACHE[path] = (mtime, states)
    return states


def plugin_enabled(plugin_id, entry=None):
    """插件是否启用，优先级：settings.ini ``[plugins]``（键存在）> env ``ZIV_AI_PLUGIN_<ID>`` >
    ``entry`` 的 ``enabled_by_default``。

    settings.ini 优先使「UI 改启用状态」无需重启 App 即生效（每任务重读；env 成为后备）。
    注册表读取由调用方负责（loader 传入 ``entry``），避免 config ↔ loader 循环导入。
    假值拼写 ``0`` / ``false`` / ``False`` 表示禁用。
    """
    states = _read_plugin_states(_settings_ini_path())
    if plugin_id in states:
        return states[plugin_id]
    value = os.environ.get(plugin_env_name(plugin_id))
    if value is not None and value != "":
        return value not in ("0", "false", "False")
    if isinstance(entry, dict):
        return bool(entry.get("enabled_by_default", False))
    return False

# 是否关闭 ComfyUI「智能显存优化」（等效官方 --disable-smart-memory）。**默认关闭（1）**。
# Step 6 实测（side 1280 / 25 步 / RTX 4080 16GB）：启用智能显存时耗时 60.19s 且 VAE decode
#   触发 OOM→tiled 回退；故维持 Step 3.4 的关闭策略，显存改由管线内编排（pipeline._release）控制。
# 回退方式：设 ZIV_AI_DISABLE_SMART_MEMORY=0（或 false）即启用智能显存（原生行为）。
# 顺序约束：comfy.model_management 在 import 时把该值读成模块常量，
#   必须在首次 import comfy.model_management 之前设置
#   （由 model_loader.prepare_environment() 落地；handlers._run_submit 先调它）。
DISABLE_SMART_MEMORY = os.environ.get("ZIV_AI_DISABLE_SMART_MEMORY", "1") not in ("", "0", "false", "False")
# SageAttention 默认开启（等效官方 --use-sage-attention，Step 6）。仅当 sageattention 包
# 可用时才真正启用（model_loader 以 find_spec 检查）；包缺失时保持关闭，避免 ComfyUI
# attention.py 因缺包而 exit(-1)（见 ComfyUI comfy/ldm/modules/attention.py:30-36）。
# 回退方式：设环境变量 ZIV_AI_SAGE_ATTENTION=0（或 false）即恢复原生 attention。
SAGE_ATTENTION = os.environ.get("ZIV_AI_SAGE_ATTENTION", "1") not in ("", "0", "false", "False")
# 关闭 ComfyUI 的 host pinned memory（等效官方 --disable-pinned-memory）。
# pinned buffer 上限 = RAM×40%×2（model_management.py:1617,1625-1628）；@1536 工作集溢出时
# 会被填满（RSS ≈ 2× staged 模型 ≈ 33GB）并触发 host RAM thrash。置 1 关闭 pin（牺牲 pin 的
# PCIe 加速）以验证机制。默认 0 = 保持 ComfyUI 默认（启用 pin）。
# 约束：必须在首次 import comfy.model_management 之前设置（同 DISABLE_SMART_MEMORY）。
DISABLE_PINNED_MEMORY = os.environ.get("ZIV_AI_DISABLE_PINNED_MEMORY", "0") == "1"
# 启用 ComfyUI Dynamic VRAM（comfy-aimdo，等效官方 ComfyUI/main.py 的启动引导）。
# **默认开启**：该引导只写在官方入口 main.py 里，进程内直连 ComfyUI 时不会执行，于是落到
#   传统 ModelPatcher（粗粒度 offload、卸载不彻底），16GB 卡上高分辨率会冲顶（实测 side 1536）。
#   启用后权重由 vbar 按需换入换出，与官方流一致。回退：设 ZIV_AI_DYNAMIC_VRAM=0（或 false）。
DYNAMIC_VRAM = os.environ.get("ZIV_AI_DYNAMIC_VRAM", "1") not in ("", "0", "false", "False")
# ---- 官方 main.py 运行时 env 对齐（2026-09-29）----
# 官方入口 ComfyUI/main.py 的 `if __name__ == "__main__"` 段设置了一些进程级 env；进程内直连
# ComfyUI 时不会执行，导致与官方行为差异（@1536 thrash 的候选源）。这里只“对齐官方已设的 env”，
# 不碰官方 cli_args / model_management / aimdo 调用。每项用 ZIV_AI_* 开关，可单独关闭做 A/B。
#   - ALIGN_CUDA_MALLOC_ASYNC：等效官方 `import cuda_malloc`（cu130 自动设
#     PYTORCH_CUDA_ALLOC_CONF=backend:cudaMallocAsync）。**必须早于首次 CUDA 初始化**。
#   - ALIGN_MIMALLOC_PURGE：等效官方 main.py:84 `MIMALLOC_PURGE_DELAY=0`（Windows）。
#   - ALIGN_CUDA_VISIBLE_DEVICES：等效官方 main.py:52 Windows 单卡强制 `CUDA_VISIBLE_DEVICES=0`。
#     该项与平台/硬件绑定，**默认关闭**（单卡机器上为 no-op；需显式开启才对齐）。
ALIGN_CUDA_MALLOC_ASYNC = os.environ.get("ZIV_AI_CUDA_MALLOC_ASYNC", "1") not in ("", "0", "false", "False")
ALIGN_MIMALLOC_PURGE = os.environ.get("ZIV_AI_MIMALLOC_PURGE_DELAY", "1") not in ("", "0", "false", "False")
ALIGN_CUDA_VISIBLE_DEVICES = os.environ.get("ZIV_AI_CUDA_VISIBLE_DEVICES", "0") not in ("", "0", "false", "False")


def apply_official_env():
    """Set the env vars official ``main.py`` sets in its ``__main__`` block.

    Idempotent and order-sensitive: call before PyTorch's first CUDA init (in the real
    backend, before ``import handlers``; in-process, at the top of
    ``model_loader.prepare_environment``). Only the three aligned vars are touched.
    """
    if ALIGN_CUDA_MALLOC_ASYNC:
        conf = os.environ.get("PYTORCH_CUDA_ALLOC_CONF", "")
        parts = [p for p in conf.split(",") if p]
        if not any(p.startswith("backend:") for p in parts):
            parts.append("backend:cudaMallocAsync")
            os.environ["PYTORCH_CUDA_ALLOC_CONF"] = ",".join(parts)
    if ALIGN_MIMALLOC_PURGE:
        os.environ.setdefault("MIMALLOC_PURGE_DELAY", "0")
    if ALIGN_CUDA_VISIBLE_DEVICES:
        os.environ.setdefault("CUDA_VISIBLE_DEVICES", "0")


# DynamicVRAM 显存余量注入（方案 1，2026-09-29）。进程内直连 ComfyUI 没有官方 main.py 的 CLI 解析，
#   故 --reserve-vram（→ control.init 的 simple_vram_headroom）与 --vram-headroom（→ init_devices
#   的每设备 extra_vram_headroom）恒为默认（None / 0）。ZIV @1536 + Viggle 峰值 16002 MiB 贴近 16376
#   卡上限，Windows 便外溢到「共享 GPU 内存」/ host RAM 触发 thrash（77s，官方 19.4s）。这里用 env 注入
#   余量把峰值压回临界之下。单位 MB（官方 CLI 用 GB）；仅当 > 0 时注入，0 = 保持 ComfyUI 自身默认。
#   - VRAM_HEADROOM_MB：每设备额外余量（等效官方 --vram-headroom），经 control.init_devices 生效。
#   - VRAM_RESERVE_MB ：进程级 simple 预算余量（等效官方 --reserve-vram），经 control.init 生效。


def _env_int_mb(name):
    try:
        return int(os.environ.get(name, "0") or "0")
    except ValueError:
        return 0


VRAM_HEADROOM_MB = _env_int_mb("ZIV_AI_VRAM_HEADROOM_MB")
VRAM_RESERVE_MB = _env_int_mb("ZIV_AI_VRAM_RESERVE_MB")

# ---- host-RAM / 共享显存守卫（mem_guard）----
# 专用 VRAM 打满后，Windows 会把权重外溢到「共享 GPU 内存」(= 系统 RAM)，DynamicVRAM 还会额外把
# 权重 pin 在 host RAM（≈2× 模型）。二者叠加会让 host RAM / commit 激增，严重时整机卡死。
# 采样每一步检查：可用物理内存 / 可用 commit 低于下限、或本进程 RSS 高于上限 → 当作 OOM 中止
# （触发分辨率降级），在系统崩之前干净退出。任一阈值置 0 = 关闭该项；全部为 0 = 守卫整体关闭。
# 回退方式：设 ZIV_AI_MEM_GUARD=0。
GUARD_ENABLED = os.environ.get("ZIV_AI_MEM_GUARD", "1") not in ("", "0", "false", "False")
GUARD_MIN_FREE_RAM_GB = float(os.environ.get("ZIV_AI_MEM_GUARD_MIN_FREE_RAM_GB", "4"))
GUARD_MIN_FREE_COMMIT_GB = float(os.environ.get("ZIV_AI_MEM_GUARD_MIN_FREE_COMMIT_GB", "4"))
# 0 = 不检查进程 RSS（默认关：不同机器/模型差异大，误杀风险高）。
GUARD_MAX_RSS_GB = float(os.environ.get("ZIV_AI_MEM_GUARD_MAX_RSS_GB", "0"))
# ---- 启动预热（优化 §10.2.1）----
# 进程连上管道后，在后台线程 import comfy/torch + DynamicVRAM init（纯 CPU、不加载权重、不占显存），
# 把首次 submit 的 ~4.1s 移出用户等待路径。C# 侧可在 App 启动时提前拉起本进程（[backend] prewarm）。
# 默认开；置 0 / false 关闭。注意：C# 的 [backend] prewarm 与这里的 ZIV_AI_PREWARM 相互独立——
# 前者控制「是否提前拉起进程」，后者控制「进程连上后是否预热」。
PREWARM = os.environ.get("ZIV_AI_PREWARM", "1") not in ("", "0", "false", "False")
# 预热深度：0 = 仅 prepare_environment（默认，安全）；1 = 再 ensure_loaded（读权重到 RAM，~0.8s）。
# 警告：1 会在引擎锁内加载权重，若与首次 submit 并发会阻塞后者、可能触发加载超时——非默认，一般不必开。
try:
    PREWARM_LEVEL = int(os.environ.get("ZIV_AI_PREWARM_LEVEL", "0") or "0")
except ValueError:
    PREWARM_LEVEL = 0
# 空闲卸载超时（秒，可配置，Z21）；可用环境变量覆盖以便测试
IDLE_UNLOAD_SECONDS = float(os.environ.get("ZIV_AI_IDLE_UNLOAD_S", "300"))
# idle_watcher 检查间隔（秒）
IDLE_CHECK_INTERVAL_S = float(os.environ.get("ZIV_AI_IDLE_CHECK_S", "30"))

# ---- 心跳（Step 3）----
# Python -> C# 心跳间隔（秒）；可用环境变量覆盖以便测试
HEARTBEAT_INTERVAL_S = float(os.environ.get("ZIV_AI_HEARTBEAT_INTERVAL_S", "10"))
# 置位则不发心跳（仅用于“失联”测试）
HEARTBEAT_DISABLED = os.environ.get("ZIV_AI_HEARTBEAT_DISABLED", "") not in ("", "0", "false", "False")
# 主循环非阻塞轮询间隔（PeekNamedPipe）；见 Step 2.4 的 fd 锁说明
POLL_INTERVAL_S = float(os.environ.get("ZIV_AI_POLL_INTERVAL_S", "0.05"))


def normalize_pipe_path(name):
    name = (name or "").strip()
    if not name:
        return PIPE_PATH
    if name.startswith("\\\\") or name.startswith("//"):
        return name
    return "\\\\.\\pipe\\" + name


def setup_logging(level="INFO", log_file=None):
    logger = logging.getLogger("zivai.server")
    logger.setLevel(getattr(logging, str(level).upper(), logging.INFO))
    for handler in list(logger.handlers):
        logger.removeHandler(handler)
        try:
            handler.close()
        except Exception:
            pass

    formatter = logging.Formatter("%(asctime)s %(levelname)s %(name)s: %(message)s")
    stream_handler = logging.StreamHandler(sys.stderr)
    stream_handler.setFormatter(formatter)
    logger.addHandler(stream_handler)

    if log_file:
        file_handler = logging.FileHandler(log_file, encoding="utf-8")
        file_handler.setFormatter(formatter)
        logger.addHandler(file_handler)

    logger.propagate = False
    return logger
