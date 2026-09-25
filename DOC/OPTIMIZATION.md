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
| 1.7 | TE-Speed-QwenImage21（**外置模块**） | **已实测** | 端到端 **+12.3%**（1K）/ **+15.6%**（2K） | 无 LICENSE / 闭源 pyd / Windows x64；**默认关**，见 §1.7 |

### 1.7 TE-Speed-QwenImage21（外置加速模块 · 已实测 · 默认关）

> 第三方 ComfyUI 节点，用「输出预测」加速 Qwen-Image-2.1 采样。**外置模块语义**：
> 不进发布包；用户自行放置 / 开启 / 删除 / 更新。接入点见 `FROZEN.md`「TE-Speed 集成」。

- **来源**：https://github.com/tl2012tl/TE-Speed-QwenImage21
- **放哪**：`<ComfyUI>/custom_nodes/TE-Speed-QwenImage21/`（`__init__.py` + `nodes.pyd` + `README.md`）
- **怎么开**：环境变量 `ZIV_AI_TE_SPEED=1`（默认关，关闭时不加载 pyd）
- **怎么删**：删除该目录即拔掉（开关开着也不会报错，自动跳过）
- **怎么更新**：替换 `nodes.pyd` 即更新
- **参数**（env 覆盖）：`ZIV_AI_TE_SPEED_MODE`（`te_predictor` | `speed`，默认 `te_predictor`）、
  `ZIV_AI_TE_SPEED_THRESHOLD`（默认 0.06）、`ZIV_AI_TE_SPEED_ERROR_LIMIT`（默认 0.08）、
  `ZIV_AI_TE_SPEED_ATTENTION`（默认 `kitchen_int8`）、`ZIV_AI_TE_SPEED_VERBOSE`

**A/B 实测（2026-09-25；side 1024 / 2048，steps=30，seed=42，te_predictor/0.06，同图同 prompt）**：

| 档位 | A 基线 | B 加速 | 端到端加速 | MAD | PSNR | SSIM | 像素差 >120 |
|---|---|---|---|---|---|---|---|
| 1K（1024×640） | 9.66 s | 8.48 s | **+12.3%** | 2.15 | 27.64 dB | 0.9865 | 0.28% |
| 2K（2048×1280） | 44.94 s | 37.91 s | **+15.6%** | 0.80 | 38.53 dB | 0.9957 | 0.011% |

- 显存：peak alloc 相同；`nvidia-smi` 峰值 +~1.6%（2K），无 OOM；连续 3 次稳定（B 37.75–38.05 s）。
- **规律**：分辨率越高 → 加速越大且画质越接近（预测更准）。
- **结论**：+15.6% **< 30% 门槛** → **默认关**（保留开关；外置模块）。
- **限制**：无 LICENSE / 闭源 pyd / Windows x64 绑定 / attention 冲突未验（默认 `kitchen_int8` 实测未崩）。
- 脚本：`_test_step2/te_speed_ab.py` / `te_speed_analyze.py`；结果 `te_speed_ab_result.json`。

## 2. 提示词遵循优化

| # | 方案 | 状态 | 说明 |
|---|---|---|---|
| 2.1 | PE-I2I 重写器（9B） | 候选 | ~18.8 GB 显存；建议作**独立预处理**，不与主推理同时驻留 |
| 2.2 | 多参考图 `<imageN>` 语法 | **已支持** | 多图路径**待实测** |
| 2.3 | 提示词五层结构 | 使用建议 | 建议 **50–150 词**，分层描述主体 / 场景 / 风格 / 光影 / 约束 |

### 2.1 后期接入点（提示词重写 / 多图任务）

> 登记于 Step 5 补完；**仅记录接缝，不实现场景**（ARCHITECTURE.md §11 反过度设计）。

- **复用同一客户端**：提示词重写（PE-I2I 类）与多图任务规划**不需要新建客户端类**，
  统一复用 `ZivAiEditor.Backend.LocalLlmClient`（实现 `ILlmClient`）。
- **差异化配置**：每个场景构造自己的 `LlmClientOptions` 即可——
  规划用 `Temperature = 0.1`（稳定）、提示词重写用 `0.7`（更有创造性）、多图任务用 `0.2`；
  `MaxTokens` / `EnableThinking` / `Timeout` 同样按场景调整。
