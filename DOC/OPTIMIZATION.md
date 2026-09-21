# ZIV.AI 优化候选清单（OPTIMIZATION）

- 状态：**候选清单，非冻结**（可增删，不影响 `FROZEN.md`）
- 用途：记录已调研的社区优化方案，按需引入
- 关联：`DOC/SPEC.md`（性能预算）、`DOC/FROZEN.md`（Z18 / Z21 / Z24）

> 本文件只登记**候选**与已知收益，未经实测的数字一律标注「待实测」。引入任一项前，
> 先单独实测，再评估叠加；不得影响 IPC 契约（`contracts/ipc-protocol.md`）与 C# 稳定性。

## 1. 速度优化

| # | 方案 | 状态 | 预期收益 | 风险 / 备注 |
|---|---|---|---|---|
| 1.1 | 前缀 KV 缓存（prompt prefix cache） | **已生效** | 编辑约 **1.7x** | 依赖文本编码器缓存命中 |
| 1.2 | MagCache | 候选 | 约 **1.75x** | **低风险**，优先候选 |
| 1.3 | TeaCache | 候选 | 约 **1.5–2.0x** | 缓存步长需调参 |
| 1.4 | Lightning LoRA（4–8 步） | 候选 | 约 **4–6x** | 注意 alpha/rank 缩放 |
| 1.5 | Turbo LoRA（2 步） | 候选（远期） | 约 **40x** | 质量下降明显，远期评估 |
| 1.6 | GGUF 量化 | 候选 | 最低 **~4 GB** 显存 | 按需引入，注意画质/兼容 |

## 2. 提示词遵循优化

| # | 方案 | 状态 | 说明 |
|---|---|---|---|
| 2.1 | PE-I2I 重写器（9B） | 候选 | ~18.8 GB 显存；建议作**独立预处理**，不与主推理同时驻留 |
| 2.2 | 多参考图 `<imageN>` 语法 | **已支持** | 多图路径**待实测** |
| 2.3 | 提示词五层结构 | 使用建议 | 建议 **50–150 词**，分层描述主体 / 场景 / 风格 / 光影 / 约束 |

## 3. 优化叠加预期

> 叠加为**乘法估算**，实际受显存、批大小、调度影响，**必须实测**。

| 组合 | 估算倍数 | 备注 |
|---|---|---|
| 前缀 KV 1.7x × MagCache 1.75x × Lightning LoRA 5x | ≈ **15x** | 待实测；注意 LoRA 与缓存相互影响 |

## 4. 引入原则

1. **先单独实测**，再评估叠加；每项记录前后耗时 / 显存 / 画质。
2. **优先低风险**（如 1.2 MagCache），后高风险（如 1.5 Turbo LoRA）。
3. **不影响 IPC 契约与 C# 稳定性**；后端可单独升级（Z23），前端不改。
4. 不破坏 Z24（输出新文件）、Z18（GPU 串行）、Z21（空闲卸载）等既有约束。

## 5. 不在清单的项

- 云端推理 / 联网服务
- 模型训练 / 微调
- 跨平台（仅 Windows）

## 6. 基线数据（Step 4 实测）

- 日期：2026-09-22；环境：Windows 10 / RTX 4080 16GB / ComfyUI v0.37.0（Python 3.13.14）。
- 配置：512² 图像编辑（同一 prompt / 同一输入图），`steps=20`、`seed=42`、`denoise=1.0`；
  smart memory **默认关闭**（`DOC/FROZEN.md` Step 3.4）；单进程引擎，跑 3 次。
- 脚本：`_test_step2/baseline_bench.py`；原始输出：`_test_step2/baseline_bench_result.txt`。

> **显存口径澄清（重要）**：下表的 `peak_alloc / peak_reserved` 为
> **「采样期间 torch 分配器峰值」**（`torch.cuda.max_memory_allocated/reserved`，
> 每次运行前 `reset_peak_memory_stats()`）；而 `DOC/DEVLOG.md` Step 3 记录的
> **0.7 GB** 是**「推理完成后的稳态占用」**（`torch.cuda.memory_allocated()`，采样结束后测量）。
> **两者口径不同，不可直接对比**：峰值约 11.0 GB 与稳态约 0.7 GB 描述的是同一过程的不同时刻
> （本步退出前稳态回到 ~9.3 GB，属 `disable_smart_memory` 下权重采样后驻留）。
> **后续优化项对比统一使用 peak 口径**——优化关注的是峰值能否降低。

