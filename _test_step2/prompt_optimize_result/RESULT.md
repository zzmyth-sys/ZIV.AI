# 提示词优化对 QW21edit 输出质量的影响（验证结果）

- 日期：2026-09-22
- 目的：验证「用官方 PE-I2I 系统提示词让 Qwythos-9B 重写用户指令」能否提升 `QW21edit` 出图质量
- 结论：**优化有效（中等幅度）**。重写把 20 字短指令扩展为 1421 字结构化英文描述，
  输出结构指标更高（blockRatio 0.914→0.935，方差 0.0293→0.0732），本地多模态模型判定
  重写版更符合「背景换茶肆、保留人物/前景」。**建议作为可选前置层引入**（见 §5）。

> **重要更正（2026-09-22，追加）**：本报告 §1–§6 使用的是**错误的 T2V 模板**
> （`temp/system_prompt.txt`，输出「成图描述」）。用户指出正确模板应为 **I2V / 图像编辑指令重写**
> （`temp/system_prompt_i2v.txt`，输出三字段 `rewritten_prompt`/`wh_ratio`/`ratio_follow`，
> 描述语言跟随用户指令=中文）。**正确模板的重跑见 §7，结论以 §7 为准**；§1–§6 仅作历史参考。

## 1. system_prompt.txt 评估

| 项 | 值 |
|---|---|
| 全文长度 | **10045 字节 / 9908 字符 / 192 行** |
| 输出格式 | 单行严格 JSON：`{"rewritten_prompt": "<描述>", "wh_ratio": "<如 3:2>"}`（**只有 2 个字段，无 `ratio_follow`**） |
| 关键约束 | 8 步流程（拆分 brief / 定画幅 / 开场句 / 清单 / 走查画面 / 文字 / 光照 / 收束）；观察者口吻（现在时、第三人称、不指令）；描述约 20 句 / 400–500 词；始终英文（图内文字保留原脚本）；`wh_ratio` 只放 JSON 不写进描述 |
| 是否需要视觉输入 | 提示词**未显式要求"看输入图"**；它是把用户 brief 改写成「成图描述」（"as if you were looking at it"）。但对**编辑**任务，输入图提供人物/前景身份信息，有视觉更稳 |

**模型能力匹配**：`llmctl args` 显示 Qwythos-9B 带 `--mmproj mmproj-Qwythos-9B-v2-BF16.gguf`
与 `--image-min-tokens 1024`；`/v1/models` 的 `capabilities=["completion","multimodal"]`
→ **支持视觉**。上下文 65536，提示词约 2.5k token + 图像 token，**充裕**。

**采用路径：A（官方全文 + 图像 + 文本）**。未走 B（降级无视觉）、未走 C（精简，无必要）。

## 2. 提示词重写结果

- **原始指令**（20 字）：`把背景替换为古代中式茶肆，保留人物与前景`
- **调用**：`POST http://127.0.0.1:8080/v1/chat/completions`，`temperature=1.0, top_p=0.95,
  top_k=20, max_tokens=4096, enable_thinking=true`（首次即成功；思考进入 `reasoning_content`，
  `content` 为干净 JSON，无需降级）。
- **解析结果**：`keys=['rewritten_prompt','wh_ratio']`；`wh_ratio="3:2"`；
  `rewritten_prompt` 长度 **1421 字符**（文件 `rewritten_prompt.txt`）。
- **重写内容摘要**：`The image is a horizontal scene of an ancient Chinese tea house...`，
  描述了木质格窗、茶壶茶杯、圆木桌、盆景、暖灰墙面、柔和自然光、深青汉服读书女性、
  平衡对称构图——**符合官方「观察者描述成图」格式**，且比原指令精确得多（对象、位置、光照、
  材质、色调齐全）。
- **质量评价**：满足官方格式与语气约束；但 `wh_ratio=3:2` 与输入图实际 16:10 不完全一致
  （当前管线用 side=1536 忽略 `wh_ratio`，见 §4 问题 2）。

## 3. 画面质量对比

### 3.1 输出文件

