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

## 8. WD14 Tagger（CPU 打标能力 · L1 已落地）

> 追加于 2026-09-25（L1）。**非速度优化**，登记为「已落地能力」。

- **能力**：对任意图片做 WD14 booru 标签识别（`wd-vit-tagger-v3`），输出标签列表。
- **实现**：外置节点 `custom_nodes/comfyui-wd14-tagger/`（模型 + 参考实现）+
  `python/server/tagger.py`（复刻推理核心，路线 A，绕开节点对 `PromptServer` / web 的依赖）。
- **运行**：**CPU only**（onnxruntime 1.30.0，`CPUExecutionProvider`），不占 GPU、不参与 Z18 串行队列。
- **默认开**：`ZIV_AI_TAGGER`（默认 `1`）；缺失 / 失败静默降级（返回空列表）。
- **边界**：L1 仅 Python 侧能力；无 IPC / C# 接入（L2 待契约授权）。

## 9. 冷启动与显存换页实测（插桩探针）

> 追加于 2026-09-28。**实测记录，非优化落地**。用于给「启动预热 / 显存换页」类优化
> 提供逐子阶段证据，取代此前只有「阶段总耗时」的粗粒度判断。

### 9.1 探针

- **脚本**：`_test_step2/cold_start_trace.py`（只读插桩，**零生产代码改动**）。
- **手法**：`monkey-patch` 包装真实调用点，采集每步 `perf_counter` + `nvidia-smi` + `torch.cuda.memory_allocated`：
  - `comfy.model_management.load_models_gpu`（权重搬运 + 显存分配）
  - `vae.encode` / `vae.decode`
  - `clip.encode_from_tokens_scheduled`（TE 前向，正 / 负各一次）
  - `comfy.sample.sample`（逐步回调）
- **运行**：
  ```powershell
  D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\cold_start_trace.py
  D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\cold_start_trace.py --warm
  ```
  解析 stdout 的 `TRACE_JSON <json>`。
- **环境**：Windows / RTX 4080 16GB（基线 1058 MiB）/ ComfyUI v0.37.0（Python 3.13.14、torch 2.13.0+cu130）；
  side=512、steps=8、seed=42、`image_path`=纯灰图；Dynamic VRAM 默认启用。

### 9.2 冷启动时间线（首次生成，进程冷态）

| 阶段 | 自起耗时 | 累计 t | 显存 (MiB) |
|---|---|---|---|
| 解释器 + PIL + 后端模块导入 | 1.66 s | 1.72 | 1060 |
| **`prepare_environment`**（import comfy/torch + DynamicVRAM init） | **4.13 s** | 5.89 | 1058 → 1227 |
| **`ensure_loaded`**（dit 0.04 / te 0.32 / vae 0.30） | **0.81 s** | 7.22 | 1227（**零变化**） |
| 推理总计 | **6.30 s** | 13.60 | |
| └ `vae.encode` | 0.43 s | 7.89 | 1227 → 1975 |
| └ **`clip.encode`（正）** | **1.77 s** | 9.74 | 1285 → **10106** |
| └ `clip.encode`（负） | 0.48 s | 10.31 | 10106 |
| └ `sample`（含 DiT 上卡） | 2.52 s | 12.98 | 10106 → 1322 → 9276 |
| └ `vae.decode` | 0.39 s | 13.50 | 9278 → 3726 |

### 9.3 关键发现

1. **显存爆发点是 TE 前向，不是 `moving_to_gpu`**：第一次 `clip.encode`（正提示词）一次把显存
   1285 → **10106 MiB（+8.8 GB）**——8.71 GB 的 `qwen3vl_8b_int8` 文本编码器在**首次 TE forward**
   才真正上 GPU。所有 `load_models_gpu` 调用本身仅 0.02–0.10 s。
2. **`ensure_loaded` 不碰显存**（1227 全程不变）：只是把 ~16 GB 权重（DiT 6.76 + TE 8.71 + VAE 0.63）
   读进 host RAM，供 Dynamic VRAM 按需换入。
3. **Dynamic VRAM 每次 run 都换页**：run 结束 `vae.decode END` 后显存回落 3726，TE 与 DiT 均被 evict。
   第二次（warm）仍要重新搬 8.8 GB TE + ~8 GB DiT（走 PCIe），故 warm 仅比冷快 ~1 s。
   16 GB 卡装不下 TE 8.8 + DiT 8.0 = 16.8 GB，**换页是结构性的**。

| 子阶段 | 冷 | 热（warm） |
|---|---|---|
| `clip.encode`（正） | 1.77 s | 1.50 s |
| `sample` | 2.52 s | 2.20 s |
| **推理总计** | **6.30 s** | **5.34 s** |

### 9.4 优化方案验证结论

| 方案 | 实测可省 | 证据 | 结论 |
|---|---|---|---|
| **背景预热 `prepare_environment`** | **4.13 s** | 纯 CPU；显存仅 CUDA context（1227）；发生在首次 `submit` 之前 | ✅ 有效，移出用户等待路径 |
| **背景预读 `ensure_loaded`** | **0.81 s** | 显存零变化，仅读盘 | ✅ 有效（代价：host RAM 常驻 ~16 GB） |
| **解释器 + 导入常驻**（进程不重启） | **1.66 s** | `probe_start` → `import_backend_end` | ✅ 有效 |
| **进程常驻复用**（第二次起） | 每图省 **~6.6 s**（上三项之和） | warm 5.34 s vs 冷 12.0 s | ✅ 最大项 |
| 预搬权重上 GPU / 保持常驻 | ≈ 0 | 权重必被 Dynamic VRAM evict，16 GB 装不下 | ❌ 无效 |
| 改 `comfy_kitchen.tensor.base` 去掉 `torch._dynamo` import | ~1.0 s（仅首次） | 属 site-packages 外部依赖 | ⚠️ 不建议（升级即坏） |