| 项 | Run 1（冷） | Run 2 | Run 3 | 稳态均值（Run 2–3） |
|---|---|---|---|---|
| 模型加载 | **6.09 s**（dit 0.05 / te 0.61 / vae 0.27） | — | — | — |
| moving_to_gpu | 5.632 s | 2.453 s | 2.176 s | **2.314 s** |
| 采样（20 步） | 1.578 s | 1.629 s | 1.650 s | **1.639 s** |
| VAE decode（含保存） | 2.285 s | 0.994 s | 0.989 s | **0.992 s** |
| 总耗时 | 15.763 s | 7.653 s | 7.413 s | **7.533 s** |
| 峰值显存（torch alloc，peak 口径） | 10993 MB | 10996 MB | 10995 MB | **10995 MB** |
| 峰值显存（torch reserved，peak 口径） | 11370 MB | 11358 MB | 11362 MB | **~11360 MB** |

- GPU 基线 793 MB → 加载后 1210 MB → 采样中 `nvidia-smi` 约 11.0–11.1 GB；基准进程退出后回落 871 MB。
- 供后续优化项对比：每项优化单独实测后，与此表逐行比对（`DOC/OPTIMIZATION.md` §4 引入原则），
  **显存统一取 `peak_alloc` 峰值口径**（见上方口径澄清）。

### 6.1 1024 编辑基线（Step 4 修正后 · AuraFlow shift=3.1）

- 配置：`ModelSamplingAuraFlow(shift=3.1)` + `comfy.sample.sample`（euler / simple /
  cfg=1.0）/ steps=40 / seed=42 / smart memory 关闭；输入 1024×640（16:10）。
- 脚本：`_test_step2/test_1024.py`；输出 `_test_step2/user_output_1024.png`。

| 项 | 数值 |
|---|---|
| 模型加载 | 0.79 s（热态；冷态约 4–7 s） |
| moving_to_gpu | **7.07 s** |
| 采样（40 步） | **8.82 s** |
| VAE decode | **4.28 s** |
| 总耗时（采样段） | **15.89 s** |
| 峰值显存（`nvidia-smi`） | **16004 MiB / 16376 MiB** |
| 画面结构比 blockRatio | 0.862（有效，非噪声） |

> **显存触顶风险**：1024 编辑设备峰值 16004 MiB，仅余约 370 MiB；更高分辨率（2048）
> 极可能 OOM，已由 `pipeline.py` 的 OOM 降级（1024 → 768 → 640）兜底。

### 6.2 Step 6 实测：SageAttention + side 1536 + Dynamic VRAM

- 配置：`side` 口径长边 **1536**（输出 1536×960）、`steps=25`、`seed=42`、`cfg=1.0`、
  SageAttention 2.2、`DISABLE_SMART_MEMORY=1`、**Dynamic VRAM 启用**（`aimdo_enabled=True`）；
  环境 Windows / RTX 4080 16GB / ComfyUI 0.37.0（Python 3.13.14）。
- 脚本：`E:\temp\opencode\zivai_stage_1536.py`；输出 `zivai_norel_cold.png` / `zivai_norel_hot.png`。

| 阶段 | 冷跑 | 热跑 |
|---|---|---|
| encode + 环境准备 | 5.44 s | 4.21 s |
| moving + 采样（25 步） | 13.35 s | 13.03 s |
| VAE decode + 保存 | 0.94 s | 0.90 s |
| **总耗时** | **19.73 s** | **18.13 s** |
| **峰值显存（nvidia-smi）** | **13091 MiB** | **13093 MiB** |

- 对照（同输入 / 同意图 / 同 seed / 同 1536）：
  - legacy `ModelPatcher`（未启用 Dynamic VRAM）：冷 41.90 s、峰值 **16030 MiB**；
  - 官方 ComfyUI server 流（`ComfyUI/main.py` 启动）：22.15 s、峰值 14968 MiB。
- 长边 2048（输出 2048×1280，同输入 / 同意图 / 同 seed）：冷跑 **40.00 s**、热跑 **39.30 s**，
  峰值 **12084 / 12226 MiB**（encode 9.23 / 8.39 s、moving+sample 29.20 / 29.30 s、
  decode 1.57 / 1.61 s）。**未 OOM、未触发 tiled VAE**；采样段约为 1536 的 **2.2×**（面积比 1.78×）。
  峰值反低于 1536，是 Dynamic VRAM 在显存吃紧时把权重 evict 到 host RAM 的表现（PCIe 往返换显存）。
- 结论：启用 Dynamic VRAM 是本次最大收益项（峰值 −2.9 GB，冷跑 −53%），且优于官方流；
  `pipeline._release` 手动分时卸载 A/B 对照无收益（19.96 / 18.12 s），已移除。
- 已知风险：`comfy_aimdo.control.init()` 必须先于 `comfy_aimdo.host_buffer` 的首次 import，
  否则 `host_buffer.lib` 被冻结为 `None`（见 `FROZEN.md` §6.6）。
