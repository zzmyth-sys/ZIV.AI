# ZIV.AI 接口清单（INTERFACES）

- 状态：**当前有效接口的单一清单**，自 **Step 8-2** 起生效；与 `FROZEN.md`（「什么时候冻的」）互补。
- 规则：**只增不改**。已废弃的条目**保留并标注**（`~~删除线~~` + 注释），不物理删除。
- 首版覆盖 Step 0 → Step 8-2 的当前有效接口；此后每步只增。
- 关联：`FROZEN.md`（冻结记录）· `ARCHITECTURE.md`（分层）· `contracts/ipc-protocol.md`（IPC 传输）· `DOC/SPEC.md`（铁律）。

> 归属：`Contracts` = kernel（端口 + 值对象）；域实现 = `Agent` / `Backend` / `Tools` / `Imaging`；
> `UI` / `App`（含 `App/Flows`、`App/Shell`）为上层。端口接口「需要它的域声明」（Q9：放 `Contracts`）。

---

## 1. Contracts（kernel）

### 1.1 Inference（`ZivAiEditor.Contracts.Inference`）
- `IInferenceClient : IDisposable` — `CheckHealthAsync` / `SubmitInpaintAsync` / `SubmitEditAsync` / `GetTaskAsync` / `CancelTaskAsync`（Step 0/7）
- `ILlmClient` — LLM 补全（Step 4/5）
- `EditRequest` — `Op` / `ImagePath` / `MaskPath` / `Prompt` / `Steps` / `Seed` / `Denoise` / `OutputPath` / `Resolution` / **`ModelId`（8-2）** / `Lora` / `Optimizations` / `Anchor` / `AdditionalImages`（Step 7 / 9C.5-D / 8-2）
- `LoraOptions` — `Path` / **`StrengthModel`（8-2：`double?`）** / **`StrengthClip`（8-2：`double?`）**（Step 4 / 8-1 / 8-2）
- `OptimizationOptions`（Step 4）
- `InpaintRequest` / `EditOps` / `HealthStatus` / `InferenceTask` / `InferenceTaskHandle` / `InferenceProgress`（Step 0/2/4）

### 1.2 Planning（`ZivAiEditor.Contracts.Planning`）
- `IPlanner`（Step 0）
- `EditPlan` — `PlanId` / `SourcePrompt` / `MainImagePath` / `ReferenceImagePath` / `AdditionalImages` / `Mask` / `Steps` / `CreatedAt` / `Resolution` / **`ModelId`（8-2）**（Step 0/9C.5-D/8-2）
- `EditStep` — `StepId` / `Order` / `ToolName` / `Parameters` / `DependsOn` / `Status` / `ErrorMessage` / **`Lora`（8-1）**（Step 0/8-1）
- `PlanRequest` — `MainImagePath` / `ReferenceImagePath` / `AdditionalImages` / `Mask` / `Prompt` / `Options` / **`ModelId`（8-2）**（Step 0/9C.5-D/8-2）
- `ICommandParser` / `CommandDefinition`（含 **`Lora`（8-1）**）/ `ParseResult`（Step 8 / 9C.9-A1 / 迁移 1 / 8-1）
- `IEditSession` / `IEditSessionWriter` / `IEditNode` / `CropSpec` / `RerunSpec`（Step 8 / 9C.6-E / 9C.10）
- `ISessionPersistence` / `SessionLoadResult`（7-C）
- `IProjectService` / `ProjectSummary`（迁移 3 / 7-C）

### 1.3 Tools（`ZivAiEditor.Contracts.Tools`）
- `IEditTool` / `IToolRegistry`（Step 0）
- `ToolInput` — `StepId` / `MainImagePath` / `ReferenceImagePath` / `AdditionalImages` / `Mask` / `Parameters` / `WorkingDirectory` / `Resolution` / **`Lora`（8-1）** / **`ModelId`（8-2）**（Step 0/9C.5-D/8-1/8-2）
- `ToolResult` / `StepProgress`（Step 0）

### 1.4 Execution（`ZivAiEditor.Contracts.Execution`）
- `IExecutor` — `ExecuteAsync` / `RerunAsync` / `CancelAsync`（Step 0/9C.8-A）
- `TaskState` / `StepState` / `TaskProgress`（Step 0）

### 1.5 Imaging（`ZivAiEditor.Contracts.Imaging`）
- `IImagingService`（迁移 4）
- `MaskSpec`（Step 0/9C.7-B）/ `ResolutionPolicy` / `ResolutionMode`（Step 6.5）

### 1.6 Models（`ZivAiEditor.Contracts.Models`）
- `IModelProfileRegistry` — `Get` / `Default` / `All`（Step 6.5）
- `ModelProfile` / `ResolutionTier` / `AspectPreset`（Step 6.5/7；8-2 起由 `models.json` 填充）
- `ModelProfile.TierLabels`（`IReadOnlyDictionary<ResolutionTier,string>`，8-3 授权追加；由 `models.json` 的 `tier_labels` 填充）

### 1.7 Enums（`ZivAiEditor.Contracts.Enums`）
- `TaskStatus` / `StepStatus`（Step 0）

---

