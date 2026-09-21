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
    "qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors",
)
VAE_PATH = os.path.join(MODEL_ROOT, "vae", "qwen_image_2.1_vae_bf16.safetensors")

MODEL_NAME = "qwen-image-2.1"

PIPE_NAME = "zivai.infer.v1"
PIPE_PATH = r"\\.\pipe\zivai.infer.v1"

PROTOCOL_VERSION = "0.4"
BACKEND_VERSION = "0.3.0"

CONNECT_TIMEOUT_S = 15.0
MAX_FRAME_BYTES = 256 * 1024 * 1024

# ---- 推理默认值（Step 2.3）----
DEFAULT_RESOLUTION = 512
DEFAULT_STEPS = 20
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
# 关闭 ComfyUI「智能显存优化」（等效官方 --disable-smart-memory）。
# 依据 klein 启动器实测：开启时采样峰值 ~16G（随时 OOM），关闭后稳态更低。
# 注意：comfy.model_management 在 import 时读取该值，必须在首次 import comfy.* 前设置
# （由 model_loader.prepare_environment() 落地）。
DISABLE_SMART_MEMORY = os.environ.get("ZIV_AI_DISABLE_SMART_MEMORY", "1") not in ("", "0", "false", "False")
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