**结论**：首次生成可从 ~13.6 s 降至 ~6.3 s（把 4.94 s 预热 + 1.66 s 导入移出等待路径）；
**每图 5–6 s 的下限**由 Dynamic VRAM 的 TE/DiT 换页决定，属独立课题——候选方向见 §10。

## 10. 冷启动 / 换页优化候选（源码核查 + 联网）

> 追加于 2026-09-28。针对 §9 暴露的两个问题：(a) 首次 `import comfy/torch` 4.13 s；
> (b) 每图 TE(8.8 GB)+DiT(6.8 GB) 无法同驻 16 GB 卡 → 每次生成都重新 fault。
> **依据**：本地 ComfyUI v0.37.0 源码（`comfy/model_management.py`、`comfy/cli_args.py`、
> `comfy/text_encoders/qwen_image21.py`）、`comfy_aimdo` 0.5.5 官方 README
> （https://github.com/Comfy-Org/comfy-aimdo ）。搜索引擎已降级，未采用二手结论。

### 10.1 已核实的机制（事实）

| 事实 | 证据 |
|---|---|
| Dynamic VRAM = **整模型级**按需 offload（VBAR fault/unpin） | `comfy_aimdo` README「VBAR allocator / fault() / unpin()」 |
| TE 初载设备恒为 CPU（aimdo 下） | `model_management.text_encoder_initial_device()`：`aimdo_enabled → offload_device` |
| TE 计算设备为 GPU | `model_management.text_encoder_device()`：`aimdo_enabled → get_torch_device()` |
| 权重被 evict 后**watermark 抬高**，后续 fault 直接失败 → 每次走临时张量重拷 | `comfy_aimdo` README「Having a weight evicted sets that VBAR's watermark… automatically fail the fault()」 |
| `prioritize()` 可把已加载模型推回最高优先级、重置 offload watermark | `comfy_aimdo` README「Priorities」 |
| Qwen-Image-2.1 **硬绑定 8B TE**（4096 维） | `text_encoders/qwen_image21.py`：`model_type="qwen3vl_8b"` 硬编码；`qwen3vl.py:156` 4B=2560 维、8B=4096 维 |
| **cfg==1.0 时负条件不被采样器消费** | `comfy/samplers.py:610`：`math.isclose(cond_scale, 1.0) → uncond_ = None`；`CFGGuider.predict_noise` 允许 negative=None |
| **TE BF16 单体重 16.33 GB**（> 16 GB 卡） | HF `abenzerps/Qwen-Image-2.1-Uncensored-GGUF` `text_encoders/qwen3vl_8b_bf16.safetensors` = 17,533,000,000 B ≈ 16.33 GiB |
| 本地 **未装** `ComfyUI-GGUF` / `gguf` 包；comfy 核心无 GGUF ops | `custom_nodes/` 无 ComfyUI-GGUF；site-packages 无 `gguf`；`comfy/ops.py` 无 GGML 引用 |
| host RAM 非瓶颈：96 GB 总 / 85 GB 空闲，pinned 上限 = RAM×40% ≈ 38 GB | 本机 `Win32_ComputerSystem`；`model_management.py:1617` |

### 10.2 候选方向（按性价比排序）

| # | 方向 | 预期收益 | 风险 / 前置 | 状态 |
|---|---|---|---|---|
| 10.2.1 | **应用启动后台预热** `prepare_environment()`（可叠加 `ensure_loaded`） | 首次生成 **−4.9 s** | 低；纯 CPU，不碰 GPU（§9.4 已验证） | ✅ **已落地**（见 §12.2） |
| 10.2.2 | **cfg==1.0 时跳过负提示词 encode** | 每图 **−1.3 s**（1024）/ 更多@2K | 低；**已核实** cfg=1.0 时负条件被 `sampling_function` 丢弃（`samplers.py:610`）。`_encode` 现对正负各做一次 TE 前向 + 视觉处理，负方向可省（negative 传 None/复制 positive） | ✅ **已落地**（见 §12，实测输出零变化） |
| 10.2.3 | **TE 换 w4a8**（Comfy-Org 官方 `qwen3vl_8b_w4a8.safetensors` **5.88 GiB**，ComfyUI 原生 `asym_w4a8_int8`） | TE 8.71→5.88：与 DiT int8(6.76) 合计 **13.27 GiB → 16 GB 同驻**，消除阶段间换页 + 传输减 33% | 中；**数据替换即可（改 `models.json` 的 `te_path`，无需改代码）**；W4A8 更激进，画质须 A/B | **首选候选**（现成权重，见 §10.7） |
| 10.2.4 | **DiT Q4/Q5 GGUF**（§1.6 已有条目，此处补新理由 + 现成权重） | DiT 6.76→**4.29**（Q4_K_M）/ **3.87**（Q4_0）GiB，与 TE int8(8.71) 合计 **13.6 / 13.2 GiB** → **可同驻 16 GB**，消除每图重 fault | 中高；**需装 `ComfyUI-GGUF` + `gguf` 包**，且 GGUF 的 patcher 会**顶掉 Dynamic VRAM**（见 §10.6）；画质需 A/B | **降级**（详见 §10.6） |
| 10.2.5 | **`prioritize()` 保持 DiT 常驻**（跨 run） | 省 DiT 重 fault（~0.3–0.5 s/图） | 高；若 DiT(6.8) 常驻则 TE(8.8) 流式需 15.6 GB + 激活，16 GB 卡边缘，可能触发 TE 走临时张量反变慢 | 候选，需实测 |
| 10.2.6 | **提示词条件缓存**：按 (prompt, 图 refs 哈希, 分辨率) 缓存 `(positive, negative)` | 重复 prompt（模板命令）每图 **−1.5–1.8 s** | 低；仅精确重复命中，自由输入收益有限；需有界缓存（Z6） | 候选 |
| 10.2.7 | **`--vram-headroom` / `--cache-ram` 调参** | 未知，可能降低换页抖动 | 低；纯配置实验 | 可试 |

