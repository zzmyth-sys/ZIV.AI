# 遮罩灰度实验报告（Mask Feather Experiment）

- 日期：2026-09-24
- 目的：验证 Qwen-Image-2.1 是否接受**灰度（软边）遮罩**，决定「羽化」实现路径
  （C# 导出灰度 PNG，或维持 C# 二值 + 后端羽化）。
- 范围：只读验证，**未改产品代码**（`src/` / `python/server/*` / `contracts/*` 均未动）。
- 脚本：`_test_step2/mask_feather_exp.py`（推理）、`mask_feather_analyze.py`（CPU 分析）。

## Z30 前置（GPU 状态）

- `nvidia-smi`：RTX 4080 16GB，**占用 1196 MiB / 空闲 14855 MiB**，无 CUDA 计算进程
  （仅桌面程序；基线约 793 MB）。判定 **空闲**。
- 用户明确同意后启动；共 **3 次推理**（未超限）；结束后 GPU 回落 1236 MiB，无残留进程。

## 关键发现（影响实验设计）

`python/server/pipeline.py` 的 `_load_mask_tensor(path, binary=True)` 在 `binary=True` 时执行
`array = (array >= 0.5)`。**产品正常编辑路径用 `mask_binary=True`，灰度遮罩会被阈值化成二值**。
→ 实验调用**既有参数** `pipeline.run(..., mask_binary=False)`（outpaint 已在用，非改代码），
三组同一代码路径、仅遮罩不同。

## 实验设置

- 主图：`_test_step2/user_input.png`（2560×1599）→ 内部缩放 side=1024 → **1024×640**
- 遮罩：居中矩形（50%×50%），A=二值 0/255；B=高斯 σ=10；C=高斯 σ=25（均 1024×640）
- 提示词：`Replace the masked region with a bright blue sky with white clouds.`
- steps=25，seed=42，denoise=1.0，`mask_binary=False`
- 环境：`Comfyui/python_embeded/python.exe`（torch 2.13.0+cu130）；Dynamic VRAM 开启
- 遮罩统计：A unique=2；B unique=255；C unique=256 → B/C 确为灰度
- 管线读入校验：三组 `_load_mask_tensor(binary=False)` 均 min=0.000 / max=1.000 → **软值确实到达采样器**

## 三组结果

| 组 | 遮罩 | 输出 | 耗时 | pipeline_ms | peak_alloc(MiB) | peak_reserved(MiB) |
|---|---|---|---|---|---|---|
| A | 二值 0/255 | `mask_feather_A.png` 1024×640 | 11.02 s | 11018 | 1841 | 2572 |
| B | 高斯 σ=10 | `mask_feather_B.png` 1024×640 | 9.38 s | 9382 | 1841 | 3208 |
| C | 高斯 σ=25 | `mask_feather_C.png` 1024×640 | 9.36 s | 9363 | 1841 | 3208 |

- 总墙钟 37.3 s（含模型 mmap 装载；DiT 6.76 GB / TE 8.71 GB 惰性映射，`load_dit 0.0s`）。
- 显存口径：上表为 **torch 分配器峰值**（激活）。Dynamic VRAM 开启时权重经 host buffer 换入换出，
  **不计入 torch 分配器**，故该值（~1.8 GB）不代表设备峰值；设备峰值本次**未采集**（需并发采样
  `nvidia-smi`，受「不超 3 次推理」约束未重跑）。

## 对比分析（CPU，均值绝对差 0-255）

**输出 vs 源图**

| 组 | 遮罩核心区 | 遮罩外 | 全图 |
|---|---|---|---|
| A | 10.02 | 0.87 | 3.16 |
| B | 9.34 | 0.83 | 2.96 |
| C | 7.21 | 0.83 | 2.42 |

→ 遮罩外几乎不变（≈0.8，为 VAE 往返误差）；核心区被编辑。**模型确实只改遮罩区**。

**输出 vs 输出**

| 对 | 全图 | 核心区 | 遮罩外 |
|---|---|---|---|
| A vs B | 0.48 | 1.51 | 0.14 |
| B vs C | 0.82 | 2.94 | 0.11 |
| A vs C | 1.07 | 3.76 | 0.17 |

→ 遮罩外几乎相同（确定性），核心区 A/B/C **可测地不同** → **灰度未被阈值化**。

**接缝带（mask 50..200）**

- B 接缝 vs A = 1.27（27980 px）
- C 接缝 vs A = 1.90（69276 px）
→ 差异集中在接缝带。

**接缝不连续度（顶边垂向梯度 / 内部参考梯度）**

| 图 | 比值 |
|---|---|
| 源图 | 0.10 |
| A（二值） | **0.17** |
| B（σ=10） | 0.15 |
| C（σ=25） | 0.13 |

→ A > B > C，趋近源图 0.10：**软边降低接缝阶跃（更自然）**。

**核心区均值 RGB**

| 图 | R | G | B |
|---|---|---|---|
| 源图 | 133.6 | 125.2 | 122.1 |
| A | 119.6 | 117.0 | 119.8 |
| B | 120.5 | 117.9 | 120.3 |
| C | 123.6 | 119.8 | 121.1 |

→ 提示词未强驱动成蓝色（核心均值略降），但核心区 p99 差达 122–132、max 145–171，
说明局部有实质改动；C（σ=25）整体改动最弱。

## 结论

对照任务判定表：

- **B/C 与 A 并非「基本相同」**（核心 1.5–3.8、接缝 1.3–1.9 的可测差异）→ 排除「灰度被内部阈值化」。
- **无异常**（无全黑/全白/报错/崩；遮罩外保留）→ 排除「模型不接受灰度」。
- **软边降低接缝不连续度**（0.17 → 0.15 → 0.13），编辑范围集中在遮罩区。

→ **判定：模型/采样器接受灰度（软边有效）。**

**建议路径：C# 可导出灰度 PNG**（后端走 `mask_binary=False`，即 outpaint 现有软掩膜路径）。

**注意（重要附注）**：

1. 产品现状 `mask_binary=True` 会**阈值化**用户遮罩；要启用软边，需让用户遮罩走 `mask_binary=False`
   分支（属产品改动，本实验未做）。
2. 软边语义是 ComfyUI 的**逐步 noise_mask 混合**（`x = latent·(1−m) + denoised·m`）：σ 越大，
   核心「满编辑区」越小（C 核心改动 7.21 < A 10.02）。**σ 不宜过大**（建议 ≤10），否则编辑强度明显下降。
3. 「是否更自然」的最终判断建议人眼确认：见 `mask_feather_sheet.png`（源/A/B/C 拼图）与
   `mask_feather_seam_zoom.png`（接缝放大）。

## 限制与遗留

- 设备峰值显存未采集（Dynamic VRAM + 3 次上限）。
- 提示词为固定一句，未针对图像内容定制；核心均值未出现明显蓝色偏移。
- 仅单张主图 / 单一矩形区域 / 单一 seed；结论为技术可行性判定，非画质调参。
- 未做「二值 + 后端羽化」对照（属另一路径）。

## 文件清单

- `mask_feather_exp.py` / `mask_feather_analyze.py`（脚本，保留）
- `mask_feather_A.png` / `mask_feather_B.png` / `mask_feather_C.png`（三组输出，保留）
- `mask_feather_mask_A/B/C.png`（三组遮罩）
- `mask_feather_sheet.png`（源/A/B/C 拼图）
- `mask_feather_seam_zoom.png`（接缝放大对比）
- 本报告
