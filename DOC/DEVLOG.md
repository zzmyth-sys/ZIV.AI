# ZIV.AI 开发日志（DEVLOG）

- 状态：随项目推进追加
- 用途：记录每个 Step 的开发过程、遇到的问题、解决方式
- 规则：只增不改；接口变更走 `FROZEN.md` 的修订说明，不在此

> ## 修改铁律
>
> 本文件**只增不改**。
>
> - 追加新记录：允许
> - 修改已有记录：**必须**先向用户说明原因并获授权
> - 不记录接口变更（那是 `FROZEN.md` 的事）

## 格式

每个 Step 一份，结构自定，至少包含：

- 目标
- 做了什么
- 遇到什么问题
- 怎么解决的
- 遗留项

---

## [Step 0] - 2026-09-21

### 目标

建立 ZIV.AI 的文档体系，冻结 Step 0 的定位、架构、铁律（Z17–Z28）与核心契约，
作为后续实现的唯一基准；**本步不写任何代码，不修改 ZIV 与 ImageGlass**。

### 做了什么

- 确认 ZIV.AI 的定位：**AI 图像编辑独立应用 + 进程外插件**；独立 `ZIV.AI.sln`、
  独立入口 `ZivAiEditor.App.exe`，可被 ZIV 主程序可选调用。
- 学习 ZIV 文档风格（`D:\devlop\ZIV\DOC\` 五份），确定 ZIV.AI 的文档约定：
  - 修改铁律 / 修订说明在文件顶部；铁律只增不改
  - 铁律编号从 ZIV 的 Z16 之后**续接**为 **Z17–Z28**；Z1–Z16 显式继承（引用不复制）
  - 验收清单分「通用 G + 专属」；Step 规划与验收记录分列
- 产出六份文件（本目录 + 契约草案）：
  - `SPEC.md`：定位、术语、功能规格（三图 + prompt / 手绘遮罩 / 提示词模板 /
    任务卡片流 / 多步执行 / 对外接口 / 空闲卸载 / 不破坏原图）、隐私、非目标、
    铁律 Z17–Z28 + 继承 Z1–Z16、评审清单。
  - `ARCHITECTURE.md`：设计目标、分层总览、功能块地图、依赖规则、
    跨模块接口契约草案（5 接口 + 6 模型）、通知与订阅、模块树、
    状态与生命周期、决策记录 D-1–D-17、与 ZIV 的对照、反过度设计、技术选型利弊、
    遗留项。
  - `FROZEN.md`：修改铁律、冻结规则、Step 0 段（铁律表 / 项目结构表 /
    核心契约签名草案 / 共享库引用方式声明）。
  - `ACCEPTANCE.MD`：通用验收清单 G1–G8（含 G7 进程隔离、G8 独立运行）、
    Step 0 验收段、Step 规划表、验收记录（空表）、性能预算表。
  - `DEVLOG.md`：本记录。
  - `contracts/openapi.yaml`：OpenAPI 3.1 最小骨架
    （`/v1/health` + `/v1/inpaint` + `/v1/task/{id}`）。

### 遇到的问题与解决

1. **ZIV 与 ZIV.AI 的共享边界如何表述**
   - 现象：`ZIV.Core` / `ZIV.Imaging` 是 ZIV 的资产，ZIV.AI 既要复用又不能复制；
     文档里若直接贴类型定义，等于变相复制（违反 Z26）。
   - 解决：文档中一律**引用**共享类型（如 `SKImageRef` 指向 `ZIV.Core`），
     并在 `FROZEN.md` 0.4 明确「仓库内只有一份、项目引用或 NuGet 引用、不得改共享库源码」。
   - 影响：`FROZEN.md` 0.4、`SPEC.md` §6.2 Z26。

2. **依赖方向中「UI 与 Backend」的关系**
   - 现象：用户要求 `App → UI → Agent/Tools/Backend → Contracts`，但又要求
     「UI 不直接依赖 Backend 实现，只依赖 Contracts 的 `IInferenceClient`」，
     字面上似有张力。
   - 解决：明确为——`Backend` 是 `IInferenceClient` 的**实现方**，`UI` 是**使用方**，
     二者编译期**互不引用**，只共享 `Contracts` 中的接口；`App` 负责装配。
     这样既保持严格单向，又满足 UI 只依赖契约。
   - 影响：`ARCHITECTURE.md` §4 及其说明、`FROZEN.md` 0.2。

3. **铁律编号续接与继承的表述**
   - 现象：ZIV 已有 Z1–Z16，若 ZIV.AI 重新从 Z1 编号会与 ZIV 冲突；
     若把 Z1–Z16 原文抄来，又违背「引用而非复制」。
   - 解决：ZIV.AI 续接 **Z17–Z28**；Z1–Z16 以表格列「原文要点 + ZIV.AI 适用说明」，
     并注明原文以 ZIV 文档为准。
   - 影响：`SPEC.md` §6.1、`FROZEN.md` 0.1。

4. **决策记录编号起点**
   - 现象：ZIV 的决策记录已到 D-23；ZIV.AI 是独立项目，若续接 D-24 会与 ZIV 混淆。
   - 解决：ZIV.AI 决策记录**从 D-1 起**独立编号，并在文档中注明「只增不改」。
   - 影响：`ARCHITECTURE.md` §9。

### 遗留项

- `contracts/openapi.yaml` 目前为**最小骨架**，Step 1 冻结三端点的完整 schema。
- **共享策略最终确认**：`ZIV.Core` / `ZIV.Imaging` 用项目引用还是 NuGet，待裁判裁决
  （`ARCHITECTURE.md` 遗留项 1 / `FROZEN.md` 0.4）。
- **Python 后端选型确认**：LightX2V 与 SGLang 是否都纳入 Step 1 骨架，还是先做单主线，
  待裁决（`ARCHITECTURE.md` 遗留项 3）。
- 发布目录、单实例端口策略、输出目录策略、遮罩坐标约定等待裁决
  （`ARCHITECTURE.md` 遗留项 2、4、5、6）。
- 性能预算数值待 Step 3 / Step 4 实测后填写。

### 备注

- 环境：Windows 10、PowerShell 7+。
- **本步未写任何代码**：未创建 `.cs` / `.csproj` / `.sln` / `.py`。
- **未修改** `D:\devlop\ZIV\` 与 `D:\devlop\ImageGlass-develop\` 下任何文件。
- 只读参考了 ZIV 的 `DOC/` 五份文档与 `src/` 下两个共享项目的 `.csproj`（用于确认
  共享库引用方式），未做任何写入。

---

## [Step 1] - 2026-09-21

### 目标

建立独立解决方案 `ZIV.AI.sln` 与 7 个 `ZivAiEditor.*` 项目骨架；把 `FROZEN.md` 0.3 的
5 接口 + 6 模型落地到 `ZivAiEditor.Contracts`（无业务逻辑）；落地共享库引用方式（项目引用）；
把 `contracts/openapi.yaml` 契约冻结提前到本步（8 端点）；产出最小可运行 Avalonia 空窗口
（标题 `ZIV.AI Editor`）与 `publish.ps1`。**不改 ZIV / ImageGlass，不复制共享库源码。**

### 做了什么

- **解决方案与项目矩阵**：`src\ZIV.AI.sln` 仅含 7 个 `ZivAiEditor.*`（Contracts / Backend /
  Agent / Tools / UI / App / Tests）；`App` 为 `net8.0-windows` WinExe（AOT / SelfContained /
  win-x64），其余为 `net8.0` 类库 / 测试。严格单向引用：
  `App → UI → Agent/Tools/Backend → Contracts → ZIV.Core/Imaging`；`Agent` / `Tools` /
  `Backend` 互不引用；UI 未引用 `ZIV.Viewer`。
- **构建配置**：`global.json`（**仓库根**，SDK 10.0.401，`rollForward latestFeature`；
  详见下方问题 5）与
  `src\Directory.Packages.props`（CPM，集中 Avalonia 12.1.1 / SkiaSharp 3.119.4 /
  xunit 2.9.3 等版本，ZivAiEditor 项目的 `PackageReference` 全部无版本号）。
- **契约落地**（`ZivAiEditor.Contracts`）：5 接口（`IInferenceClient` / `IEditTool` /
  `IPlanner` / `IExecutor` / `IToolRegistry`）、6 模型（`EditPlan` / `EditStep` / `ToolInput` /
  `ToolResult` / `MaskSpec` / `TaskState` 类）、2 枚举与 9 个辅助类型；**签名与 Step 0 完全一致**，
  接口无方法体，模型为纯属性，无业务逻辑。
- **占位骨架**：`Backend/HttpInferenceClient`、`Agent/PlaceholderPlanner`、
  `Agent/PlaceholderExecutor`、`Tools/PlaceholderToolRegistry`（均 throw-only）、
  `UI/UiPlaceholder`（空类）；不写任何真实逻辑。
- **App 空窗口**：`Program.cs` + `App.axaml(.cs)` + `MainWindow.axaml(.cs)`，窗口标题
  `ZIV.AI Editor`，960×640，含一行占位文本；未设 `ApplicationIcon`（无 Assets）。
- **跨进程契约**：重写 `contracts/openapi.yaml` 为 OpenAPI 3.1，冻结 **8 个端点**
  （health / inpaint / img2img / upscale / segment / outpaint / task 查询 / task 取消），
  字段 snake_case；共享 `ImageEditRequest`、统一 `TaskAccepted`（`task_id`），任务查询返回
  `TaskStatusResponse`（`state` / `progress` / `output_path` / `error`），状态枚举命名
  `TaskRunState` 以区分 C# 的 `TaskState` 类。
- **发布脚本**：`publish.ps1` 移植自 ZIV（`#requires -Version 7`），固定发布目录
  `D:\Program Files\ZIV.AI`（可用 `ZIV_AI_PUBLISH_DIR` 覆盖），保留 `settings.ini` / `_cache` /
  `*.bat`，去符号发布并清理残留 `*.pdb`。