## 2. Agent（域实现；域物理划分待 8-4）
- `EditSession`（`: IEditSession, IEditSessionWriter`）/ `EditSession.Images` / `.Subtree`（Step 8/9C.10）
- `SessionStore`（`: ISessionPersistence`）/ `.Images` / `.Cleanup` / `SessionLoader` / `SessionFileRerun`（Step 9C.6-E/9C.10/7-C）
- `SessionSignature` / `ProjectNaming` / `ProjectService`（`: IProjectService`）（迁移 3/7-C）
- `Executor`（`: IExecutor`）/ `ExecutionQueue`（Step 6；Z18）
- `CommandParser`（`: ICommandParser`）/ `PromptExpander`（Step 8/9C.9-A1）
- `LlmPlanner` / `FallbackPlanner` / `ResilientPlanner`（`: IPlanner`）（Step 5/6）

## 3. Backend（inference 域实现）
- `IpcInferenceClient`（`: IInferenceClient`）/ `IpcDtos` / `IpcSubmitMapper` / `IpcFraming` / `IpcJsonContext`（Step 2/7/8-2）
- `PythonProcessManager`（Step 2/3/4）
- `LocalLlmClient`（`: ILlmClient`）/ `LlmClientOptions`（Step 4/5）
- `ModelProfileRegistry`（`: IModelProfileRegistry`）/ `ModelProfileFileDto` / `ModelProfileJsonContext`（Step 6.5/8-2）
- `ResolutionResolver`（Step 6.5/7）

## 4. Tools（tools 域实现）
- `ToolRegistry`（`: IToolRegistry`）（Step 6）
- `QwenImage21EditTool`（`: IEditTool`，`QW21edit`）/ `QwenImage21OutpaintTool`（`: IEditTool`，`QW21outpaint`）（Step 6/7）
- `ToolOutputPath`（Step 6）

## 5. IPC 消息字段（`contracts/ipc-protocol.md`；Step 2/3/4/6.5/7/9C.5-D/8-2）
- `ping` / `pong`（含 `model_status`）/ `cancel` / `canceled` / `accepted` / `progress`（`stage` / `sub_stage`）/ `preview`（+ `0x02` 二进制帧）/ `result` / `error` / `heartbeat`
- `submit.op`：`t2i` / `inpaint` / `outpaint`
- `submit.payload`：`image_path` / `mask_path` / `prompt` / `steps` / `seed` / `denoise` / `output_path` /
  `lora { path, strength_model, strength_clip }` / `optimizations` /
  `resolution { mode, side, area, scale, width, height, max_pixels }` / `anchor` / `additional_images` /
  **`model_id`（8-2，可选）**

## 6. 数据文件（数据 / 机制分离；Q1）
- `Template/commands.json`（Step 8 / 9C.9-A1；8-1 起支持 `lora`）
- `Template/loras.json`（8-1）
- `Template/models.json`（8-2）
- `settings.ini`（`[backend]` / `[llm.planner]` / `[llm.rewriter]`）

## 7. 命名空间（Step 8-4 物理划分后）

> 本节为 **Step 8-4 追加**（只增不改）。§1.2 `Contracts.Planning` 与 §2 Agent 的命名空间描述**以本段为准**。

- `ZivAiEditor.Contracts.Planning` → **已废弃**，拆为：
  - `ZivAiEditor.Contracts.Session`（IEditSession / IEditSessionWriter / IEditNode / CropSpec / RerunSpec / ISessionPersistence / SessionLoadResult）
  - `ZivAiEditor.Contracts.Project`（IProjectService / ProjectSummary）
  - `ZivAiEditor.Contracts.Execution`（ICommandParser / CommandDefinition / ParseResult / IPlanner / EditPlan / EditStep / PlanRequest；与既有 IExecutor / TaskState / StepState / TaskProgress 合并）
- `ZivAiEditor.Agent` → **已废弃**，拆为：
  - `ZivAiEditor.Agent.Session`（EditSession* / SessionStore* / SessionLoader / SessionFileRerun / SessionSignature）
  - `ZivAiEditor.Agent.Project`（ProjectService / ProjectNaming）
  - `ZivAiEditor.Agent.Execution`（Executor / ExecutionQueue / CommandParser / LlmPlanner / FallbackPlanner / ResilientPlanner / PromptExpander；`Execution/Command` 与 `Execution/Planner` 子文件夹共享此命名空间）
- 程序集名不变（Q1 / Q11）：`ZivAiEditor.Contracts` / `ZivAiEditor.Agent`。

## 8. 追加端口（8-4 · Z-006 收口）

> 本节为 **Z-006 收口追加**（只增不改）。

- `ZivAiEditor.Contracts.Project.IProjectMetadataStore`（project 域声明；`SessionStore` 实现）：
  - `string RootDirectory { get; }`
  - `Task<ProjectSummary?> ReadMetadataAsync(string directory, CancellationToken ct = default)`
  - `Task WriteMetadataNameAsync(string directory, string name, CancellationToken ct = default)`
  - **不含**会话内容读写（见 `ISessionPersistence`）。

## 9. 修订说明（2026-09-25 · 独立审查 R-3）

> 本节为**追加**（只增不改）。

- 新增域实现程序集 **`ZivAiEditor.Imaging`**：实现 §1.5 的 `IImagingService`（crop / mask 导出 /
  羽化），依赖 `Contracts` + `ZIV.Core` + `ZIV.Imaging`（对应 §7 的 imaging 物理域）。
- §1.5 `IImagingService` 的实现方为 `ImagingService`（`ZivAiEditor.Imaging`）；UI / App 只依赖端口。
- 命名空间映射：`ZivAiEditor.Imaging`（程序集与命名空间同名），与 §7 映射一致。