### 10.3 已排除方向（证据）

| 方向 | 排除理由 |
|---|---|
| 4B TE 替换（省 4 GB） | **架构不兼容**：Qwen-Image-2.1 DiT 要求 8B TE 的 4096 维隐状态；4B 为 2560 维 |
| **TE BF16 替换（升精度）** | **可运行但无收益**：TE 本就卸到内存流式（§10.7），16.33 GiB 不构成硬墙；但每次 prompt 前向要搬 16.33 GiB（int8 的 ~1.9×）→ 更慢，且 **int8 vs BF16 无任何公开画质对比**。要缩小 TE 应用 `w4a8`（10.2.3），不是 BF16 |
| `--gpu-only` 让全部权重常驻 GPU | TE 8.8 + DiT 6.8 + VAE 0.6 = 16.2 GiB 权重本身即 ≥16 GB，必然 OOM |
| `--lowvram`（TE 跑 CPU） | 该开关在 **Dynamic VRAM 启用时无效**（`cli_args.py:170`）；且 CPU 前向远慢于 1.5 s |
| 改 `comfy_kitchen` 去 `torch._dynamo` import | site-packages 外部依赖，升级即坏；且仅首次 ~1 s（§9.4） |
| 预搬权重上 GPU 常驻 | 被 §9.3 / 10.1 watermark 机制证伪 |

### 10.4 结论

- **立即可做且已验证**：10.2.1（启动预热）——把首次生成 ~13.6 s 压到 ~6.3 s。
- **每图下限的破解点**是让 TE 与 DiT **同驻**：10.2.3（TE Q4，需自造权重）或 10.2.4（DiT Q4 GGUF，
  有现成权重但会顶掉 Dynamic VRAM，见 §10.6）二选一即可让权重总和 < 16 GB。**须先做画质 A/B**
  与「GGUF+aimdo 混用是否退化」验证（沿用 §1 的 MAD / PSNR / SSIM 口径）。
- 10.2.2（跳过负条件）是**唯一不依赖量化/换模**的确定收益项，**已核实机制**（`samplers.py:610`），建议优先 GPU 实测。
- **优先排序（据 §10.5/§10.6/§10.7 核查）**：10.2.1 → 10.2.2 → **10.2.3（TE 换 w4a8，现成权重、零依赖、数据替换）** →（每图快则）采样加速 §1.4 → 最后才 10.2.4（GGUF）。
- **TE 精度的正确取向（§10.7）**：要省要快 → `w4a8`（5.88 GiB）；要画质 → 只能 BF16（16.33 GiB，靠内存卸载，更慢），且**无公开画质对比**，收益未证实。

### 10.5 参考仓库：Qwen-Image-2.1-Uncensored-GGUF

> https://huggingface.co/abenzerps/Qwen-Image-2.1-Uncensored-GGUF （Qwen-Image-2.1 的量化发行；
> 源权重 `Qwen/Qwen-Image-2.1`，TE/VAE 源 `Comfy-Org/Qwen-Image-2.1`；转换自 leejet/stable-diffusion.cpp）。

**DiT 权重（同架构、仅精度不同）**：

| 量化 | 文件大小 (GiB) | TE(int8 8.71) + DiT + VAE(0.63) | 16 GB 可同驻 |
|---|---|---|---|
| int8_convrot（**当前在用**） | 6.76 | 16.10 | ❌ 换页 |
| fp8 | 6.63 | 15.97 | ❌ 边缘 |
| Q8_0 | 7.07 | 16.41 | ❌ |
| Q6_K | 5.47 | 14.81 | ⚠️ 紧张 |
| Q5_K_M | 4.86 | 14.20 | ⚠️ 可行 |
| **Q4_K_M**（仓库推荐） | **4.29** | **13.63** | ✅ |
| Q4_0 | 3.87 | 13.21 | ✅ |
| NVFP4 | 3.77 | — | ⚠️ Ada(4080) 无原生 FP4 |

**Text Encoder**：仓库**仅**提供 `qwen3vl_8b_bf16`（16.33 GiB，装不下）与 `qwen3vl_8b_int8_convrot`
（8.71 GiB，即当前在用）。**无 Q4 TE**。

**接入前置**：GGUF 需 `ComfyUI-GGUF`（leejet fork，原生支持 Qwen-Image 2.1）+ `gguf` pip 包；
本机两者均未安装，且 comfy 核心不含 GGUF ops——`model_loader.load_dit` 需改为走 GGUF 加载
（复刻节点的 `gguf_sd_loader` + `custom_operations` 路径）。

### 10.6 GGUF DiT 在 in-process 管线中的可行性核查（2026-09-28）

> 结论先行：**技术上可加载，但收益小且与 Dynamic VRAM 冲突，不作为优先项**。证据如下。

**可加载路径（已读源码确认）**：`ComfyUI-GGUF`（leejet/city96 fork）提供三个纯 Python 件，
可在 in-process 中复用，无需节点服务：

```python
from ComfyUI_GGUF.loader import gguf_sd_loader       # GGUF → {name: GGMLTensor}
from ComfyUI_GGUF.ops import GGMLOps, GGUFModelPatcher
sd, extra = gguf_sd_loader(unet_path)
model = comfy.sd.load_diffusion_model_state_dict(
    sd, model_options={"custom_operations": GGMLOps()}, metadata=extra.get("metadata", {}))
model = GGUFModelPatcher.clone(model)
```