- **测试**：`ZivAiEditor.Tests` 增加冒烟用例（`new MaskSpec().IsBinary == true`）。
- **文档**：`FROZEN.md` 追加顶部修订说明（裁判裁决 1–6 + 路径更正）与 Step 1 段；
  `ARCHITECTURE.md` 更新 §4 / §7 / §9（D-3 / D-7 / D-8 / D-9）与遗留项裁决说明；
  `SPEC.md` 新增 §3.9（输出目录 + 遮罩坐标）；`ACCEPTANCE.MD` 追加 Step 1 验收；
  本记录。

### 遇到的问题与解决

1. **`TaskState` 是枚举还是类，文本自相矛盾**
   - 现象：Step 1 启动文本把 `TaskState` 称作枚举，但 `FROZEN.md` 0.3 明确它是**类**
     （`Status` 字段的类型 `TaskStatus` 才是枚举）。
   - 解决：以 `FROZEN.md` 0.3 为准——`TaskState` 实现为 `sealed class`，`TaskStatus` /
     `StepStatus` 为枚举；在 `FROZEN.md` 1.2 与 `DEVLOG` 记录该澄清，不改 0.3。
   - 影响：`Execution/TaskState.cs`、`FROZEN.md` 1.2。

2. **跨解决方案 `ProjectReference` 相对路径层级算错**
   - 现象：`FROZEN.md` 0.4 示例写的是 `..\..\ZIV\src\ZIV.Core\ZIV.Core.csproj`，那是假设
     ZIV.AI 项目位于 `src\` 下一级；实际项目在 `src\ZivAiEditor.<X>\`，到 `D:\devlop\` 有
     **三级**，按示例会解析到 `D:\devlop\ZIV.AI\ZIV\...` 而找不到项目。
   - 解决：全部改为 `..\..\..\ZIV\src\ZIV.Core\ZIV.Core.csproj` 与
     `..\..\..\ZIV\src\ZIV.Imaging\ZIV.Imaging.csproj`；在 `FROZEN.md` 顶部修订说明中
     记录该更正并声明 0.4 示例路径作废（未改 0.4 原行）。
   - 影响：`Contracts` / `UI` / `App` 的 `.csproj`；`FROZEN.md` 修订说明 / 1.3。

3. **`TaskStatus` 与 BCL 的 `System.Threading.Tasks.TaskStatus` 重名（CS0104）**
   - 现象：`ImplicitUsings` 注入 `System.Threading.Tasks`，其中已有 `TaskStatus`；凡引用本项目
     `TaskStatus` 的文件会出现二义性。
   - 解决：在引用处加别名 `using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;`
     （`StepStatus` 无冲突，正常 using）。
   - 影响：`InferenceTask.cs` / `InferenceTaskHandle.cs` / `Execution/TaskState.cs` /
     `Execution/TaskProgress.cs`。

4. **经 `ZIV.AI.sln` 构建会连带把 ZIV 项目构建到 Debug**
   - 现象：`dotnet build ZIV.AI.sln -c Release` 时，ZivAiEditor 项目为 Release，但被项目引用的
     `ZIV.Core` / `ZIV.Imaging`（不在本 sln 内）输出到 `bin\Debug`；直接构建单个
     `ZivAiEditor.*` 项目时 ZIV 才按 Release 构建。
   - 原因：MSBuild 对**不在解决方案内**的 `ProjectReference` 会回退到默认配置（Debug），
     而非继承全局 `Configuration=Release`。
   - 解决：不把 ZIV 项目加入 `ZIV.AI.sln`（遵守 Z25 与任务约束）；记录为**已知现象**。
     构建 / 测试 / 发布均成功；ZIV 侧仅 `bin` / `obj` 被写入，无源码改动。
   - 影响：`FROZEN.md` 1.3、本记录「遗留项」。

5. **SDK 8.0.203 下 App 无法编译（Avalonia 生成器需 Roslyn 4.14）**
   - 现象：最初照搬 ZIV 把 `global.json` 固定为 SDK 8.0.203；在 `src` 目录下构建时
     `MainWindow.axaml.cs` 报 `CS0103: 名称"InitializeComponent"不存在`，并伴随 4 条
     `CS9057`（Avalonia 12.1.1 分析器目标 Roslyn 4.14 高于 SDK 8 的 4.9），XAML 源生成器未运行。
   - 原因：Avalonia 12.1.1 的 XAML 源生成器要求 Roslyn 4.14（.NET SDK 10）。
   - 解决：`global.json` 固定为 **SDK 10.0.401**，并移到**仓库根**（使任意 cwd 都生效）；
     此后从仓库根与 `src` 构建均 0 错误 0 警告。ZIV 自身 `global.json` 仍为 8.0.203（未改）。
   - 影响：`global.json`；`FROZEN.md` 1.6；`ARCHITECTURE.md` §7；`ACCEPTANCE.MD` 1.4。

### 遗留项

- **ZIV 构建配置回退**：如上第 4 点；如需让 sln 构建也强制 ZIV 用 Release，可在后续 Step
  评估给 `ProjectReference` 指定 `SetConfiguration` / `Configuration` 元数据（本步未做）。
- **`IInferenceClient` 提交入口不足**：OpenAPI 已冻结 6 个编辑端点，但契约目前只有
  `SubmitInpaintAsync`；Step 4 实现 `HttpInferenceClient` 时需扩展提交入口（走修订说明，
  不破坏既有成员）。
- **Python 后端尚未落地**：`python/` 目录与 SGLang 服务属后续 Step（裁决 3：Step 1 仅 SGLang，
  LightX2V 为 Step 7 可选加速）。
- **Step 2 顺延**：OpenAPI 冻结已提前到 Step 1，Step 2 调整为 DTO / 客户端生成等工作。
- **性能预算**数值仍待 Step 3 / Step 4 实测。

### 备注

- 环境：Windows、PowerShell 7、.NET SDK 10.0.401（仓库根 `global.json` 固定；见问题 5）。
- 验证：`dotnet build` 0 错误 0 警告；`dotnet test` 1 通过；启动 exe 后窗口标题为
  `ZIV.AI Editor` 且进程存活；`dotnet publish -r win-x64`（AOT）成功；`openapi.yaml` 可被
  YAML 解析且 8 路径齐全；`publish.ps1` 语法与端到端发布均成功。
- **未修改** `D:\devlop\ZIV` 与 `D:\devlop\ImageGlass-develop` 下任何**源文件**；因项目引用，
  ZIV 的 `bin` / `obj` 构建产物有更新（见第 4 点）。

---

## [Step 2] - 2026-09-21

### 目标

把 Python 后端从「SGLang HTTP」转向 **ComfyUI v0.37.0 便携版源码 + in-process 管线**，
并以实测为依据冻结 **IPC 传输契约**（Named Pipe），据此修订 Step 0/1 的 D-7 / D-8 / Z23。
本步产出：实测报告 + 文档冻结修订；**不写 C# 业务代码**。

### 做了什么

- **只读调查**：ComfyUI 便携版（v0.37.0，Python 3.13.14，torch 2.13.0+cu130）、Qwen-Image-2.1
  模型（`C:\AI\ComfyUI_PIC`，只读）、ZIV.AI 现状、ZIV 共享库、Klein 启动器。
- **实测 11 项**（脚本与结果保留于 `D:\devlop\ZIV.AI\_test_step2\`，报告 `REPORT.md`）：
  1. DiT `qwen_image_2.1_int8_convrot` 可加载（`QwenImage21Transformer2DModel`，7.1B 参数）✅
  2. TE `qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot` 可加载（`Qwen35TEModel_`）✅
  3. VAE `qwen_image_2.1_vae_bf16` 可加载（238 keys）✅
  4. ComfyUI v0.37.0 **无官方 Embedding API** ❌
  5. 端到端 512² 编辑闭环跑通（T2I 14.3s / Edit 16.1s，输出 PNG）✅
  6. 进度 callback 每步触发；`interrupt_current_processing` 打断生效 ✅
  7. Named Pipe 基准：7.91MB 单程 2.711ms（2918 MB/s）✅
  8. 共享内存对比：7.91MB 5.371ms（不占优）⚠️
  9. 无显存泄漏；进程退出回基线（813 MiB）✅
  10. Named Pipe 默认 ACL 允许 `Everyone` / `Anonymous` 读 ⚠️
  11. Python 3.13 free-threading 不可用 ❌
- **后端定案**：ComfyUI in-process；模型绝对路径直传（无需 `extra_model_paths.yaml`）。
- **契约**：新增 `contracts/ipc-protocol.md`（IPC 传输契约，Step 2 冻结）；
  `contracts/openapi.yaml` 降级为 Schema 参考。
- **文档**：`FROZEN.md` 顶部修订说明 + Step 2 段；`ARCHITECTURE.md` D-7 / D-8；
  `SPEC.md` Z23；`ACCEPTANCE.MD` Step 2 段；本记录。

### 遇到的问题与解决

1. **无官方 Embedding API**
   - 现象：ComfyUI v0.37.0 无 `comfy/client/`、无 `embedded_comfy_client.py`（grep 零命中）。
   - 解决：自封装 Python 推理进程，直接 `import comfy` 调管线（实测可行）。
2. **取消异常是 `BaseException`**
   - 现象：`interrupt_current_processing` 抛出的 `InterruptProcessingException` 继承
     **`BaseException`**，`except Exception` 捕获不到（实测进程 EXIT=1）。
   - 解决：Python 侧必须以 `except comfy.model_management.InterruptProcessingException` 或
     `except BaseException` 捕获，映射为 `canceled`。
3. **共享内存不占优**
   - 现象：共享内存 7.91MB 5.371ms，慢于 Named Pipe 2.711ms（同步 + 两次拷贝开销）。
   - 解决：IPC 采用 Named Pipe，不引入共享内存。
4. **默认管道 ACL 不安全**
   - 现象：默认 ACL 允许 `Everyone` / `ANONYMOUS LOGON` 读取。
   - 解决：用 `PipeSecurity` + `SetAccessRuleProtection(true,false)` +
     `NamedPipeServerStreamAcl.Create` 收紧为仅当前用户。
5. **pywin32 未安装**
   - 现象：便携版 Python 无 `win32event/win32pipe`。
   - 解决：Python 侧同步改用 `ctypes`（已验证可行）；不下载依赖。
6. **free-threading 不可用**
   - 现象：`sys._is_gil_enabled() == True`，无 `python3.13t.exe`。
   - 解决：不启用 free-threading（GIL build）。

### 遗留项

- **预览帧**（latent→小图）未单独实测；`callback` 可用，Step 2 实现时补测。
- **高分辨率 / 多参考图**下的显存与稳定性未测（仅测 512² 单参考图）。
- **实现未开始**：`IpcInferenceClient`、`python\` 推理进程、`IInferenceClient` 提交入口扩展
  均属 Step 2 后续实现。
- Step 0 规划中的 Step 2「DTO / 客户端生成」已不适用（OpenAPI 降级为 Schema 参考）。

### 备注

- 环境：Windows 10、PowerShell 7、ComfyUI 便携版 v0.37.0（Python 3.13.14）、.NET SDK 10.0.401。
- **未修改** `DOC/*.md` 之外无；**未修改** `C:\AI\ComfyUI_PIC`（只读访问模型）；
  **未下载**任何模型 / 依赖。
- 实测脚本 / 工程保留于 `D:\devlop\ZIV.AI\_test_step2\`（按要求不删除）。

---

## [Step 2.2] - 2026-09-21

### 目标

跑通「首次 `submit` 触发惰性加载 → `progress` 推送 `loading_model` → 加载完成」。
不含真实推理（Step 2.3）、预览 `0x02` 帧（Step 2.3）、取消 / 打断（Step 2.4）、
空闲卸载（Z21，Step 3+）。

### 做了什么

- **Python 侧**（`python/server/`）：
  - 新增 `model_loader.py`：`comfy.sd.load_diffusion_model` /
    `load_clip([...], clip_type=CLIPType.QWEN_IMAGE)` / `comfy.utils.load_torch_file` +
    `comfy.sd.VAE`；ComfyUI 源码树**惰性导入**（冷 `ping` 不触碰 torch）。
    每阶段加载完成后回调通知。
  - 新增 `engine.py`：`ModelEngine` 管理 `not_loaded → loading → loaded`；
    `ensure_loaded()` 为惰性入口；记录每阶段及总加载耗时；`unload()` 为 Z21 空钩子。
  - 扩展 `handlers.py`：`pong.model_status` 取自 engine；`submit` → `accepted` →
    6 条 `loading_model` 进度（dit / te / vae 各「开始 + 完成」）→ 1 条 `sampling` 进度
    （标注 Step 2.3 接管）；新增 `_make_progress_pusher`。
  - 扩展 `config.py`：`COMFY_ROOT`、显存策略预留（`VRAM_MODE` / `IDLE_UNLOAD_SECONDS`）；
    `BACKEND_VERSION 0.2.1 → 0.2.2`，`PROTOCOL_VERSION "1" → "0.2"`（对齐 `ipc-protocol.md`）。
  - `main.py`：新增 `submit` 分派。
- **C# 侧**（`src/ZivAiEditor.Backend`、`src/ZivAiEditor.Tests`）：
  - 新增 `InferenceProgressExtensions.cs`：`InferenceProgressDetail`
    （`task_id` / `stage` / `sub_stage` / `step` / `total` / `fraction` / `message`）。
    **不改** Contracts 的 `InferenceProgress` 签名。
  - 扩展 `IpcInferenceClient`：新增 `ProgressReceived` 事件；`SubmitInpaintAsync` 骨架
    （发 `submit`，解析 `accepted` / `progress` / `result` / `error`，加载完成即返回 handle）；
    `CheckHealthAsync` 保持 ping/pong。
  - `PythonBackendOptions` 新增 `ModelLoadTimeoutMs`（默认 180 s）。
  - 新增 `IpcModelLoadTests.cs`（2 用例）。
- **契约**：`progress` 帧新增可选 `stage` / `sub_stage` 字段（向后兼容）；
  **未改消息类型**，未改 `ipc-protocol.md` 既有定义。

### 实测

- **Python 直连**（临时脚本，引擎级）：
  - 冷加载 `TOTAL_LOAD_S = 8.44`；`TIMINGS {dit 0.048, te 0.596, vae 0.282}`。
    （`REPORT.md` 的 24 s / 8 s 为**三个独立进程各自含 `import comfy`** 的 wall；
    单进程只 import 一次，故显著更快。）
  - 进度序列：`dit@0.0 → dit@0.33 → te@0.33 → te@0.66 → vae@0.66 → vae@1.0`。
  - 显存：`nvidia-smi` 基线 997 → 峰值 1156 → 退出 914 MiB；
    `torch alloc / reserved = 0`（ModelPatcher 延迟上 GPU，与任务 1–3 一致）。
  - 二次 `ensure_loaded` 返回同 timings、状态仍 `loaded`（不重载）。
- **C# 端到端**：
  - `dotnet build ZIV.AI.sln`：**0 错误 0 警告**。
  - `dotnet test ZIV.AI.sln`：**5 通过 / 0 失败**（17 s），含新增 2 个加载用例：
    冷 `ping` = `not_loaded` → `submit` 收到 dit / te / vae 三类 `loading_model` 帧
    → `ping` = `loaded` → 二次 `submit` 无 `loading_model` 帧 → 关闭后无残留 Python 进程、
    显存回基线（900 MiB）。

### 遇到的问题与解决

1. **`import comfy` 是 wall 的主体**
   - 现象：直连冷加载 8.4 s 中约 7.5 s 花在首次 `import comfy`（连带 torch）。
   - 解决：`model_loader` 惰性导入 + `prepare_environment()` 幂等；冷 `ping` 不触碰 torch，
     后端启动即可返回 `not_loaded`。
2. **加载耗时远超 `RequestTimeoutMs`（10 s）**
   - 现象：`CheckHealthAsync` 的 10 s 超时不适用于模型加载。
   - 解决：新增 `ModelLoadTimeoutMs`（默认 180 s），仅 `SubmitInpaintAsync` 使用。
3. **`stage` / `sub_stage` 无处存放**
   - 现象：`InferenceProgress`（Contracts，sealed，仅 `Fraction` / `Message`）不可扩展，
     且 `IInferenceClient` 签名冻结。
   - 解决：在 Backend 层新增 `InferenceProgressDetail` + `ProgressReceived` 事件承载；
     `IProgress<InferenceProgress>` 仍按契约回传 `Fraction` / `Message`。

### 遗留项

- **`health` / `health_result`**：契约 0.2 已删，但 `python/server` 与 C# 仍保留兼容分支；
  建议 Step 3 清理（本步未动以收缩范围）。
- **`python/server/README.md`** 仍描述 Step 2.1 能力，未随本步更新。
- **`SubmitInpaintAsync` 只到「加载完成」**：不发 `result`、不推理；`result` 分支当前抛
  `NotSupportedException`，Step 2.3 接续。
- **`engine.unload()` 为空钩子**：Z21 空闲卸载待 Step 3+。
- **`progress` 的 `stage` / `sub_stage`** 尚未写入 `contracts/ipc-protocol.md`（本步仅 DEVLOG 记录）。

### 备注

- **未修改** `DOC/*.md` 的 Step 0 / Step 1 冻结行（仅追加 Step 2.2 段）。
- **未修改** `C:\AI\ComfyUI_PIC`（只读访问模型）；未下载模型 / 依赖；Python 侧零新依赖。
- 环境：Windows 10、PowerShell 7、ComfyUI 便携版 v0.37.0、.NET SDK 10.0.401。

### 遗留项处理（2026-09-21 补记）

- **`health` / `health_result`**：已从 `python/server/handlers.py`（`handle_health`）、
  `python/server/main.py`（分派分支）、`IpcInferenceClient.cs`（`IsHealthResponse`）与
  `python/server/README.md` 中清除；**代码侧不再有任何引用**。
- **`python/server/README.md`**：已随本补记更新消息表（`ping` / `submit` / `pong` /
  `accepted` / `progress`）。
- **`progress` 的 `stage` / `sub_stage`**：已写入 `contracts/ipc-protocol.md`（§3.2 字段说明 +
  新增 §3.3「加载阶段进度序列」），`ipc_version 0.2 → 0.3`，并在 `FROZEN.md` Step 2 段追加
  修订说明第 7 条。
- **`vram_used_mb` 口径**：复测确认 ComfyUI **延迟加载权重**——`load_models_gpu()` 前
  `torch.cuda.memory_allocated() == 0`、DiT 参数 device = `cpu`；调用后 alloc ≈ **6920 MB**、
  device = `cuda:0`。契约口径已明确为 **NVML 当前 GPU 占用**。
- 仍未处理：`SubmitInpaintAsync` 只到「加载完成」（Step 2.3）、`engine.unload()` 空钩子（Step 3+）。

---

## [Step 2.3] - 2026-09-21

### 目标

在模型加载完成后跑通真实推理：`submit → encode → sample（逐步 progress + preview 0x02）
→ VAE decode → PNG → result`；支持 mask（noise_mask，Z19）与 reference_latents 注入；
输出始终为新文件（Z24）。不含取消 / 打断（2.4）、其他编辑 op、空闲卸载、heartbeat。

### 做了什么

- **Python 侧**（`python/server/`）：
  - 新增 `pipeline.py`：复用 `_test_step2/t5_e2e_inpaint.py` 的管线逻辑——
    `encode_from_tokens_scheduled`（Qwen TE）→ `CFGGuider(cfg=1.0)` →
    `sampler_object("euler")` + `comfy_extras.nodes_flux.get_schedule` → `guider.sample`
    → `VAE.decode` → PNG。支持 `reference_latents` 注入（参考图编辑）与二值 mask
    作为 `denoise_mask`（有 mask 时目标 latent 取编码后的原图，无 mask 时为零 latent）；
    采样回调内预埋 `throw_exception_if_processing_interrupted()`（Step 2.4 检查点）；
    输出路径按 Z24 / SPEC §3.9 生成（同目录 + `_ai_{timestamp}`，冲突避让，绝不覆盖原图）。
    返回 `output_path` / `seed` / `width` / `height` / `duration_ms`。
  - 新增 `preview.py`：`latent_preview.get_previewer(device, latent_format)` 获取预览器；
    ComfyUI 默认 `--preview-method none`，这里显式置 `Auto`（解析为 Latent2RGB，无需 TAESD
    权重）；在回调中 `decode_latent_to_preview_image("JPEG", x0)` 后编码为 JPEG 字节。
  - 扩展 `ipc.py`：新增 `write_binary_frame` / `build_preview_payload` / `write_preview`
    （原子发送 `preview` 控制帧 + `0x02` 二进制帧，载荷 = `[4B id][id][4B JPEG len][JPEG]`，
    见契约 §3.5）；`write_frame` 拆出 `_write_frame_locked` 以便同锁连发两帧。
  - 扩展 `engine.py`：新增 `components` 属性暴露已加载的 `(dit, clip, vae)`。
  - 重写 `handlers.py` 的 `handle_submit`：加载完成后调用 `pipeline.run`；回调发
    `progress`（`stage=sampling` / `vae_decode`）与 `preview`+`0x02`；成功后发 `result`
    （task_id / output_path / duration_ms / width / height / seed）；异常发 `error`（含 code）。
    `InterruptProcessingException`（`BaseException`）按类型名映射为 `canceled` 骨架。
  - `config.py`：新增 `DEFAULT_RESOLUTION` / `DEFAULT_STEPS` / `PREVIEW_EVERY` /
    `PREVIEW_SIZE` / `OUTPUT_DIR`。
- **C# 侧**（`src/ZivAiEditor.Backend`、`src/ZivAiEditor.Tests`）：
  - `InferenceProgressExtensions.cs` 新增 `PreviewFrame`（TaskId / Step / Total / JpegBytes）、
    `InferenceResultDetail`（OutputPath / DurationMs / Width / Height / Seed）与
    `InferenceBackendException`（含 `Code`）。
  - `IpcInferenceClient`：新增 `PreviewReceived` / `ResultReceived` 事件；`submit` 循环解析
    `0x02` 二进制帧（`[4B id][id][4B jpeg len][jpeg]`，step/total 取自前一条 `preview`
    控制帧）并触发 `PreviewReceived`；`result` 分支解析详情、触发事件并返回
    `InferenceTaskHandle{Status=Succeeded}`；`canceled` 返回 `Canceled`；`error` 抛
    `InferenceBackendException`。**未改** `IInferenceClient` / `InferenceProgress` 签名。
  - 新增 `IpcInferenceTests.cs`（2 用例）与 `GpuSerialCollection.cs`（GPU 用例串行，
    避免两个 ComfyUI 进程并发导致 OOM）。
- **契约 / 文档**：未新增消息类型，仅按 §3.5 实现既有 `0x02` 帧；追加 `DEVLOG` / `ACCEPTANCE`
  的 Step 2.3 段。**未改** Step 0/1 冻结行。

### 实测

- **Python 直连**（临时脚本，引擎级；8 步）：
  - 冷加载 `model_load_s = 7.59`；加载序列与 Step 2.2 一致（dit / te / vae）。
  - 首次 `pipeline.run`：`encode ≈ 6.05 s → moving_to_gpu ≈ 5.76 s（load_models_gpu）
    → 10 步采样 ≈ 1.9 s → vae_decode + 保存`，`duration_ms = 13737`。
  - 二次 `pipeline.run`：`moving_to_gpu` 间隔降至 **2.51 s**，`duration_ms = 9932`。
  - 预览：每一步 1 帧 JPEG（约 0.9 KB，32×32，来自 latent 分辨率），可被 PIL / Skia 解码。
  - 原图与遮罩 SHA-256 前后一致；输出为新文件。
- **C# 端到端**：
  - `dotnet build ZIV.AI.sln`：**0 错误 0 警告**。
  - `dotnet test ZIV.AI.sln`：**7 通过 / 0 失败**（约 1.79 分钟），含新增 2 个推理用例
    （sampling progress ≥ 3 → preview ≥ 1 → result；PNG 512×512；原图哈希不变；
    二值 mask 用例）。
  - 关闭后无残留 Python 进程；`nvidia-smi` 回 **628 MiB**。

### 遇到的问题与解决

1. **ComfyUI 默认不生成预览**
   - 现象：`cli_args` 的 `--preview-method` 默认 `none`，`get_previewer()` 直接返回 `None`。
   - 解决：`preview.get_previewer` 显式把 `args.preview_method` 置为 `Auto`（解析为
     Latent2RGB，QwenImage21 的 `latent_rgb_factors` 已就绪，无需 TAESD 权重）。
2. **`decode_latent_to_preview_image` 不返回字节**
   - 现象：契约描述为「JPEG 字节」，实际返回 `("JPEG", PIL.Image, res)` 三元组。
   - 解决：`preview.encode_jpeg` 取出 PIL 图后用 `BytesIO` 编码为 JPEG 字节。
3. **GPU 用例并发会 OOM（Z18）**
   - 现象：默认 xUnit 按测试类并行；`IpcModelLoadTests` 与 `IpcInferenceTests` 同时起
     ComfyUI 进程，各占约 17 GB。
   - 解决：新增 `GpuSerialCollection`（`DisableParallelization = true`），两个推理测试类
     共享同一集合，串行执行。
4. **`preview` 二进制帧无 step/total**
   - 现象：契约 §3.5 的 `0x02` 载荷只有 task_id + JPEG，无采样步信息。
   - 解决：C# 记录前一条 `preview` 控制帧的 step/total，在随后到达的 `0x02` 帧上补齐。

### 遗留项

- **取消 / 打断（Step 2.4）**：采样回调已预埋 `throw_exception_if_processing_interrupted()`，
  Python 侧已能按类型名把 `InterruptProcessingException` 映射为 `canceled`；但 `cancel`
  消息、`CancelTaskAsync` 与 C# 取消联动未实现。
- **denoise < 1.0**：payload 已接收，但当前按整段 sigma 处理，未做部分去噪的完整语义。
- **`moving_to_gpu` 非独立 stage**：契约 stage 枚举无该项，故并入 `sampling`（message
  标为 `moving_to_gpu`），未新增协议字段。
- **`engine.unload()` 仍为空钩子**（Z21，Step 3+）；`GetTaskAsync` 仍为 `NotSupported`。

### 备注

- **未修改** `DOC/*.md` 的 Step 0 / Step 1 冻结行（仅追加 Step 2.3 段）。
- **未修改** `C:\AI\ComfyUI_PIC`（只读访问模型）；未下载模型 / 依赖；Python 侧零新依赖。
- 环境：Windows 10、PowerShell 7、ComfyUI 便携版 v0.37.0、.NET SDK 10.0.401。

---

## [Step 2.4] - 2026-09-21

### 目标

跑通「submit → 采样中 → cancel → canceled → 进程存活、模型不卸载、显存回落」。

### 做了什么

- **Python 侧**（`python/server/`）：
  - `handlers.py`：新增 `handle_cancel(message)`（调用
    `comfy.model_management.interrupt_current_processing(True)`）与
    `_make_cancel_poller(frame_io)`——采样循环里**非阻塞轮询**管道，遇到 `cancel`
    就置中断标志、遇到 `ping` 就回 `pong`；新增 `_release_caches()`（
    `soft_empty_cache(force=True)` + `torch.cuda.empty_cache()`）在取消后释放本次的
    激活显存；`handle_submit` 保持**同步**执行（同一线程完成整次推理）。
  - `ipc.py`：`FrameIO` 新增 `has_pending_frame()`（ctypes 调 `PeekNamedPipe` 看是否有
    完整帧头）与基于 `os.read` 的读取路径；**写**仍走 `stream.write`。
  - `pipeline.py`：`run()` 新增 `poll_cancel` 参数，在**每个采样回调开头**与**VAE decode 前**
    各排空一次，随后 `throw_exception_if_processing_interrupted()`。
  - `main.py`：`_dispatch` 新增 `message_type == "cancel"` 分支（空闲兜底路径）。
- **C# 侧**（`src/ZivAiEditor.Backend`、`src/ZivAiEditor.Tests`）：
  - `IpcInferenceClient`：实现 `CancelTaskAsync(taskId)`——校验当前有同名在飞任务后，
    经独立 `_writeGate` 发送 `{"type":"cancel","task_id":…}`，并等待 submit 读循环在收到
    `canceled` 时完成的 `TaskCompletionSource`；新增 `ActiveTaskId` 属性。**未改**
    `IInferenceClient` 签名（`CancelTaskAsync` 为 Step 1 冻结成员）。
  - 新增 `CancelRequest` 记录并注册进 `IpcJsonContext`。
  - 新增 `IpcCancelTests.cs`（2 用例，入 `GpuSerialCollection` 串行）。
- **契约 / 文档**：`cancel` / `canceled` 为契约 0.2 既有消息，**未改** `ipc-protocol.md`；
  追加 `DEVLOG` / `ACCEPTANCE` 的 Step 2.4 段；**未改** Step 0/1 冻结行。

### 实测

- **Python handler 路径**（FakeFrameIO，20 步）：
  - `smi`：启动 673 → 加载后 911 → 采样中 **13381**（alloc 12657 / peak 12818）
    → cancel 后 **13019**（alloc 10830，权重驻留、激活释放）→ 二次推理后 15943（瞬时）。
  - 取消：**延迟 0.056 s**，收到 1 帧 `canceled`，无 `result`、无 `error`，未写输出文件，
    `_active_task_id` 置空。
  - 二次 `submit` 正常完成（`duration_ms 11505`，输出存在）——模型未卸载。
- **C# 端到端**：
  - `dotnet build ZIV.AI.sln`：**0 错误 0 警告**。
  - `dotnet test ZIV.AI.sln`：**9 通过 / 0 失败**（约 2.51 分钟），含新增
    `IpcCancelTests` 2 例（采样中取消→`Canceled`、进程存活、二次提交成功；无在飞任务时
    返回 `false`）。
  - 关闭后无残留 Python 进程；`nvidia-smi` 回 **541 MiB**。

### 遇到的问题与解决

1. **主循环被推理阻塞，`cancel` 读不到**
   - 现象：`handle_submit` 同步跑整次推理，单线程读循环期间无法读 `cancel`。
   - 尝试：把推理放到 worker 线程、主线程继续读——**不行**。
2. **worker 线程写管道死锁（关键坑）**
   - 现象：worker 的 `write_json` 永久阻塞（`accepted` 都发不出），C# 一帧收不到。
   - 根因：CPython 的 `FileIO`（`open(..., "r+b", buffering=0)`）内部只有**一把 fd 锁**；
     主线程阻塞 `read` 持锁 → worker `write` 饿死。改用 `os.read` 仍不行：
     `os.read` / `FileIO.write` 在 Windows 上都走 CRT fd，**共用同一把 CRT fd 锁**。
   - 解决：**回到单线程 I/O**。`handle_submit` 同步执行；在采样回调内用
     `msvcrt.get_osfhandle` + `ctypes` 调 **`PeekNamedPipe`** 非阻塞探测，有帧才读，
     读到 `cancel` 就置中断标志。这样读写在时间上不重叠，且只需 ctypes（零新依赖）。
3. **取消后显存要回落**
   - 现象：中断异常抛出后激活内存仍被 torch 缓存占用。
   - 解决：捕获 `InterruptProcessingException`（`BaseException`）后先
     `soft_empty_cache(force=True)` + `torch.cuda.empty_cache()` 再回 `canceled`。
     权重保留（Z21 空闲卸载是后续 Step），符合「模型不卸载」。

### 遗留项

- **加载阶段无法打断**：`loading_model` 期间没有采样回调，`cancel` 要等加载完成、进入
  采样首个回调才被处理（Z18 串行；已在契约允许范围）。本步测试都是在采样中取消。
- **VAE decode 不可打断**：decode 前检查一次；decode 本身较快（约 2 s），可接受。
- **`shutdown` 在任务进行中**：轮询只处理 `cancel` / `ping`，`shutdown` 会被留到任务结束后。
- **`engine.unload()` 仍为空钩子**（Z21，Step 3+）；`RerunAsync`（Step 6）、
  `GetTaskAsync`（更后）未实现；heartbeat（Step 3）未实现。

### 备注

- **未修改** `DOC/*.md` 的 Step 0 / Step 1 冻结行（仅追加 Step 2.4 段）。
- **未修改** `C:\AI\ComfyUI_PIC`；未下载模型 / 依赖；Python 侧零新依赖。
- 环境：Windows 10、PowerShell 7、ComfyUI 便携版 v0.37.0、.NET SDK 10.0.401。

---

## [Step 3] - 2026-09-22

### 目标

按重新定义（原「SGLang 服务」随 D-7 废弃）实现 **空闲卸载（Z21）+ heartbeat**：
跑通「空闲超时 → 卸载模型 → 显存回落 → 下次推理自动重载」与「Python 心跳 → C# 检测异常」。

### 做了什么

- **Python 侧**（`python/server/`）：
  - `engine.py`：实现 `unload()`（`unload_all_models()` + `soft_empty_cache(force=True)`
    + `gc.collect()` + `torch.cuda.empty_cache()`，状态回 `not_loaded`，保留最近使用时间戳）；
    新增 `touch()` / `seconds_idle()`。
  - 新增 `idle_watcher.py`：daemon 线程，按 `IDLE_CHECK_INTERVAL_S` 轮询，满足
    「已加载 + 无在飞任务 + 空闲 > `IDLE_UNLOAD_SECONDS`」即 `engine.unload()`。
  - 新增 `heartbeat.py`：daemon 线程，每 `HEARTBEAT_INTERVAL_S`（默认 10 s）发
    `{"type":"heartbeat","vram_used_mb":<NVML>,"current_task_id":<id|null>}`。
  - `main.py`：主循环改为 `PeekNamedPipe` **非阻塞轮询**（`has_pending_frame` + 短 sleep），
    并启动 heartbeat / idle 两个线程；`pollable` 不可用时回退阻塞读且不启线程。
  - `model_loader.py`：`prepare_environment()` 中置
    `comfy.cli_args.args.disable_smart_memory = True`（等效官方 `--disable-smart-memory`）。
  - `handlers.py`：`_run_submit` 开头先 `model_loader.prepare_environment()`（保证
    smart-memory 标志早于 `import comfy.model_management`）；推理结束 `_ENGINE.touch()`；
    新增 `is_busy()` / `active_task_id()`。
  - `config.py`：新增 `IDLE_CHECK_INTERVAL_S` / `HEARTBEAT_INTERVAL_S` / `HEARTBEAT_DISABLED`
    / `POLL_INTERVAL_S` / `DISABLE_SMART_MEMORY`（均可环境变量覆盖）；`ipc_version 0.3 → 0.4`。
- **C# 侧**（`src/ZivAiEditor.Backend`、`src/ZivAiEditor.Tests`）：
  - `IpcInferenceClient`：重构为**后台接收循环**（读线程统管所有帧），`SubmitInpaintAsync`
    写 submit 后等 per-task `TaskCompletionSource`；`CheckHealthAsync` 等 `pong` TCS；
    新增 `HeartbeatReceived` / `HeartbeatLost` 事件与 `LastHeartbeatAt`；`HeartbeatLost`
    由看门循环按 `PythonBackendOptions.HeartbeatLostAfterMs`（默认 30 s）触发。
  - `PythonProcessManager`：`PythonBackendOptions` 新增 `HeartbeatLostAfterMs` 与
    `Environment`（注入子进程环境变量，供测试压缩间隔）。
  - 新增 `IpcIdleUnloadTests.cs`（3 用例，入 `GpuSerialCollection`）。
- **契约 / 文档**：`contracts/ipc-protocol.md` 新增 `heartbeat` 消息、`ipc_version → 0.4`；
  `FROZEN.md` 新增 Step 3 段（含重新定义与 smart-memory 决策）。**未改** Step 0/1 冻结行、
  `IInferenceClient` 签名。

### 实测

- **智能显存优化（关键）**：关闭后 512²/4 步采样峰值
  `nvidia-smi` **16020 → 9144 MiB**、`torch alloc` **11.4 GB → 0.7 GB**；
  单次推理 **13.0 → 13.7 s**（基本不变）。来源：`klein启动器/aimdo_init.py`。
- **空闲卸载（直连实测）**：推理后空闲 **3.5 s** 触发卸载；`alloc` 11462 → **8.8 MB**；
  `nvidia-smi` 经 WDDM 滞后 ~1.5 s 回落至 **~1038 MiB**。
- **卸载后重载**：惰性 load **1.6–2.6 s**；下次推理首个采样步前 `moving_to_gpu` **~5–6 s**，
  推理 **~13.7 s**，输出正常。
- **heartbeat**：间隔 1 s 时 2.6 s 内收到 2 帧；停止心跳后按阈值触发 `HeartbeatLost`。
- **C# 端到端**：
  - `dotnet build ZIV.AI.sln`：**0 错误 0 警告**。
  - `dotnet test ZIV.AI.sln`：**12 通过 / 0 失败**（约 3.24 分钟），含新增
    `IpcIdleUnloadTests` 3 例与 Step 2.2/2.3/2.4 回归。
  - 关闭后无残留 Python 进程；`nvidia-smi` 回 **753 MiB**。

### 遇到的问题与解决

1. **heartbeat 线程写被主循环阻塞的读饿死（Step 2.4 的 fd 锁）**
   - 现象：主循环 `read` 阻塞时，heartbeat 线程的 `stream.write` 拿不到 fd 锁 → 永不发送。
   - 解决：主循环改为 `PeekNamedPipe` 非阻塞轮询，读只在有数据时发生；
     heartbeat 线程与采样写入经 `FrameIO._write_lock` 串行，不再互锁。
2. **C# 空闲时收不到 heartbeat**
   - 现象：原「请求/响应」模型只在 submit/ping 期间读管道，空闲时心跳堆积无人读。
   - 解决：`IpcInferenceClient` 改为**后台接收循环**统管全部帧，per-task TCS + 事件派发；
     心跳/失联检测在空闲时也持续生效。
3. **`DISABLE_SMART_MEMORY` 读取时机**
   - 现象：`comfy/model_management.py` 在 **import 时**把 `args.disable_smart_memory`
     读成模块常量；而 `handlers._clear_interrupt()` 会先于 `prepare_environment()` 导入
     `comfy.model_management`，导致设置失效。
   - 解决：`_run_submit` 开头先调 `model_loader.prepare_environment()`，保证设为 `True`
     早于任何 `comfy.model_management` 导入（复测 `DISABLE_SMART_MEMORY == True`）。
4. **卸载后显存「看起来」没降**
   - 现象：`unload()` 后 `torch alloc` 已 8.8 MB，但 `nvidia-smi` 仍 ~13.5 GB。
   - 原因：WDDM/NVML 计账滞后（~1.5 s），非泄漏。
   - 解决：测试改为轮询 `CheckHealth`（NVML）直到 `not_loaded` 且 `vram < 阈值`。

### 默认策略说明（Step 3 收尾）

- **为什么默认关闭 smart memory**：512²/4 步实测，开启时采样峰值 `nvidia-smi` 16020 MiB、
  `torch alloc` 11.4 GB（16GB 卡随时 OOM）；关闭后 9144 MiB / 0.7 GB，**显存降约 43%**，
  单次推理 13.0 → 13.7 s（**速度损失约 5%**）。ZIV.AI 定位本地单卡交互，稳定性优先，
  故**默认关闭**（**与 ComfyUI 原生默认不同**）。来源：`klein启动器/aimdo_init.py`。
- **回退方式**：设环境变量 `ZIV_AI_DISABLE_SMART_MEMORY=0`（或 `false`）即恢复原生行为。
- **顺序约束（踩坑）**：`comfy/model_management.py` 在 **import 时**把
  `args.disable_smart_memory` 读成模块常量；而 `handlers._clear_interrupt()` 会先于
  `prepare_environment()` 导入该模块，导致设置失效。现由 `handlers._run_submit` 开头先调
  `model_loader.prepare_environment()`，保证早于任何 `comfy.model_management` 导入。

### 遗留项

- **自动重启未实现**（Step 4）：`HeartbeatLost` 仅暴露事件，`PythonProcessManager` 不订阅。
- **`loading_model` 阶段仍不响应 cancel**（Step 2.4 遗留）；`engine.unload()` 会卸载
  ComfyUI 全部托管模型（当前仅本模型）。
- **cudaMallocAsync**：klein 还启用官方 `import cuda_malloc`（cu130 自动开）；本步未启用，
  因异步内存池可能影响 `nvidia-smi` 回收口径，留待评估。
- `RerunAsync`（Step 6）、`GetTaskAsync` 未实现。

### 备注

- **未修改** `DOC/*.md` 的 Step 0 / Step 1 冻结行（仅追加 Step 3 段）。
- **未修改** `C:\AI\ComfyUI_PIC`；未下载模型 / 依赖；Python 侧零新依赖。
- 参考：`D:\devlop\klein启动器`（`aimdo_init.py` / `config.json` 的显存管理）。
- 环境：Windows 10、PowerShell 7、ComfyUI 便携版 v0.37.0、.NET SDK 10.0.401。

---

## [Step 4] - 2026-09-22

### 目标

实现后端自愈（`HeartbeatLost` → 自动重启）、App 层装配通路、pipeline 优化钩子预留，
并完成 512² 编辑基线性能实测。范围外：具体 LoRA / MagCache 加载、PE-I2I 重写器、
`RerunAsync` / `GetTaskAsync`、UI 展示（Step 9）。

### 做了什么

- **C# 契约**（`ZivAiEditor.Contracts/Inference/`）：
  - 新增 `LoraOptions`、`OptimizationOptions`；`InpaintRequest` 追加可选 `Lora` /
    `Optimizations`（`init`，默认 `null`）——走 `FROZEN.md` Step 4 修订说明。
- **自动重启**（`src/ZivAiEditor.Backend/`）：
  - `PythonProcessManager`：新增 `PythonBackendState` 与配置 `AutoRestartEnabled` /
    `MaxRestartAttempts` / `RestartBackoffMs`；新增 `AttachInferenceClient` /
    `DetachInferenceClient`（订阅 `HeartbeatLost`）、`RequestRestartAsync`、`NotifyTaskSucceeded`；
    事件 `Restarting` / `Restarted` / `RestartFailed`。重启用**新管道名**
    `zivai.infer.{C#PID}.{seq}`（`PipePath` 改为动态）；指数退避 2/4/8s；超限标记 `Failed`。
  - `IpcInferenceClient`：构造时 `AttachInferenceClient`；接收循环结束/管道断开时把在飞任务与
    pending ping 以 `InferenceBackendException(code="BACKEND_RESTARTED")` 结束；提交写管道遇
    `IOException` / `ObjectDisposedException` 归一为同 code；新接收流启动时重置
    `LastHeartbeatAt` 与失联锁存；成功 `submit` 后 `NotifyTaskSucceeded()` 清零重启计数；
    `Dispose` 先 `DetachInferenceClient` 再取消接收循环。`SubmitPayload` 增加
    `Lora` / `Optimizations` 字段。
- **Python 侧**（`python/server/`）：
  - 新增 `pipeline_hooks.py`（注册 / 应用 pre-sampling hook，签名
    `(model, clip, params) -> (model, clip)`）。
  - `pipeline.py` 阶段化：`apply_pre_sampling_hooks`（模型加载后、**encode 之前**）
    → `encode_prompt` → `sample` → `vae_decode` → `save_png`；行为不变。
    （收尾修正：hook 由「encode 之后」前移到「encode 之前」，使 LoRA 对 `clip` 的修改
    能影响文本条件编码；见下方「修正记录」。）
  - `handlers.py`：每次 `submit` 先清空再按 payload 的 `lora` / `optimizations` 注册占位 hook
    （仅记录 intent，不加载）；`config.py` `PROTOCOL_VERSION 0.4 → 0.5`、`BACKEND_VERSION 0.4.0`。
- **App 装配**（`src/ZivAiEditor.App/`）：新增 `SettingsLoader`（读程序目录 `settings.ini`，
  缺省回退仓库根模板 / 内置默认）、`AppContext`（装配 `PythonProcessManager` +
  `IpcInferenceClient`，`AutoRestart = true`）；`App.axaml.cs` 构造 `AppContext` 并把
  `IInferenceClient` 注入 `MainWindow`（构造参数）；退出时释放。
- **测试**：新增 `IpcAutoRestartTests`（2 用例，入 `GpuSerialCollection`）；`IpcIdleUnloadTests`
  的 `HeartbeatLost` 用例显式关闭自动重启（避免与 Step 3 断言相互干扰）。
- **基线**：新增 `_test_step2/baseline_bench.py`；结果写入 `DOC/OPTIMIZATION.md` §6。
- **契约 / 文档**：`contracts/ipc-protocol.md` `0.4 → 0.5`（新增可选字段 + §7 变更点）；
  `FROZEN.md` Step 4 段；本记录；`ACCEPTANCE.MD` Step 4 段。

### 实测

- **Python 钩子（无 GPU）**：注册空 hook → `applied_hook_count == 1`、被调用；
  `payload={}` / `{"lora":null,"optimizations":null}` → 0 hook；有 `lora`+`magcache` → 2 hook。
- **自动重启（C# 端到端，GPU）**：
  - 正常路径：推理成功 → Kill Python → `HeartbeatLost` → 自动重启（新 PID、`Restarted`）→
    新任务成功、输出 512×512。
  - 超限路径：连续 3 次 Kill 各触发一次成功重启（计数不因重启清零）→ 第 4 次 Kill →
    `RestartFailed`、状态 `Failed`、不再拉起进程（等待 3s 仍 `Failed`）。
- **基线**（512²/20 步，smart memory 关闭）：加载 **6.09 s**；稳态 `moving_to_gpu` **2.31 s**、
  采样 **1.64 s**、VAE decode **0.99 s**、总 **7.53 s**；峰值 torch alloc **10995 MB**；
  进程退出后 `nvidia-smi` 回落 **~871 MB**。
- **构建 / 测试**：`dotnet build ZIV.AI.sln` **0 错误 0 警告**；
  `dotnet test ZIV.AI.sln` **14 通过 / 0 失败**（约 4m23s），含新增 2 个重启用例；
  无残留 Python 进程，显存回基线。

### 遇到的问题与解决

1. **重启计数何时清零**
   - 问题：若每次重启成功即清零，则"连续 Kill 超限"永远测不出。
   - 解决：计数仅在**一次成功 `submit`** 后清零；单纯重启成功不清零。这样连续失联累计，
     超过 `MaxRestartAttempts` 才 `Failed`，与验收标准"连续 Kill 4 次 → RestartFailed"一致。
2. **重启与在飞任务**
   - 问题：管道断开后原 `SubmitInpaintAsync` 的等待 TCS 无人完成，会挂到 180s 超时。
   - 解决：接收循环退出时统一 `FailInFlight(BACKEND_RESTARTED)`；提交侧写管道异常也归一为该 code。
3. **重启与旧接收循环竞态**
   - 问题：旧接收循环的收尾 `FailInFlight` 可能误伤重启后新注册的任务。
   - 解决：收尾时校验 `_receiveStream` 仍是本流才失败在飞任务；新流启动时重置心跳时间戳与失联锁存。
4. **`AppContext` 与 `System.AppContext` 重名（CS0117）**
   - 现象：`SettingsLoader` 里 `AppContext.BaseDirectory` 解析到本项目的 `AppContext` 类。
   - 解决：显式写 `System.AppContext.BaseDirectory`。
5. **Step 3 心跳测试被自动重启干扰**
   - 问题：`HeartbeatLost_Fires_When_Heartbeats_Stop`（heartbeat 关闭）在默认自动重启下会不断重拉后端。
   - 解决：该测试类 options 显式 `AutoRestartEnabled = false`。

### 遗留项

- **LoRA / MagCache 未实现**：`handlers` 只解析并注册占位 hook；具体加载（`load_lora_for_models` /
  MagCache patch）留待优化步骤（`DOC/OPTIMIZATION.md` 候选 1.2 / 1.4）。
- ~~钩子位置在 `encode` 之后~~ — **已修正**：hook 现于 `encode` 之前应用（见「Step 4 修正记录」）。
- **UI 层（`ZivAiEditor.UI`）无改动**：当前无 `MainWindow`（窗口在 `App`），故 B3「UI 注入
  `IInferenceClient`」以 App 层 `MainWindow` 构造注入落地。
- `RerunAsync` / `GetTaskAsync` / `loading_model` 阶段取消 / PE-I2I 均未实现（范围外）。
- **基线口径澄清（非矛盾）**：本步基线 ~11.0 GB 为**「采样期间 torch 分配器峰值」**
  （`max_memory_allocated`，运行前 reset）；Step 3 记录的 0.7 GB 为**「推理完成后稳态」**
  （`memory_allocated`，采样后测量）。两者口径不同、**不可直接对比**；本步退出前稳态
  ~9.3 GB（`disable_smart_memory` 下权重采样后驻留）。**后续优化对比统一用 peak 口径**。
  详见 `DOC/OPTIMIZATION.md` §6 口径澄清。

### Step 4 修正记录（2026-09-22 收尾）

1. **钩子位置前移**（`python/server/pipeline.py`）：`apply_pre_sampling_hooks` 从
   `encode_prompt` **之后**移到**之前**，使 LoRA 对 `clip` 的修改（`strength_clip`）能作用于
   文本条件编码。签名与 handlers 的注册时机不变。
   - 验证：注册测试 hook → `call_order == ['hook', 'encode']`，且 hook 返回的 patched `clip`
     流向 `encode_prompt`；`IpcInferenceTests` 2/2 通过（无回归）。
2. **基线显存口径澄清**（`DOC/OPTIMIZATION.md` §6 + 本文件）：~11.0 GB 为采样期间 torch
   **峰值**口径，Step 3 的 0.7 GB 为推理完成后**稳态**口径；两者不同口径、不可直接对比，
   后续优化对比统一用 **peak** 口径。
3. **TE/DiT 不匹配 → 噪声根因修复**（见 `_test_step2/diagnose/DIAGNOSIS.md`）：文本编码器
   由 Qwen3.5-9B 换为 Qwen3-VL-8B（`config.TEXT_ENCODER_PATH`），端到端恢复出图。
4. **采样配置修正为 AuraFlow shift=3.1**（`pipeline.py`）：`ModelSamplingAuraFlow().patch_aura(model, 3.1)`
   + 官方 `comfy.sample.sample()`（`euler` / `simple` / `cfg=1.0`），替换原先的
   `comfy_extras.nodes_flux.get_schedule`（Flux 经验 mu）。经 1024 实测验证。
   `config.py` 新增 `AURAFLOW_SHIFT=3.1` / `SAMPLER_NAME=euler` / `SCHEDULER_NAME=simple`；
   `DEFAULT_STEPS` 20 → 40。
5. **分辨率与 OOM 降级**：`config.MAX_RESOLUTION`（默认 1024，面积口径）为目标分辨率；
   `RESOLUTION_FALLBACK=[1024,768,640]`。`pipeline.run` 捕获 CUDA OOM 后逐级降分辨率重试并释放缓存。
   **显存触顶风险**：1024 编辑峰值 `nvidia-smi` **16004 / 16376 MiB**（余量仅 ~370 MiB），
   更高分辨率需依赖降级。`denoise` 参数改为经 `comfy.sample.sample(denoise=...)` 生效
   （此前被忽略）。
6. **测试断言增强**：新增 `ImageQuality`（8×8 块均值方差占比 `blockRatio` + lag-1 自相关）
   与 `ImageQualityTests`（合成噪声/结构图，无 GPU）；`IpcInferenceTests` 全部输出断言
   "非纯噪声"，并新增 1024 用例；`baseline_bench.py` 增加 `block_ratio` 检查。
   说明：任务建议的"方差 0.3–0.8"阈值在本模型上**不成立**（正常图方差 0.007–0.075，
   噪声 0.007–0.037，重叠），故改用实测可分的 `blockRatio`（正常 0.85–0.97 vs 噪声 0.30–0.74）。

### 备注

- **未修改** `DOC/*.md` 的 Step 0/1/2/3 冻结行（仅追加 Step 4 段与 `InpaintRequest` 修订说明）。
- **未修改** `C:\AI\ComfyUI_PIC`；未下载模型 / 依赖；Python 侧零新依赖。
- 环境：Windows 10、PowerShell 7、ComfyUI 便携版 v0.37.0、.NET SDK 10.0.401、RTX 4080 16GB。
