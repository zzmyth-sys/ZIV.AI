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

MODEL_ROOT = os.environ.get(
    "ZIV_AI_MODEL_ROOT",
    r"C:\AI\ComfyUI_PIC\ComfyUI\models",  # 开发期默认；生产建议用 env 或 models.json
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
DEFAULT_STEPS = int(os.environ.get("ZIV_AI_DEFAULT_STEPS", "40"))
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


def plugin_enabled(plugin_id, entry=None):
    """插件是否启用：env ``ZIV_AI_PLUGIN_<ID>`` 优先，否则用 ``entry`` 的 ``enabled_by_default``。

    注册表读取由调用方负责（loader 传入 ``entry``），避免 config ↔ loader 循环导入。
    假值拼写 ``0`` / ``false`` / ``False`` 表示禁用。
    """
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
# 启用 ComfyUI Dynamic VRAM（comfy-aimdo，等效官方 ComfyUI/main.py 的启动引导）。
# **默认开启**：该引导只写在官方入口 main.py 里，进程内直连 ComfyUI 时不会执行，于是落到
#   传统 ModelPatcher（粗粒度 offload、卸载不彻底），16GB 卡上高分辨率会冲顶（实测 side 1536）。
#   启用后权重由 vbar 按需换入换出，与官方流一致。回退：设 ZIV_AI_DYNAMIC_VRAM=0（或 false）。
DYNAMIC_VRAM = os.environ.get("ZIV_AI_DYNAMIC_VRAM", "1") not in ("", "0", "false", "False")
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