**冲突点（关键）**：

1. `comfy/sd.py:2389`：`load_diffusion_model_state_dict` 在**未**传 `disable_dynamic` 时返回
   `CoreModelPatcher`——本项目 `model_loader._enable_dynamic_vram()` 已把它改成 `ModelPatcherDynamic`。
2. `ComfyUI-GGUF/nodes.py` 调 `GGUFModelPatcher.clone(model)`；`clone` 实现里临时把
   `self.__class__` 设为 `GGUFModelPatcher` 再走 `ModelPatcher.clone`，彼时 `is_dynamic()` 返回
   **False**（`model_patcher.py:403`），最终产物是 **`GGUFModelPatcher`（legacy 家族）**，
   **Dynamic VRAM 行为被顶掉**。
3. GGUF 与 aimdo 是两套重叠的「权重留 host、按需上台」设计（GGUF 靠 `GGMLOps` 逐层 dequant；
   aimdo 靠 vbar fault）——混用属未验证组合。

**收益核算（为何小）**：GGUF 只能缩小 **DiT**，而本项目较大的那个是 **TE（int8 8.71 GB）**，
仓库无现成 Q4 TE。且每图省下的只是**重 fault 的搬运**（§9.3 ~0.5–1 s），
真正的 5–18 s 主体是**不可约的算力**（TE 前向 + N 步采样 + VAE decode）。

**排除 / 降级理由**：

| 点 | 说明 |
|---|---|
| 冲突 Dynamic VRAM | §6.2 实测 Dynamic VRAM 是最大收益项（峰值 −2.9 GB、冷跑 −53%）；换 GGUF 大概率要关掉它 |
| 收益小且与分辨率无关 | 重 fault ~0.5–1 s/图；1536/25 步生产档（§6.2 热跑 18.13 s）占比 ~4% |
| 新增依赖 | 需 `gguf` pip 包 + `ComfyUI-GGUF`（违反 ARCHITECTURE §11「优先少依赖」）|
| 画质风险 | Q4_0 对图像模型偏激进；仓库推荐 Q4_K_M（4.29 GiB）也要 A/B |
| TE 仍是大头 | 要根治换页必须动 TE（10.2.3 自造 Q4 TE，含视觉塔，复杂），GGUF DiT 解决不了 |

**判定**：若目标是**首次生成快** → 用 10.2.1（预热，已证 −4.9 s），与 GGUF 无关。
若目标是**每图快** → 用采样加速（§1.4 Lightning LoRA / 降步数 / 缓存），量级远大于 GGUF。
**GGUF DiT 仅在「16 GB 卡跑 2048 且受 OOM 限制」时才值得单独实测**，且必须先验证
GGUF+aimdo 混用是否会退化。

### 10.7 社区实证：TE 卸载到内存 vs 16 GB（2026-09-28）

> 针对「BF16 TE 在 16 G 卡上是否可行」。**结论：机制上可行（TE 本就卸到内存流式），
> 但 BF16 TE 更大更慢；更值得下的是官方 `qwen3vl_8b_w4a8`（5.88 GiB）。**

**A. TE 卸载到内存是官方/社区的既定做法（不是权宜之计）**

- **SGLang 对消费卡的官方推荐布局**（经 `ai.rs` 转述）：*「Offload the text encoder. It runs once
  per prompt, so it can live in system RAM and stream through the card layer by layer while the
  DiT and VAE stay resident.」* —— TE 放内存、逐层流过显卡，DiT 常驻。
- **全 BF16 可在 2 GB 显存运行**：`HF Qwen/Qwen-Image-2.1 discussions/33`（nihui）的
  `qwenimage-ncnn-vulkan`，33 GB 全 BF16「model data backed by system memory」，GTX 1060 都能跑。
  → **模型体积 vs 显存不是硬墙**，流式即可。
- **16 GB 实机验证（int8 组合）**：`discussions/35`（TheTechnoX，RTX 4060 Ti 16GB）跑
  int8 TE + int8 DiT + BF16 VAE，**无 CPU/磁盘卸载**，峰值 ~15 GB、生成期 13–14 GB、出图 ~20 s。
- 社区按**系统内存**挑 TE 版（r/StableDiffusion `which_text_encoder_for_32gb_system_ram`）。

**B. Comfy-Org 官方 TE 清单（实测尺寸，GiB）**

| 文件 | 大小 | ComfyUI 原生 | 备注 |
|---|---|---|---|
| `qwen3vl_8b_bf16.safetensors` | **16.33** | ✅ | 参考精度；比 DiT 还大 |
| `qwen3vl_8b_int8_convrot.safetensors` | 8.71 | ✅ | **当前在用** |
| **`qwen3vl_8b_w4a8.safetensors`** | **5.88** | ✅（`quant_ops.py:252` `asym_w4a8_int8`）| **无需 GGUF / 新依赖** |

**C. BF16 TE 的代价（回答"16 G 用 BF16 TE"）**

- 显存：不必常驻（卸内存），**可行**；但要 **16.33 GiB 系统内存**（本机 96 GB 无压力）。
- 速度：每次 prompt 前向流式传输 **16.33 GiB**，约为 int8（8.71）的 **~1.9×** → 编码更慢
  （int8 实测 `clip.encode` 1.5–1.8 s，BF16 估 ~2.5–3 s）。
- 收益：**无任何公开的 int8 vs BF16 画质对比**（`ai.rs` 明确指出）；收益未被证实。

**D. 对 §9.3 换页问题的真正解法：换 `w4a8` TE（而不是 BF16）**

