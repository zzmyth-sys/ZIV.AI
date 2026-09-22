# Step 7 GPU 端到端验证结果（e2e_step7）

- 日期：2026-09-22；环境：Windows 10 / RTX 4080 16GB / ComfyUI v0.37.0 / .NET SDK 10.0.401。
- Z30：开跑前 `nvidia-smi` = **1055 MiB**（≈基线 ~900 MiB），无推理进程 → 空闲，符合 Z30。
- 装配：`PythonProcessManager → IpcInferenceClient → ToolRegistry(QW21edit + QW21outpaint) → Executor`。
- 命令：`e2e_step7.exe all`（三条路径在同一进程内串行，模型仅加载一次）。
- 脚本：`Program.cs`；图像检查 `check_step7.py`。

## 汇总

| 路径 | op | 请求尺寸 | 实际尺寸 | 模式 | 耗时 | 结果 |
|---|---|---|---|---|---|---|
| A · T2I | `t2i` | Side(1536) | 1536×1536 | RGBA | 30.44 s | ✅ PASS |
| B · Outpaint | `outpaint` | Explicit(2048,1280) | 2048×1280 | RGBA | 39.69 s | ✅ PASS |
| C · 原生 RGBA | `t2i` | Side(1024) | 1024×1024 | RGBA | 9.83 s | ✅ PASS |

- 模型加载（冷）：约 6.8 s（首个 submit 惰性加载，`loading_model` dit/te/vae）。
- GPU 峰值（500 ms 采样）：**12929 MiB**；结束后回落 **953 MiB**（≈基线）。
- 源图 `user_input_1024.png` SHA256 前后一致：**True**（Z24 未破坏原图）。
- 总耗时：**81.21 s**。

## 路径 A：T2I（`op="t2i"`）

- 输入：`ImagePath=null`，prompt「一位穿深青汉服的女性站在古代中式茶肆中读书，木桌与灯笼，写实风格」，steps=25，seed=42。
- 输出：`t2i_output.png`（3,438,258 bytes，1536×1536）。
- 画质：`blockRatio=0.893`、`lag1=0.970` → **非噪声**。
- 视觉：茶肆内景（木柱、灯笼、桌椅、青花茶具）与读书女子，写实风格，与 prompt 吻合。

## 路径 B：Outpaint（`op="outpaint"`）

- 输入：`ImagePath=user_input_1024.png`，`anchor=center`，`Resolution=Explicit(2048,1280)`，
  prompt「扩展画布，四周补充为古代中式茶肆环境，保持画面中心的原图内容不变」，steps=25，seed=42。
- 输出：`outpaint_output.png`（2,842,064 bytes，**2048×1280**，与请求一致）。
- 中心保真：源图（1024×640）与输出中心裁剪（@512,320）→ **MAD=1.3、corr=0.9994**（几乎逐像素保留）。
- 新区域：std=27.2（有生成内容，非纯色填充）；中心 std=74.0。
- 视觉：四周生成了木质茶肆环境（家具、盆栽、木地板），衔接合理；**但源图自带视频黑边（letterbox）**，
  中心在视觉上呈「墙上挂画/屏幕」的硬边效果——属源图边框 + inpaint 扩图的已知接缝现象，非管线错误。

## 路径 C：原生 RGBA（关键验证）

- 输入：`ImagePath=null`，prompt「一位穿深青汉服的女性，RGBA 透明背景，alpha channel，PNG with transparency，保持人物边缘清晰」，steps=25，seed=42，Side(1024)。
- 输出：`rgba_output.png`（1,047,537 bytes，1024×1024）。
- **PNG 模式：`RGBA`**（非 RGB）。
- alpha：`min=0`、`max=255`、`mean=81.0`；**透明像素(α<128)=68.3%**、不透明(α≥128)=31.7%。
- alpha 空间分布：中心（主体）均值 **180** vs 边缘（背景）均值 **48** → 主体不透明、背景透明。
- 视觉：人物干净抠出、边缘清晰、背景透明。
- **结论：确认 Qwen-Image-2.1 原生 RGBA 输出成立**（VAE 64 通道含 alpha），无需 matting 模型。

## 备注

- T2I / Outpaint 输出同为 `RGBA` 模式，但 alpha 近乎全 255（不透明）——说明管线保留第 4 通道，
  仅 RGBA 提示词才产生真实透明。
- `run_outpaint` 复用 masked inpaint 路径；画布恰为 `_snap16` 目标尺寸，explicit resize 为 no-op。
- 本验证为临时程序，**不属于 `ZIV.AI.sln`**；输出 PNG / `work/` / `run.log` 不入库。

## 方案 A 复测：对齐官方 outpaint 工作流（2026-09-22）

依据官方蓝图 `ComfyUI/blueprints/Image Outpainting (Qwen-Image).json`，把扩图从
「黑填充 + 硬二值掩膜」改为「**灰 0.5 填充 + 羽化/生长软掩膜**」（`ImagePadForOutpaint`
+ `Grow and Blur Mask` 的等价实现）：

- `outpaint.py`：画布填充 `(128,128,128)`；掩膜按 `feathering=40` 的 `v²` 斜坡、`grow=20`
  膨胀、`blur=31` 高斯；输出软掩膜（0..1）。
- `pipeline.py`：`_load_mask_tensor(binary=False)` 放行后端生成的 outpaint 软掩膜
  （Z19 仅约束 C# 侧用户掩膜为二值）；`run_outpaint` 传 `mask_binary=False`。

### 结果

| 输入 | 尺寸 | 耗时 | 峰值 | 中心 MAD / corr | 视觉 |
|---|---|---|---|---|---|
| `user_input_1024.png`（含黑边） | 2048×1280 | 49.50 s | 12217 MiB | 1.3 / 0.9993 | 接缝变软、环境更连贯；**中心仍呈「框中景」= 源图 letterbox 黑边所致** |
| `source_crop.png`（裁掉黑边，1020×543） | 2048×1280 | 49.15 s | 12363 MiB | 1.4 / 0.9994 | **无缝衔接**：茶肆环境自然外扩，中心原图逐像素保留，几乎无可见接缝 |

- 结论：**方案 A 有效**；此前「硬边/框」主要来自测试源图的视频黑边，非算法缺陷。
- Python 单测 `test_outpaint.py` 更新为灰填充 + 软掩膜语义 → **11 通过 / 0 失败**。
- 未采用 ControlNet（官方 v1 蓝图所用 InstantX Inpainting ControlNet 面向 Qwen-Image v1，
  与 2.1 兼容性未验证）；如需进一步一致性可另做小样实测（方案 B）。
- 原 `user_input_1024.png` 的 A 结果另存 `outpaint_output_A_bars.png`；裁切源结果
  `outpaint_output_crop.png`。