| | 路径 | 尺寸 | 字节 | SHA-256 |
|---|---|---|---|---|
| 原始 prompt | `_test_step2/prompt_optimize_result/orig_prompt_output.png` | 1536×960 | 1,769,268 | `C223721F80D97B4774E7A3873145918C9F39601FD7435D4966DFD7DBCD7AEEE1` |
| 重写 prompt | `_test_step2/prompt_optimize_result/optimized_prompt_output.png` | 1536×960 | 1,540,959 | `6B0A73A9F8CBDA5B8ECBDF28C1A9E6B07CA7E5A84DBCDDCD359E7D961BA90C9F` |

> 原始 prompt 输出 SHA-256 与上一轮 e2e_step6 完全一致 → 同图/同 seed/同 steps **可复现**。

### 3.2 客观指标（`ImageQuality` 口径）

| 图 | var | blockRatio（≥0.80） | lag1（≥0.90） | 结论 |
|---|---|---|---|---|
| 源图 1024×640 | 0.0842 | 0.894 | 0.986 | 正常 |
| 原始 prompt 1536×960 | 0.0293 | 0.914 | 0.991 | 非噪声 |
| **重写 prompt 1536×960** | **0.0732** | **0.935** | 0.991 | 非噪声 |

- 重写版 **方差更高（0.0732 vs 0.0293），更接近源图（0.0842）**；**blockRatio 更高（0.935 vs 0.914）**
  → 大尺度结构更丰富、细节/对比更多（与重写描述里更多的对象、材质、光照相印证）。
- 文件字节数重写版更小，但字节数不是画质代理（压缩差异），以结构指标为准。

### 3.3 主观评价

> Agent 为纯文本模型，无原生视觉；以下由**本机多模态 Qwythos-9B 代观察**（GPU 分时，
> 原始输出见 `vision_compare_raw.json`）。**注意：评审模型与重写模型同源，可能有自偏好偏差。**

- **背景**：两图均呈古代中式茶肆（木质、灯笼、茶具、暖色室内）。
- **人物/前景**：两图均完整保留（深青汉服女性、手中书、圆木桌、桌上茶壶茶杯）。
- **伪影**：两图均**无水印 / 文字伪影 / 明显畸变**。
- **判定**：**图2（重写 prompt）更符合要求** —— 背景更简洁、空间感更强、人物与前景处于视觉中心、
  构图平衡；图1 背景较杂乱、人物偏于画面一侧、焦点分散。

### 3.4 耗时与显存（分时复用，Z18）

| 运行 | 总耗时 | 模型加载（含 import） | moving_to_gpu | 采样 25 步 | decode+保存 | 峰值显存 | 结束后 |
|---|---|---|---|---|---|---|---|
| 原始 prompt | 28.48s | ~7.2s | ~5.5s | ~11.6s | ~1.4s | **13514 MiB** | 1119 MiB |
| 重写 prompt | 28.96s | ~7.2s | ~5.5s | ~11.6s | ~1.4s | **13828 MiB** | 1102 MiB |

- 两次均为**冷启动**（各自新 Python 进程），耗时几乎相同 → **重写不增加推理成本**（提示词长度对采样无影响）。

## 4. 遇到的问题与解决

1. **`llmctl stop` 需要配置文件参数**：`llmctl stop`（无参）只打印用法；正确为
   `llmctl stop config\launcher_config.json`。已修正并成功停止。
2. **`wh_ratio` 当前未被管线使用**：重写产出 `3:2`，但 `pipeline` 用 `RESOLUTION_MODE=side`
   （长边 1536）决定尺寸，忽略 `wh_ratio`；输入 16:10 输出仍 16:10。若要让 `wh_ratio` 生效，
   需后续把该字段接入 IPC/管线（契约议题）。
3. **Agent 无原生视觉**：改用本机多模态 Qwythos-9B 代观察；已标注同源偏差风险，建议人工复核。
4. **`SettingsLoader` 为 App 的 `internal`**：runner 直接读根 `settings.ini [backend]` + 默认回退。
5. **输出路径控制**：`EditPlan` 无 `WorkingDirectory`，改用 `EditStep.Parameters["output_path"]`
   显式指定（`QwenImage21EditTool` 已支持），两图路径可控。

## 5. 建议：是否引入管线