| 组合 | TE | +DiT | +VAE | 合计 (GiB) | 16 GB 同驻 |
|---|---|---|---|---|---|
| 当前 | int8 8.71 | int8 6.76 | 0.63 | 16.10 | ❌ 换页 |
| **w4a8 TE + int8 DiT** | **5.88** | 6.76 | 0.63 | **13.27** | ✅ 余 ~2.7 GB |
| w4a8 TE + BF16 DiT | 5.88 | 13.25 | 0.63 | 19.76 | ❌ |
| `ai.rs` 报的"可达 16 G"组合 | w4a8 5.88 | NVFP4 3.87 | 0.63 | 10.38 | ✅（NVFP4 需 Blackwell） |

- **`w4a8` 同时优于 GGUF 路线（§10.6）**：同为 ComfyUI 原生 safetensors（不用装 `gguf` +
  `ComfyUI-GGUF`、不顶掉 Dynamic VRAM），体积比 int8 小 33%。
- **前置**：`w4a8` 是 W4A8（比 W8A8 更激进），画质风险高于 int8；**须 A/B**（MAD/PSNR/SSIM）。
- 接入：数据替换 `Template/models.json` 的 `te_path` + 下载权重即可，**无需改代码**。

#### 10.7.1 实测：BF16 TE（`qwen3vl_8b_bf16_heretic` 16.33 GiB）vs int8

> 探针 `_test_step2/cold_start_trace.py`；`ZIV_AI_TE_PATH` 覆盖 TE；side 512/1024/1536、steps=8、
> seed=42、Dynamic VRAM 默认；RTX 4080 16GB（卡基线 ~1.5 GB）。

| 用例 | `clip.encode`(正) | 推理总时长 | `run` 总 | **卡峰值 (MiB)** | 占 16 GB |
|---|---|---|---|---|---|
| int8 @512 | 1.73 s | 6.14 s | 6.14 s | 10418 | 64% |
| **bf16 @512** | **2.92 s** | **8.10 s** | 8.10 s | **15522** | **95%** |
| int8 @1024 | 1.95 s | 9.05 s | 9.05 s | 12234 | 75% |
| **bf16 @1024** | **3.35 s** | **11.48 s** | 11.48 s | **15510** | **95%** |
| bf16 @1536 | 4.34 s | 21.26 s | 21.26 s | 15536 | 95% |

- **结论（实测）**：
  1. **BF16 TE 在 16 GB 上能跑**（512 / 1024 / 1536 均无 OOM、无降级）——证实「TE 卸内存流式」
     的判断；但 **VRAM 冲顶到 ~15.5 GB（95%）**，仅余 ~840 MiB。
  2. **TE 前向 ~1.7× 慢**（1.73→2.92 / 1.95→3.35 / 1536→4.34 s），推理总时长 **+27~32%**。
  3. 峰值 15.5 GB **与分辨率几乎无关**（TE 主导），说明 aimdo 把能塞的 TE 都 fault 进显存了；
     分辨率再升（**2048 是 `models.json` 的 native/high_quality 档**）激活增长 → **OOM 风险高**。
  4. 收益（画质）：**仍无 int8 vs BF16 的公开对比**，本次未做画质 A/B。
- **建议**：若为「无审查」内容且接受 95% VRAM → BF16 可用，但**避开 2048**；若为省显存/提速 →
  用 `w4a8`（5.88 GiB，本身就比 int8 小）。若两者都要 → 先做画质 A/B 再定。

#### 10.7.2 画质 A/B：难提示词（多语言文字 + 复杂布局）

> 提示词 `_test_step2/../hard_prompt.txt`（赛博朋克夜市：中文「拉面 / OPEN 24H / 修理中」+
> 复杂多物体布局 + 材质/反射约束）；1024²、steps=20、seed=42、同输入图；int8 vs bf16(heretic)。

| 项 | int8 | bf16 |
|---|---|---|
| `clip.encode`(正) | 2.06 s | **3.21 s**（1.56×） |
| `sample`（20 步） | 7.99 s | 8.17 s |
| `run` 总 | 13.52 s | **14.99 s**（+10.9%） |
| 卡峰值 | 12373 MiB | **15511 MiB**（95%） |

**数值差异（同 seed，int8 vs bf16）**：MAD **6.06**、PSNR **22.74 dB**、SSIM **0.892**、
像素差 >120 占 **0.52%**。
→ 远大于「近无损」（对照 §1.7 TE-Speed 的 PSNR 27.6/38.5 dB、SSIM 0.987/0.996）；
说明 **int8 量化确实显著改变了条件**（而非微小扰动）。

**人工目视（两图并排）**：构图、要素、可读文字（拉面 / OPEN 24H / 修理中 / 红灯笼）**基本等价**，
差异仅在次要元素位置（招牌位置、火花方向、背景广告牌）——**int8 不输 bf16**。
即 PSNR 低是扩散轨迹发散，**不是画质下降**。

**结论**：本组（1 提示词 × 1 seed）**未见 BF16 的画质优势**，而代价是 **+11% 时长、VRAM 95%**。
→ 用 `int8`（当前）或 `w4a8`（更小）即可；BF16 仅在明确获「无审查」等收益时采用。
（局限：单提示词单 seed，非盲评；如需定论应多提示词 × 多 seed 盲测。）

## 11. DiT 量化对比：UC int8 vs UC fp8 vs 原版 int8

> 追加于 2026-09-28。`models/diffusion_models/image2/` 三个 DiT；**TE 固定 int8**、难提示词（§10.7.2）、
> 1024²、steps=20、seed=42。脚本 `_test_step2/cold_start_trace.py`（`ZIV_AI_DIT_PATH` 换 DiT）。