- **装配点**：`ZivAiEditor.App.AppContext`。当前只注册一个 planner 用实例
  （`LlmClient` 属性，见 `settings.ini` 的 `[llm.planner]` 段）；后期在此按场景构造额外
  `LocalLlmClient` 实例，`settings.ini` 预留了 `[llm.prompt_rewriter]` / `[llm.multi_image]`
  段注释。
- **不引入**：`ILlmClientFactory`、多实现框架等抽象；按场景构造 options 即可。

### 2.1.1 PromptOptimizer 定位（Step 6 登记）

> 登记于 Step 6（Executor）；**仅记录定位与接缝，不实现**（ARCHITECTURE.md §11）。

- **独立层，不是 Planner / Executor 的一部分**：PromptOptimizer 是 pipeline 的**前置步骤**，
  只负责把用户的简短输入**重写**为更详细、结构化的编辑提示词；它**不生成 `EditPlan`**
  （那是 Planner）、**不执行步骤**（那是 Executor）、**不改变 IPC 契约**。
- **实现方式**：调用 `ZivAiEditor.Backend.LocalLlmClient`（`ILlmClient`）重写 prompt；
  与 Planner 一样，C# 不加载 LLM 运行时（Z17），客户端由 App 层装配。
- **接入点（二选一，后续 Step 决定）**：
  1. **pipeline 的 `encode_prompt` 之前**（Python 侧）——在文本条件编码前对 prompt 做重写；
  2. **C# 侧提交前预处理**——在调用 `Executor.ExecuteAsync` 之前，先经优化器得到重写后的
     prompt，再交给 Planner / Tool。
  推荐 C# 侧：不侵入 Python、便于复用 `LocalLlmClient` 与独立 `LlmClientOptions`。
- **典型用法示例**：用户输入「换背景为茶肆」→ 优化器扩展为
  「古代中式茶肆室内，木质梁柱与灯笼，暖黄灯光，背景虚化，保留前景人物与服装细节」，
  再交给 Planner / `QW21edit`。
- **边界**：不新增契约；不改 Planner / Executor / `IInferenceClient`；不引入 factory
  （按场景构造 `LlmClientOptions`，`Temperature ≈ 0.7`）。

### 2.1.2 经典 img2img（从输入图 latent 部分去噪）（候选，非当前范围）

> 登记于 Step 6 收尾修正；**仅登记，不实现**（ARCHITECTURE.md §11 反过度设计）。

- **机制**：VAE encode 输入图作为**起始 latent**，`denoise < 1.0` 控制保留程度（部分去噪，
  保留原图结构）。
- **与当前 `QW21edit` 的区别**：
  - 当前**有 mask** 路径 = 输入图 latent + `noise_mask`（局部编辑）；
  - 当前**无 mask** 路径 = 纯噪声起点（`torch.zeros`）+ `reference_latents` 注入
    （**参考条件编辑**）；
  - **经典 img2img** = 输入图 latent 起点 + 部分去噪（保留原图结构）。
  - 三者都是「编辑」语义的不同实现；`QW21edit` **不包含**经典 img2img。
- **引入方式**：`pipeline._encode()` 增加显式 mode 分支 + IPC `submit.payload` 增加可选
  `mode` 字段（属跨进程契约修订，需走修订流程并实测画质 / 显存 / 耗时）。
- **风险**：Qwen-Image 系列在 ComfyUI 上有**已知未解决问题**
  （GitHub Issue **#9702** / **#10063**），社区尚未解决。
- **状态**：**待社区成熟后引入，不阻塞主线**。
- **关联**：Step 6 的 `QW21edit` 命名与语义均与此区分；无 mask 路径当前行为即参考条件编辑。

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

## 7. 工具能力候选（Step 7.5）

> 登记于 Step 7 可行性调研；**本步不实现、不注册**（ARCHITECTURE.md §11）。
> Qwen-Image-2.1 **原生支持 RGBA 抠图**（VAE 64 通道含 alpha，去噪直接生成透明度）；
> **超分无原生能力**，需独立模型。候选列入 **Step 7.5**。

