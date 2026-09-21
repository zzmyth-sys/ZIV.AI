import logging
import os
import sys

COMFY_ROOT = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"

MODEL_ROOT = r"C:\AI\ComfyUI_PIC\ComfyUI\models"

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

PROTOCOL_VERSION = "0.5"
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

# ---- 显存策略（Z21 空闲卸载：Step 3 实现）----
# auto = 交给 ComfyUI 的 model_management 决定权重驻留（CPU/GPU/offload）
VRAM_MODE = "auto"
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