### 11.1 速度 / 显存

| DiT | `encode`(正) | **`sample`(20 步)** | `decode` | `run` 总 | 卡峰值 (MiB) |
|---|---|---|---|---|---|
| 原版 int8_convrot | 2.04 s | **7.91 s** | 0.60 s | **13.43 s** | 12319 |
| UC int8_convrot | 2.03 s | **8.46 s** | 0.61 s | **14.00 s** | 12521 |
| **UC fp8** | 2.03 s | **10.93 s** | 0.60 s | **16.49 s** | 12386 |

- **int8 明显比 fp8 快：采样快 ~29%（7.9–8.5 s vs 10.9 s），整图快 ~2.5 s，显存还略低。**
  （fp8 文件 6.63 GiB、int8 6.76 GiB，体积无差。）
- 推测原因（未深究）：`int8_convrot` 走 `comfy_kitchen` 的 INT8 优化核；fp8 路径在本机
  可能落到 dequant/回退，反而更慢。**结论：RTX 4080 上选 int8。**

### 11.2 画质差异（同 seed，MAD / PSNR / SSIM）

| 对比 | MAD | PSNR | SSIM | 差>120 占比 | 目视 |
|---|---|---|---|---|---|
| **UC int8 vs UC fp8**（纯量化差） | 7.52 | 21.52 dB | 0.880 | 0.78% | 构图/文字/质量**等价** |
| **原版 int8 vs UC int8**（模型差） | 2.83 | 28.72 dB | **0.960** | 0.09% | **几乎无差** |
| 原版 int8 vs UC fp8 | 7.30 | 21.74 dB | 0.886 | 0.76% | 等价 |

- **UC（无审查微调）与原版 int8 质量几乎一致**（PSNR 28.7 dB / SSIM 0.96）：构图、可读文字
  （拉面 / OPEN 24H / 修理中）、材质反射**目视无差**；仅背景广告牌等次要元素位置略异。
- 有意思：**量化格式变化（int8↔fp8，PSNR 21.5）比 UC 微调（PSNR 28.7）更改变输出**，
  但三者目视质量等价——**PSNR 低 ≠ 画质差**（扩散轨迹发散）。
- 结论：**要 UC 且要快 → UC int8**；fp8 无速度/显存/画质优势。

### 11.3 2K / 25 步复核（排除低步数/低分辨率的偏移）

> 同上三 DiT，改为 **2048²、steps=25**（TE 仍 int8），验证 §11.1/11.2 结论是否受"步数/分辨率"影响。

| DiT | `encode`(正) | **`sample`(25 步)** | `decode` | `run` 总 | 卡峰值 (MiB) |
|---|---|---|---|---|---|
| 原版 int8 | 8.19 s | **59.14 s** | 1.90 s | **79.28 s** | 15445 |
| UC int8 | 8.25 s | **63.74 s** | 2.02 s | **83.94 s** | 15500 |
| **UC fp8** | 8.20 s | **77.29 s** | 2.54 s | **98.35 s** | 15468 |

- **结论不变、且差距更大**：2K/25 步下 fp8 采样比 int8 **慢 21%**（77.3 vs 63.7 s），整图 **+17%**。
- UC int8 比原版 int8 慢约 7–8%（1024 时也约 7%，**可复现**；同为 6.76 GiB int8_convrot，
  原因未查明，疑权重布局触发不同 kernel 路径——非量化差）。
- 三者峰值都 ~15.5 GB（94–95%，Dynamic VRAM 顶到卡上限，**无 OOM、无降级**）。

| 2K 对比 | MAD | PSNR | SSIM | 目视 |
|---|---|---|---|---|
| UC int8 vs UC fp8 | 7.63 | 23.27 dB | 0.879 | 等价（fp8 版人物多了裙装，属随机差异） |
| **原版 int8 vs UC int8** | 3.99 | **25.86 dB** | **0.936** | **几乎无差** |
| 原版 int8 vs UC fp8 | 7.27 | 23.38 dB | 0.888 | 等价 |

- **2K/25 步复核后结论与 §11.1/11.2 一致**：int8 比 fp8 快得多；UC 与原版质量无差别。

### 11.4 「UC（Uncensored）」标签核查（为何下载量高却与官方无差）

> 依据：`blog.laozhang.ai/en/posts/qwen-image-2-1-nsfw`（2026-09-24）、HF 仓库元数据、
> `LOW-VRAM.md`、HF discussions #35/#9。

**事实（可查证）**：

- **官方 Qwen-Image-2.1 本地权重本身就"无审查"**：Diffusers `model_index.json` **没有 `safety_checker`
  组件**；模型卡与 GitHub README 均无内容政策。**过滤只存在于阿里云托管服务**（HF 官方 demo Space
  转发到 `poc-dashscope.aliyuncs.com`、Model Studio API）——那里 `prompt`/`negative_prompt` 及
  **输出**都会被审核（`DataInspectionFailed`）。
- **"Uncensored GGUF" 是纯量化，未做任何权重修改**：发布当天 4 个同名仓库 `Qwen-Image-2.1-Uncensored-GGUF`
  （不同账号）**SHA-256 完全相同**（同一份上传复制 3 份）；其卡片自述 *"quantizations of the original
  Qwen-Image-2.1 weights; no fine-tuning, abliteration, or other weight modification was applied"*。
  → **没有"拒绝层"可去**，X 上那条 ~1800 赞的"refusal layer removed"是误传。