> **更正（2026-09-22 · Segment 定位修正）**：本段初稿曾判定「Segment 去背景：❌ Qwen-Image-2.1
> 无原生 RGBA / alpha 输出，无法做真正的去背景；需独立模型 BiRefNet（~2.2GB）/ RMBG-2.0
> （~1.5GB）」。该判定**有误**：Qwen-Image-2.1 的 VAE 为 **64 通道 RGBA**，alpha 通道是潜空间
> 的一等公民，模型在去噪过程中直接生成透明度；ComfyUI v0.37.0 官方提供「Remove Background」
> 模板，从采样器出来即为透明 PNG，不需要 matting 模型或背景去除节点。以下按正确认识重写为
> **两条独立条目**（原生抠图 / 对已有 RGB 图的后处理抠图）；**原判断原文保留于本更正框内**。

### 7.1 Segment（`QW21segment`）—— 原生路径（Step 7.5 候选）

- **机制**：Qwen-Image-2.1 **原生 RGBA 输出**（VAE 64 通道含 alpha），生成时直接输出透明度。
- **实现**：走 `QW21edit` **同管线**，提示词含 `transparent background` / `RGBA` /
  `alpha channel`；C# 侧新增 `QW21segment` 工具
  （`Capabilities=["segment","remove-background","rgba"]`），输出透明背景 PNG。
- **依赖**：**无需新模型**。
- **状态**：**Step 7.5 候选**（本轮未实测）。
- **依据**：ComfyUI v0.37.0 官方「Remove Background」模板。

### 7.2 对已有 RGB 图抠图 —— 后处理路径（待真实需求）

- **机制**：**独立抠图模型**（BiRefNet ~2.2 GB / RMBG-2.0 ~1.5 GB）对**已有的 RGB 图**做分割，
  输出透明 PNG。
- **适用**：用户上传一张照片，要抠出主体（**不是**生成时透明）。
- **依赖**：`models/background_removal/` 权重（当前仅占位文件）；ComfyUI 核心
  `comfy_extras/nodes_bg_removal.py`（`LoadBackgroundRemovalModel` + `RemoveBackground`）。
- **定位**：与 7.1 **解决的问题相同**（得到透明背景 PNG），**机制不同**；二者**不叠加**，
  是**替代路径 / 独立入口**——BiRefNet / RMBG-2.0 **不是** Segment 的依赖。
- **状态**：待真实需求引入，**非当前范围**。

### 7.3 Upscale 放大（候选）

- **结论**：❌ Qwen-Image-2.1 非超分模型；「编辑指令精细化」本质是**重绘**不是放大。
  本步 UI 上的替代方案是「高质」tier（用户直接选 2048 出图），不引入伪放大。
- **ComfyUI 支持**：核心 `comfy_extras/nodes_upscale_model.py`
  （`UpscaleModelLoader` + `ImageUpscaleWithModel`，含分块 `tiled_scale` + OOM 自动降 tile）。