- **建议引入，但作为「可选前置层」**（对应 `OPTIMIZATION.md` §2.1.1 的 PromptOptimizer）：
  - 调用链：用户指令 →（可选）`LocalLlmClient` + PE-I2I 系统提示词重写 → `Executor.ExecuteAsync`。
  - 默认**可开关**（配置项），失败/超时**回退原文**（不阻断编辑）。
  - 只复用 `LocalLlmClient` + 独立 `LlmClientOptions`（`temperature≈1.0, top_p=0.95, top_k=20`），
    **不新增契约、不改 Planner/Executor/IPC**。
  - 可选缓存：同一指令 + 同图的重写结果可缓存，避免重复占 GPU。
- **保留意见（诚实）**：
  - 本验证仅 **1 个 prompt / 1 个 seed / 1 次采样**，指标提升为**中等**，不构成统计显著；
    建议后续用多 prompt × 多 seed 做 A/B 再定论。
  - 主观判定来自**同源**多模态模型，存在自偏好；**需人工目视复核**。
  - `wh_ratio` 暂不生效；重写会增加一次 LLM 调用（~7s 启动 + 数秒推理，且需与 ComfyUI **分时**占 GPU）。

## 6. 环境与合规

- GPU 全程**串行**（Z18）：llama-server（pid 21360 → 停 → pid 11656 → 停）与 ComfyUI 推理
  **未同时**占 GPU；峰值 llama-server ~11.2 GB、ComfyUI ~13.8 GB。
- 测试后 GPU 回落 **1350 MiB**；**无残留 ComfyUI / 推理 python 进程**（仅常驻 `windows-mcp`）。
- 源图 SHA-256 前后一致：`88A9B5F9E217E00D9EC8C333277D89095BA91EED1798FEAA4751300FA5E14AC7`。
- **未修改任何源码 / `DOC/` 冻结文档 / `python/server/*` / `C:\AI\ComfyUI_PIC`；未跑 `dotnet test`。**
- 新增临时文件：`_test_step2/test_prompt_optimize.py`、`_test_step2/prompt_optimize_result/`
  （`orig_prompt.txt` / `rewritten_prompt.txt` / `rewrite_raw.json` / `rewrite.json` /
  `test_vision_compare.py` / `vision_compare_raw.json` / 两张 PNG / `runner/`）/ 本 `RESULT.md`。

---

## 7. 更正重跑：正确的 I2V 模板（2026-09-22，追加）

> 用户指出 §1–§6 用的 `temp/system_prompt.txt` 是 **T2V（成图描述）** 模板；正确应为
> `E:\Downloads\system_prompt.txt` → 复制为 `temp/system_prompt_i2v.txt`。本节为**正确模板**的重跑。

### 7.1 模板评估

| 项 | 值 |
|---|---|
| 文件 | `temp/system_prompt_i2v.txt`（复制自 `E:\Downloads\system_prompt.txt`） |
| 长度 | **18344 字节 / 205 行**（脚本读到 17709 字符） |
| 定位 | **图像编辑指令重写器**（"Edit Prompt Enhancer — General v2"）；**明确「An input image is ALWAYS present」**（必须有输入图，非文生图） |
| 输出格式 | **三字段**：`{"rewritten_prompt", "wh_ratio", "ratio_follow"}`（`wh_ratio` 与 `ratio_follow` 互斥） |
| 语言 | 描述散文（A）跟随用户指令语言（中文→中文）；图内渲染文字（B）按优先级决策 |
| 与模型匹配 | Qwythos-9B 多模态 → **路径 A**（图 + 文） |

### 7.2 关键问题：reasoning 预算截断

- 首次 `enable_thinking=true`：`reasoning_content` 在 **~256 token 处被 `--reasoning-budget 256` 截断**
  （思考停在 "That directly mirrors the"），`completion_tokens=303` → 输出草率：
  `rewritten_prompt` 仅 23 字（原句 + 「不变」），且 **`wh_ratio="3:4"` 违规**
  （单图编辑、用户未指定比例 → 应为 `wh_ratio=""`、`ratio_follow="<image1>"`）。
- **解决**：改 **`enable_thinking=false`** 重跑 → 输出合规且丰富（见 7.3）。这是本步最重要的发现：
  Qwythos-9B 的 `--reasoning-budget 256` 对该长系统提示词过小，**必须关闭 thinking 或增大预算**。

### 7.3 重写结果（`enable_thinking=false`，文件 `rewritten_i2v_prompt_nothink.txt`）