- **它真正提供的价值是 GGUF 小体积**（Q4_0 4.05 / Q4_K_M 4.6 GB 等），服务低显存用户，**与审查无关**。
- HF 30 天数据：`abenzerps/…Uncensored-GGUF` **likes 2114 / 下载 964,220**；官方 `Qwen/Qwen-Image-2.1`
  仅 **52,804**（官方可用文件在 `Comfy-Org/Qwen-Image-2.1` = 3,987,373）。
  `abenzerps/Qwen-Image-2.1-GGUF` 与 `…Uncensored-GGUF` **是同一 id**（改名）。

**结论（回答"不如官方为何还有人用"）**：

1. **前提不成立——UC ≈ 官方**（同一 base 权重的量化副本）。本仓库实测印证：UC int8 vs 原版 int8
   PSNR 25.9 / SSIM 0.936，**目视无差**（§11.3）。差异来自"两次独立量化"的数值扰动，被扩散放大。
2. **高下载 = 命名营销 + 真实需求叠加**：①「Uncensored」抓住 NSFW 搜索流量（而官方本地本就不拦）；
   ② GGUF Q4 满足低显存。
3. **实践建议**：**要 UC 效果不必找"UC"仓库**——本地跑官方权重即可（官方无过滤）；要省显存用官方
   `Comfy-Org` 的 `w4a8`/int8 或可信 GGUF。**"Uncensored" 标签本身不带来画质或能力增益**。
   （注意：托管服务仍会过滤，拒绝是运营策略而非模型故障。）

## 12. 冷启动 / 换页优化落地记录

> 追加于 2026-09-28。记录已实施优化项的实测前后对比。

### 12.1 ✅ 10.2.2 — cfg==1.0 跳过负提示词 encode（已落地）