- **候选模型**（盘上已有，`C:\AI\ComfyUI_PIC\ComfyUI\models\upscale_models\`）：
  | 模型 | 倍率 | 显存（估） | 说明 |
  |---|---|---|---|
  | Real-ESRGAN x4plus | 4× | ~1–2 GB | 通用超分 |
  | 4x-UltraSharp | 4× | ~1–2 GB | 锐利，适合插画 |
  | Real-ESRGAN x2plus | 2× | ~1–2 GB | 低倍率 |
  | 4x_foolhardy_Remacri | 4× | ~1–2 GB | 摄影向 |
- **实现路径**：Python 侧 `op="upscale"` 分支调用 `ImageUpscaleWithModel`（可用
  `Scale` 模式或直接指定倍率）；C# 侧新增 `QW21upscale` 工具（`Capabilities=["upscale"]`）。
  与生成式编辑不同，超分不采样，可与主模型分时驻留（Z21）。

### 7.4 Outpaint Inpainting ControlNet（候选，未采取）

> 登记于 Step 7 outpaint 对齐官方工作流之后；**本步未采取**，后续有需求再评估。

- **背景**：官方蓝图 `ComfyUI/blueprints/Image Outpainting (Qwen-Image).json` 的完整链路为
  `ImagePadForOutpaint`（灰 0.5 + feathering）+ `Grow and Blur Mask` + **`ControlNetInpaintingAliMamaApply`**
  （`Qwen-Image-InstantX-ControlNet-Inpainting.safetensors`）+ KSampler。
- **Step 7 已采取（方案 A）**：仅对齐**画布填充 + 掩膜羽化/生长**两段，扩图走现有 `noise_mask`
  软掩膜路径（实测无黑边源图无缝外扩，中心 MAD 1.4 / corr 0.9994）。
- **未采取**：**Inpainting ControlNet 引导**（官方第三段）。
- **候选权重**（盘上已有，`C:\AI\ComfyUI_PIC\ComfyUI\models\controlnet\`）：
  `Qwen-Image-InstantX-ControlNet-Inpainting.safetensors`、
  `Qwen-Image-ControlNet-Union .safetensors`。
- **风险 / 前置**：官方蓝图面向 **Qwen-Image v1**（`qwen_image_fp8_e4m3fn` / `qwen_2.5_vl_7b` /
  `qwen_image_vae`）；我们用的是 **Qwen-Image-2.1**，**无官方 2.1 outpaint 模板**，v1 ControlNet
  与 2.1 的兼容性**未验证**。若引入需先做**小样实测**（画质 / 显存 / 耗时），再决定是否入管线。
- **触发条件**：当新区域与原图一致性要求提高、或方案 A 的软掩膜仍不满足时再评估。

### 7.5 划像对比的 outpaint 精确对齐（候选 · 登记于 Step 9C.2-C）

> **登记位置说明**：本项是「数据管道 / 特性完整性」候选，非性能优化，也非发布阻断项，
> 故登记于 `OPTIMIZATION.md` §7（工具能力候选）而非 `RELEASE-CHECKLIST.md`。
> 依据：Step 9C.2-C 前置调查结论（见 `DOC/DEVLOG.md` Step 9C.2-C）。

- **背景**：Step 9C.2-C 的划像对比在「父图与当前图尺寸不同」（典型：outpaint）时采用
  **简化对齐**——父图**居中**绘制在当前画布上，外扩区域以画布背景色填充，**不做**按实际
  offset 的精确摆放。
- **根因（不可得）**：outpaint 的几何信息（父图在输出画布中的 `offset` / 尺寸、`anchor`、
  目标画布尺寸）当前**未回传到 C#**：
  - `EditNode`（`ZivAiEditor.Agent/EditSession.cs`）只有 `NodeId` / `ParentNodeId` /
    `ImagePath` / `Command` / `CreatedAt`，**无几何字段**；
  - `ToolResult.Metadata`（`ZivAiEditor.Tools/QwenImage21OutpaintTool.cs`）只写 `task_id`；
  - `EditRequest.Anchor` 仅**上行**（请求 → 后端），`InferenceResultDetail` 只有
    `Width` / `Height` / `Seed`，**无源图 offset / 源图尺寸 / anchor 回传**；
  - `python/server/outpaint.py` 的 `anchor_position()` / `build_outpaint()` **确实计算**了
    `x / y / src_w / src_h / target_w / target_h`，但只落盘到临时 workdir，**result 帧不回传**；
  - `contracts/ipc-protocol.md` §3.2 `result` 帧字段为 `task_id` / `output_path`（实现另含
    `width` / `height` / `duration_ms` / `seed`），**无几何字段**。
- **精确对齐所需的改动（属冻结结构，须先获授权）**：
  1. `contracts/ipc-protocol.md` `result` 帧新增可选几何字段（如
     `source_rect` = `{x,y,w,h}` + `canvas` = `{w,h}`），升 `ipc_version`（向后兼容可选字段）；
  2. Python `handlers` / `pipeline.run_outpaint` 回传该字段；
  3. C# `InferenceResultDetail` / `ToolResult.Metadata` 承接；
  4. `EditNode` 扩展几何字段（**改冻结结构**）或在 App 侧旁路保存节点几何。
- **触发条件**：当 outpaint 结果的划像对比需要「像素级对齐外扩区域」时再评估；
  届时**必须先报告并获授权**（涉及 `EditNode` / IPC result 帧等冻结结构）。
- **当前实现**：简化对齐（居中 + 背景填充），见 `ZivAiEditor.App/Controls/CompareOverlay.axaml.cs`。