- `wh_ratio = ""`、`ratio_follow = "<image1>"`（**符合规则**）
- `rewritten_prompt`（155 字，中文编辑指令）：

> 将图像中的背景替换为古代中式茶肆，人物与前景保持完全不变。茶肆内部采用传统木质装修风格，深棕色雕花格子窗配深褐色窗框，窗上悬挂着流苏挂饰。茶肆内摆放着木制桌椅，桌上设有茶壶、茶杯等茶具。两侧各有一张雕花木桌，桌上摆放着盆景。室内光线温暖柔和，营造出古朴典雅、充满生活气息的氛围，整体呈现典型的传统中式茶馆风貌。

### 7.4 QW21edit 运行（同图 / steps=25 / seed=42）

| 运行 | 总耗时 | 峰值显存 | 输出 | 字节 | SHA-256 |
|---|---|---|---|---|---|
| 原始 prompt | 28.48s | 13514 MiB | `orig_prompt_output.png` | 1,769,268 | `C223721F…` |
| **I2V 重写 prompt** | 28.80s | 13469 MiB | `optimized_i2v_prompt_output.png` | 1,787,044 | `13CB14BE…` |

### 7.5 客观指标（`ImageQuality` 口径）

| 图 | var | blockRatio（≥0.80） | lag1（≥0.90） | 结论 |
|---|---|---|---|---|
| 源图 1024×640 | 0.0842 | 0.894 | 0.986 | 正常 |
| 原始 prompt | 0.0293 | 0.914 | 0.991 | 非噪声 |
| **I2V 重写** | 0.0391 | 0.896 | 0.987 | 非噪声 |

- I2V 重写版方差略高于原始（0.0391 vs 0.0293），blockRatio 略低（0.896 vs 0.914）——
  客观指标**差距不大**，需结合主观判断。

### 7.6 主观评价（Qwythos-9B 代观察，同源偏差）

> 原始输出见 `vision_compare_i2v_raw.json`。**评审模型与重写模型同源，可能有自偏好。**

- **背景**：两图均为古代中式茶肆（木质结构、窗格、盆景、茶具、暖色室内）。
- **人物/前景**：图1（原始）人物边缘与背景融合不自然、略显「浮」在背景上；**图2（I2V 重写）
  人物与前景保留完整、边缘自然、融合度更高**。
- **水印/伪影**：图1 左上竖排书法、右上「腾讯视频」水印仍在；**图2 画面干净，无水印/文字干扰**
  （随背景替换一并消失）。
- **判定**：**图2（I2V 重写）更符合「把背景替换为古代中式茶肆，保留人物与前景」**。

### 7.7 结论（更正）

- **正确 I2V 模板的重写有效**：把 20 字指令扩为 155 字具体中文编辑指令（木质装修、雕花格子窗、
  流苏、茶具、盆景、暖光），保留条款明确；主观判定人物保留更好、边缘更自然、背景更精准、
  画面更干净。**应以本节结论为准**，§1–§6（T2V）作废/仅参考。
- **重要工程结论**：调用该模板时 **必须 `enable_thinking=false`（或增大 reasoning budget）**，
  否则 256 token 预算会截断思考、产出不合规结果。
- **仍建议作为可选前置层引入**；诚实保留：单样本、评审同源偏差、`wh_ratio`/`ratio_follow`
  当前未被管线使用（需接入才生效）、需与 ComfyUI **分时**占 GPU。

### 7.8 更正重跑环境与合规

- GPU 全程**串行**（Z18）：llama-server pid 3736 → 停 → pid 11416 → 停；ComfyUI 单独运行，未同时占 GPU。
- 测试后 GPU **1078 MiB**；无残留推理进程；源图 SHA-256 前后一致
  （`88A9B5F9E217E00D9EC8C333277D89095BA91EED1798FEAA4751300FA5E14AC7`）。
- **未修改任何源码 / `DOC/` 冻结文档 / `python/server/*` / `C:\AI\ComfyUI_PIC`；未跑 `dotnet test`。**
- 新增临时文件：`temp/system_prompt_i2v.txt`、`_test_step2/test_prompt_optimize_i2v.py`、
  `prompt_optimize_result/rewrite_i2v*.json`、`rewritten_i2v_prompt*.txt`、
  `optimized_i2v_prompt_output.png`、`vision_compare_i2v_raw.json`。