- **改动**：`python/server/pipeline.py` `_run_once` 计算 `cfg`，当 `math.isclose(cfg, 1.0)`
  （**与采样器同一谓词**，`samplers.py:610`）且开关开时 (`config.SKIP_NEGATIVE_AT_CFG1`，
  env `ZIV_AI_SKIP_NEGATIVE`，默认 1）→ `encode_prompt(..., need_negative=False)` → `_encode`
  跳过负向 TE 前向（含视觉塔），复用 positive 作占位。采样器在 cfg≈1 时丢弃 uncond，故语义不变。
- **回退**：`ZIV_AI_SKIP_NEGATIVE=0` 恢复旧行为。

**实测（1024²、steps=20、难提示词、seed=42、int8 三件套）**：

| | `clip.encode` 次数 | `clip.encode` 耗时 | `run` 总 | 卡峰值 |
|---|---|---|---|---|
| `SKIP_NEGATIVE=1`（新） | **1** | 2.15 s | **12.31 s** | 12925 MiB |
| `SKIP_NEGATIVE=0`（旧） | 2 | 2.07 + **1.34** s | 13.25 s | 12784 MiB |

- **输出零变化**：两图 MAD **0.0** / PSNR **inf** / SSIM **1.0**（cfg=1.0 丢弃 uncond，符合预期）。
- **净收益**：每图 **−0.94 s**（1024）；`clip.encode` 由 2 次降为 1 次，省 ~1.34 s。

> **待填**：10.2.1（启动预热）实施后回填；10.2.4 画质 A/B。

### 12.2 ✅ 10.2.1 — 应用启动后台预热（已落地）

**改动**（两层，均可回退）：

| 层 | 文件 | 内容 |
|---|---|---|
| Python | `config.py` | `PREWARM`（env `ZIV_AI_PREWARM`，默认 1）、`PREWARM_LEVEL`（默认 0，安全的容错解析） |
| Python | `main.py` | 连上管道后 `_start_prewarm()` → 后台守护线程调 `model_loader.prepare_environment()`（LEVEL≥1 再 `ensure_loaded`，非默认） |
| Python | `model_loader.py` | `prepare_environment()` 加**线程锁**（预热线程与首次 submit 可能并发，须恰好一次） |
| C# | `SettingsLoader.cs` | `[backend] prewarm`（默认 1）→ `BackendSettings.Prewarm` |
| C# | `AppContext.cs` | `Create` 里 `settings.Prewarm` 时 `StartBackendPrewarm()`：后台 `_backend.EnsureStartedAsync()` **只起进程**（不发健康往返——import 期间持 GIL 会让 10s 健康检查误超时） |
| 配置 | `settings.ini.template` | 文档化 `[backend] prewarm = 1` |

**回退**：`settings.ini` 的 `[backend] prewarm = 0`（C# 改为惰性启动）；或 env `ZIV_AI_PREWARM=0`（Python 连上后不预热）。

**注（review 记录）**：C# `[backend] prewarm` 与 Python `ZIV_AI_PREWARM` **相互独立**（前者=是否提前起进程；后者=起后是否预热）；`PREWARM_LEVEL=1` 会在引擎锁内加载权重，与首次 submit 并发时会阻塞后者，**非默认**。

**验证**：
- `python -m py_compile`（main/config/model_loader）通过。
- `dotnet build src/ZIV.AI.sln -c Release`：**0 警告 0 错误**；`dotnet test`：**753 通过 / 0 失败**。
- Python 预热直调：`prepare_environment` 冷态 **6.17 s**，二次调用 **0.0000 s**（幂等）；
  `config.PREWARM=True`、`PREWARM_LEVEL=0`。
- 机制：首次 submit 的 `prepare_environment` 变为无操作 → 该 ~4–6 s 从用户等待路径移到 App 启动后的后台。

> **局限（未做）**：GUI 端到端（App 启动即拉起 Python）本机未跑（开发构建的程序目录无 `Comfyui/`、
> `python/`，路径由发布布局或 `settings.ini` 提供）；C# 侧仅构建 + 单测通过，逻辑为
> fire-and-forget `CheckHealthAsync`。发布版首图实测待补。

### 12.3 首次生成「用户等待」前后对比（int8 三件套 · 难提示词 · seed=42）

> 探针 `cold_start_trace.py`：`--prewarmed` 模拟 App 启动已预热（`prepare_environment` 不计入等待）；
> `ZIV_AI_SKIP_NEGATIVE` 控制负向编码。分段 `dur` 求和（不含解释器启动 ~1.5 s，优化后随 App 启动移出）。

| 分辨率/步数 | 优化前 | 优化后 | **差距** | 其中：预热移出 | 其中：省负向编码 |
|---|---|---|---|---|---|
| 1024² / 20 | 20.23 s | 13.31 s | **−6.92 s** | −6.26 s（import 1.91 + prepare 4.35） | −1.33 s |
| 1536² / 25 | 44.66 s | 34.22 s | **−10.44 s** | −6.50 s（1.92 + 4.58） | −3.54 s |
| 2048² / 25 | 87.67 s | 73.89 s | **−13.78 s** | −6.19 s（1.80 + 4.39） | **−7.68 s** |

- **关键发现**：**负向编码随分辨率快速增长**（1.33 / 3.54 / **7.68 s**）——因为负向 prompt 与正向一样
  带 `images_vl` 做**完整视觉塔前向**（参考图被放大到目标分辨率 → 视觉 token 随面积增加）。
  故 §10.2.2 在**高分辨率收益远大于**最初估计（§10.2.2 表里的 "−0.5 s" 仅针对 512/低分辨率）。
- **预热项稳定 ~6.2–6.5 s**（与分辨率无关，纯 import comfy/torch）。
- 显存：各档峰值 12.4 / 13.3 / 15.4 GB，优化前后一致（未增），**均无 OOM**。
- 口径：探针（后端路径）分段实测；GUI 端到端首图待发布版复测。

> **修正**：§10.2 中 10.2.2 的收益估计应改为「512 约 −0.5 s，1024 −1.3 s，**1536 −3.5 s，2048 −7.7 s**」。

### 12.4 口径澄清：§12.3 的 "2048" 是**正方形** 2048×2048（非 §6.2 的 2048×1280）

> 核查动机：2048² 峰值 ~15.4 GB、sample ~60 s，明显高于 §6.2 记录的 2048×1280（peak 12084、~29 s）。
> 结论：**不是回归，是测试口径不同**——2048² = 4.19 MP，2048×1280 = 2.62 MP（**1.6×** 面积）。

**动态显存核查（确认生效）**：`aimdo_enabled=True`、`CoreModelPatcher=ModelPatcherDynamic`、
`comfy_aimdo.devctxs=1`、`disable_smart_memory=True`；`reserve_vram=None` / `vram_headroom=0`。

**对齐宽高比（16:10）复测 vs §6.2**（int8 三件套、25 步、Dynamic VRAM）：

| 尺寸 | 本次 sample | 本次 run | 本次峰值 | §6.2 记录 |
|---|---|---|---|---|
| 1536×960 | 13.53 s | 18.33 s | 13762 MiB | 热 18.13 s / peak 13091 |
| 2048×1280 | 29.69 s | 37.26 s | 12864 MiB | 热 39.30 s / peak 12084 |

→ 时间基本一致、峰值差 ~5–6%：**无回归，动态显存正常**。

**单步耗时随面积**（本次）：1024²=1.05 MP→0.402 s/步；1536²=2.36 MP→1.055 s/步（面积 2.25×，耗时 2.63×）；
2048²=4.19 MP→2.409 s/步（面积 4.0×，耗时 6.0×）。1536 近线性；**2048² 超线性**（注意力 + 峰值 94%
时 Dynamic VRAM 抖动加剧）。→ **生产 2K 预设**（`models.json` 1:1=2048×2048 / 16:9=2752×1536 ≈ 4.2 MP）
确实吃满 16 GB（~15.4 GB / ~60 s@25 步），这是面积与注意力决定的，非故障。

**社区基线（对照）**：note.com（2026-09-24）RTX 4070 **12 GB**、INT8 DiT、**832×1248（1.04 MP）**、
25 步 → **32.63 s**。我们 2048×1280（2.62 MP）37.3 s：**2.5× 面积仅 1.14× 时间**，per-pixel 远快
（4080 > 4070 + 对方有卸载）。→ **本机 2K 速度属正常区间**。

**可选缓解**（若必须常跑正方形/宽幅 2K）：`--vram-headroom` 预留头寸减少抖动；或降一档 side（如 1792）。

---

## 附录：重建 G1 同负载对照（官方侧基线，2026-09-30，只增）

- **G1 定义（裁判担责，写死）**：`op=inpaint` / `side=1536`（输出 1216×1536）/ `nref=2` /
  `steps=40` / `sampler=euler` / `scheduler=simple` / `shift=3.1` / `cfg=1.0` / `seed=42` /
  `denoise=1.0` / `viggle=0|1`（Viggle 档 6-sigmas 接管）。
- **官方脚本（tracked）**：`tools/zivcli/official/g1_workflow.py`（工作流构造）+ `run_official.py`
  （起服务 / 提交 / 分段计时 / 峰值采集 / 结果 JSON）；用法见 `tools/zivcli/official/README.md`。
- **测量字段**与 `tools/zivcli/runner.py` 对齐；分段口径见 `DOC/FROZEN.md`「重建 G1 官方对照基线」段 §G1R.3。
- **冒烟事实（2026-09-30，非正式对照）**：官方非 Viggle @1536/2ref/40 步 →
  `load_sec=25.9s`、采样 `wall_sec=88.9s`、峰值 VRAM 15148 MiB、峰值 RSS 16.05 GB（单次，未取均值）。
  仅供脚本可运行性 / 字段完整性核验，**不作性能结论**。
