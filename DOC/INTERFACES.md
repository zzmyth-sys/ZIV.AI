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

## 10. 追加说明（Step 9C.7-C · 遮罩修复）

> 本节为**追加**（只增不改）。

- **无新增 / 修改契约**。本轮改动在 `ZivAiEditor.UI`（`SessionViewModel.AlignForMask`，
  `ChatMessage.MaskPath` / `MaskFeatherPx`，`MaskState.MaxFeatherPx` 25 → 15）与
  `ZivAiEditor.App`（`ImagePreview.MaskToolEntered` / `MaskExportScheduled` 事件，`MainWindow`
  窗口级遮罩导出任务 `_pendingMaskExport`，`MaskOverlayBitmap` 气泡 / 预览共用叠加）。
- `MaskSpec.FeatherPx` 契约不变；`MaskFeather.MaxRadiusPx` 保持 25（纯函数内部钳制，产品路径受
  `MaskState.MaxFeatherPx = 15` 限制）。

## 11. 追加说明（模板系统 T1–T2 · 数据分层）

> 本节为**追加**（只增不改）。

- **契约追加**：`ZivAiEditor.Contracts.Execution.CommandHandler`（enum：`Edit` / `T2I` / `Outpaint` / `Tag`）；
  `CommandDefinition.Handler`（`CommandHandler`，JSON `handler`，默认 `Edit`；旧文件缺省 → `Edit`）。
- **契约追加（T2.5）**：`CommandDefinition.Loras`（`List<LoraOptions>?`，JSON `loras`）；`Lora`（JSON `lora`）
  保留为只读兼容字段；`EffectiveLoras`（`[JsonIgnore]`）= `Loras` 非空取 `Loras`，否则 `Lora` 升级为 `[Lora]`；
  服务层写入只写 `loras`（`NormalizeLora`）。
- **服务层（非契约，`ZivAiEditor.Agent.Execution`）**：
  - `CommandSource`（enum：`BuiltIn` / `User`）+ `CommandTemplateDto(CommandDefinition, CommandSource)`；
  - `ICommandTemplateService`：`List()` / `Add` / `Update` / `Delete` / `Reset` / `ResetAll`；
  - 实现 `CommandTemplateService(templateDirectory)`：内置 `commands.json` + 用户 `commands.user.json` 按
    `name`（Ordinal）合并；写仅动用户文件（原子写；空 → 删文件）。
- **数据文件追加**：`Template/commands.user.json`（用户覆盖层；可选，缺失 = 无覆盖）。
- **未接入**：`CommandParser` 仍只读内置 `commands.json`（合并视图接入属 T3）；`AppContext` 未装配服务（T3/T5）。
- **契约追加（T3.1）**：`ParseResult.Capability`（`string?`，非 null = 能力调用，当前 `"tag"`；T4 执行）；
  `ParseResult.Warnings`（`IReadOnlyList<string>`，字段归属越界警告）；
  `CommandDefinition.EffectiveHandler`（`[JsonIgnore]`）= 显式非 `Edit` handler 优先，否则旧 `t2i=true` → `T2I`。
- **解析行为（T3.1）**：`CommandParser` 按 `EffectiveHandler` 分流（`Edit` / `T2I` / `Outpaint` → `EditPlan`；
  `Tag` → `Capability="tag"`，暂不产 plan）；字段归属校验（越界报警 + 忽略）；handler 输入约束
  （`T2I` N=0 / `Tag` N=1 / `Outpaint` N=1 / `Edit` N≥1）。
- **T3.1 行为反转**：`T2I` 有输入图由「忽略」改为「报错」（授权）；已同步既有测试。
- **契约追加（T3.2）**：`EditStep` / `ToolInput`（`List<LoraOptions>? Loras`）、
  `EditRequest`（`IReadOnlyList<LoraOptions>? Loras`）各加 `Loras`（JSON `loras`，`Lora` 保留只读兼容）
  与 `EffectiveLoras`（`[JsonIgnore]`，去重按 `path`，保留首次）；新增 internal `LoraSlots.Resolve`。
- **IPC（T3.2）**：`submit.payload.loras`（数组，可选，优先于 `lora`）；`ipc_version 0.8 → 0.9`。
  `IpcSubmitMapper` → `SubmitPayload.Loras`；`config.PROTOCOL_VERSION = "0.9"`。
- **透传链（T3.2）**：`CommandParser` / `Executor` / `QwenImage21EditTool` / `IpcSubmitMapper` 全用
  `EffectiveLoras`；`QwenImage21OutpaintTool` 不透传（Z-024）。

## 12. 追加说明（P1 · `/扩图` 重定位）

> 本节为**追加**（只增不改）。

- **契约追加（Step 1）**：`ZivAiEditor.Contracts.Session.CropSpec` 增 `int SourceWidth` / `int SourceHeight`
  （JSON `source_width` / `source_height`；`0` = 未知）与实例方法 `bool IsOutpaint()`（源尺寸未知 → `false`）。
- **契约（P1a）**：`CommandDefinition.FixedResolution`（`ResolutionPolicy?`，JSON `fixed_resolution`，
  命令自带固定分辨率，优先于 `width`/`height` 参数与 UI 档位）。
- **行为（Step 2–5，非契约）**：`/扩图` 由通用外扩改为「裁切外扩跟随动作」——按名称触发，需当前节点
  `Crop.IsOutpaint()`；`CommandParser` ctor 增可选 `IImagingService?`（`ParseSlashCommand` 改 async）；
  `plan.Mask` 为临时 `MaskSpec`（不写节点）；`CommandRequirements.RequiresOutpaintCrop` /
  `SessionViewModel.AlignForCrop` 为新增 UI 门控；`FlowRunner.RerunNodeAsync` 增 `/扩图` 重跑预检。

## 13. 追加/更正说明（P1 收尾 · 原生尺寸 + 无掩膜）

> 本节为**追加**（只增不改）；**更正 §12** 中「/扩图 软掩膜 + imaging 注入 + async + 分辨率随 UI 档位」。

- `/扩图`（`CommandParser`，按名称）：**无掩膜**（`plan.Mask = null`）+ **原生尺寸**
  `ResolutionPolicy{Explicit, crop.Width, crop.Height}`；非 `/扩图` 命令仍用节点手绘掩膜与 UI 档位。
- `CommandParser` ctor 恢复 `(string commandsJsonPath = ...)`；`ParseSlashCommand` 恢复同步（无 await）。
- `CommandDefinition.FixedResolution`（P1a）保留（`/全景` 用）。
- `OutpaintMask`（`ZivAiEditor.Agent.Session`）保留，供 B 策略 / 实验。
- `python/server/pipeline.py::_resize_mask(mask,w,h,mode)`：软掩膜 `bilinear` / 二值 `nearest`。

## 14. 追加/更正说明（P1 收尾修正 2 · 外扩放宽 + `/扩图` 蓝底无掩膜）

> 本节为**追加**（只增不改）；**更正 §13** 中「/扩图 无掩膜 + 原生尺寸」与「灰底填充」。

- **外扩数值放宽**（9C.4-B.2 / D2 / D3）：`CropState.MaxExpandFactor` 2.0 → **3.0**；
  `MaxPixelCount` 16 MP → **36 MP**；`ImagePreview.Crop.CropViewMarginFactor` 0.65 → **0.5**。
  钳制算法与 D1 / D4 / D5 不变。
- **外扩填色**：`ImageCropper.CanvasFill` 灰 `(128,128,128)` → **蓝 `(0,0,255)`**（对齐 Lazy 工作流
  `_pad_outpaint_blue`）；`CropOverlay` 预览色同步为蓝。
- `/扩图`（`CommandParser`，按名称）：**无掩膜**（`plan.Mask = null`）+ **分辨率跟随 UI**
  （不再自带 `Explicit{crop.W,crop.H}`，`ApplyResolution` 注入 UI 档位）。非 `/扩图` 命令仍用节点手绘掩膜与 UI 档位。
- `CommandParser` ctor 恢复 `(string commandsJsonPath = ...)`；`ParseSlashCommand` 恢复同步（无 await）；
  去掉 `IImagingService` 注入。`AppContext` 构造顺序恢复（… → `BuildAgent` → … → `BuildImaging`）。
- `/扩图` 模板（`Template/commands.json` + `CommandParser.BuiltIn.cs`）：工作流内置扩图提示词
  「…Replace all solid blue padded regions…outside the blue areas.」。
- `OutpaintMask`（`ZivAiEditor.Agent.Session`）保留供实验，产品不再使用。
- **Z-029**：A / B 均不采用。

## 15. 追加说明（模板系统 T5·S1 · 合并视图装配 + 纯函数）

> 本节为**追加**（只增不改）。

- **构造重载（非契约）**：`CommandParser` 新增 `CommandParser(IReadOnlyList<CommandDefinition> commands)`；
  旧 `CommandParser(string commandsJsonPath = DefaultCommandsPath)` **保留并委托**（`: this(LoadCommands(path))`，
  文件缺失回退 `BuiltInCommands()`，行为不变）。解析逻辑未动。
- **装配（非契约）**：`AppContext.BuildAgent` 构造 `CommandTemplateService(templateDirectory)`，parser 以
  `ICommandTemplateService.List()` 的 `Definition` 列表构造（内置 + 用户合并视图）；`AppContext` 新增属性
  `ICommandTemplateService CommandTemplates`（供后续 UI 使用）。
- **新增纯函数（非契约）**：
  - `ZivAiEditor.App.CommandAvailability`：`readonly record struct Context(bool HasImage, int ImageCount,
    bool HasOutpaintCrop)` + `Evaluate(CommandDefinition, Context) -> (bool available, string? reason)`；
    四轴 = T2I 需无图 / 编辑需有图 / multi-only 需 ≥2 图 / `/扩图` 需外扩裁切。
  - `ZivAiEditor.UI.Editing.CommandSuggestions.Filter(IReadOnlyList<CommandDefinition>, string prefix)`：
    Ordinal 前缀；空串或 `/` → 全部；保持原顺序。
  - `CommandRequirements.RequiresMoreImages` / `RequiresOutpaintCrop` 签名不变，改为复用
    `CommandAvailability.Evaluate`（hint 文案不变）。
- **未接入**：`/` 候选 Popup（S4）；热重载（Z-030，重启生效）。
- **无契约追加 / 无 IPC 改动**。

## 16. 追加说明（模板系统 T5·S4 · `/` 候选列表 UI）

> 本节为**追加**（只增不改）。

- **UI（非契约）**：`ZivAiEditor.App/MainWindow.CommandList.cs`（partial）——`PART_Input` 下方
  `Popup PART_CommandPopup` 展示候选；`MainWindow.axaml.cs` 的 Tunnel `KeyDown` 首句调用
  `HandleCommandListKey`。筛选 / 可用性复用 `CommandSuggestions` / `CommandAvailability`。
- **行为**：输入单 token 且以 `/` 开头（无空白）显示；不可用项置灰 + ToolTip(reason) + ↑↓ 跳过；
  ↑↓ 选择 / Enter·Tab 插入（不发送）/ Esc 关闭；输入框自持焦点。
- **无契约追加 / 无 IPC 改动**。

## 17. 追加说明（模板系统 T5·S4-fix · 候选排序 / 使用次数）

> 本节为**追加**（只增不改）；**更正 §16** 的「不可用项置灰 + ToolTip + ↑↓ 跳过」描述。

- **新增纯逻辑（非契约）**：`ZivAiEditor.UI.Editing.CommandOrdering.Order(filtered, counts)` ——
  `/扩图` 置顶 → 其余按次数降序（缺键 = 0）→ 并列保持原序（稳定）。
- **新增 App 状态（非契约）**：`ZivAiEditor.App.CommandUsageStore` —— `Load()` / `Record(name)` / `Counts`；
  文件 `{AppContext.BaseDirectory}/commands.usage.json`（Z14，源生成 JSON；缺 / 坏 → 空；写失败静默）。
- **行为更正**：`ShowSuggestions` 先 `CommandAvailability.Evaluate` **剔除不可用**（不渲染，取代置灰）；
  再 `CommandOrdering.Order`；`CommitSelection` 成功后 `Record(name)`。
- **无契约追加 / 无 IPC 改动**。

## 18. 追加说明（模板系统 T5 收口 · 非契约登记索引）

> 本节为**追加**（只增不改）。T5 收口索引；新增文件均已在前述节登记，本节不重复契约描述。

- **非契约新增（汇总）**：
  - `ZivAiEditor.UI.Editing.CommandSuggestions`（§15）/ `CommandOrdering`（§17）——纯逻辑；
  - `ZivAiEditor.App.CommandAvailability`（§15）/ `CommandUsageStore`（§17）——App 层；
  - `ZivAiEditor.App.MainWindow.CommandList`（§16）——`/` 候选列表 UI（partial）；
  - `{AppContext.BaseDirectory}/commands.usage.json`（§17）——使用次数状态文件（非契约）。
- **Z-021 / Z-030 关闭**（见 `FROZEN.md`「T5 收口」）。
- **无契约追加 / 无 IPC 改动**。

---

## 19. 追加说明（`/扩图` 重跑修复 A+B，2026-09-26）

> 非契约行为修正（无签名 / 契约变更）。

- **重跑保留 crop/mask**：`FlowRunner.RerunNodeAsync` 成功分支不再 `SetNodeCrop/SetNodeMask(null)`，不再删
  `oldCrop` / `oldMask` 文件；仍 cascade 删除子树与旧输出图。理由：crop/mask 为节点用户编辑（9C.6-B / 9C.7），
  重跑只更换输出图。
- **取消引导（非契约 UI）**：提交取消后追加非错误 System 提示；气泡「重新生成」ToolTip = `重跑此节点：{Command}`。

## 20. 追加说明（气泡 X 删除节点 + 子树，2026-09-26）

> 本节为**追加**（只增不改）。

- **契约追加（尾部）**：
  - `ZivAiEditor.Contracts.Session.IEditSessionWriter.RemoveNodeAndSubtree(string nodeId) → IReadOnlyList<IEditNode>`：
    删除 `nodeId` 自身 + 全部后代，返回被删节点（含自身）；未知 → 空表（no-op）；不抛；删 root 等价清空。
  - `ZivAiEditor.Contracts.Imaging.IImagingService.CleanupNode(string? sessionId, IReadOnlyList<string> nodeIds)`：
    删 `_cache/crops|masks/{sessionId}/{nodeId}.png`；目录不存在 no-op、逐文件容错、never-throw。
- **UI 端口追加（尾部）**：`ZivAiEditor.UI.Chat.IEditFlowRunner.DeleteNodeAsync(string nodeId) → Task<bool>`。
- **实现（非契约）**：`EditSession.Subtree` 增 `RemoveNodeAndSubtree`（与 `RemoveSubtree` 共用 `RemoveRange`）；
  `ImagingService.CleanupNode`；`FlowRunner.Delete.cs`；`MainWindow.Delete.cs`（partial）；气泡 × 样式（`Border.bubble` +
  `Button.bubbleDelete`）。
- **构造追加（非契约）**：`FlowRunner` ctor 末位增可选 `IImagingService? imaging = null`。
- **无 IPC / Python 改动**。

## 21. 追加说明（视图改造 + UI 统一 + 裁切比例 N1–N5，2026-09-26）

> 本节为**追加**（只增不改）。**无契约变更 / 无签名变更 / 无 IPC / Python 改动。**

- **无 Contracts 变化**：本步仅动 App / UI 层。
- **UI 层新增（非 kernel）**：
  - `ZivAiEditor.UI.Editing.CropAspectMode`（`Free / R16x9 / R9x16 / R1x1`）——纯 UI 状态。
  - `ZivAiEditor.UI.Editing.CropState.Aspect` / `SetAspect(CropAspectMode)`——纯状态（`CropState` 改 `partial`）。
  - `ZivAiEditor.App.Controls.PanZoomCanvas`（UserControl）——替换 `UVtools.AvaloniaControls.AdvancedImageBox`；
    `UVtools.AvaloniaControls` 依赖已移除（`Directory.Packages.props` / `App.csproj` / `App.axaml`）。
  - `ZivAiEditor.App.Controls.Modes.CropModePanel` / `MaskModePanel`（UserControl）。
- **行为修正（非契约）**：`ImageViewModel.ClampOffset` 每轴至少 10% 可见；裁切默认框 `0.75 → 0.85`；
  进入裁切用标准 `Fit()`；平移（空格+左键 / 中键）在所有模式生效。

## 22. 追加说明（ZIV ↔ ZIV.AI 快捷编辑桥接，2026-09-26）

> 本节为**追加**（只增不改）；契约细节同时冻结进 `FROZEN.md`「桥接契约追加」。**无 IPC / Python 改动**；
> `session.json` 格式版本仍为 **2**。

- **契约追加（尾部，授权四项）**：
  - `IEditSession.SourceImage`（`string?`，只读）：源图反查键。`EditSession` 可写；`SetRoot` /
    `ResetToRoot` / 多图 `SetRoot` 置 `imagePaths[0]`；`NewSession` 置 null；`Restore` 签名不动，
    由 `SessionLoader.LoadFromJson` 在 `Restore` 后赋值 `dto.SourceImage`。
  - `IProjectService.FindBySourceImageAsync(string normalizedPath, CancellationToken)`：规范化 +
    `OrdinalIgnoreCase` 比较 `source_image`，返回最近创建的项目。
  - `CommandDefinition.Quick`（默认 false）+ `ShortcutLabel`（`string?`）：JSON `quick` / `shortcut_label`；
    `CommandTemplateService.NormalizeLora` 逐字段重建处补齐（并修 `FixedResolution` 静默丢失）。
  - `ICommandParser.ParseAsync(input, session, imageCount, resolution, outputPath, ct)` 新重载：
    非空时向 `EditStep.Parameters["output_path"]` 注入；既有三签名不变，既有重载委托并传 `null`。
- **附属**：`ProjectSummary.SourceImage`（init-only 附加属性，主构造签名不变）；`Agent.Project.PathNormalizer`
  （`internal static`，非 Contracts；经 `InternalsVisibleTo` 供 App 使用）。
- **session.json**：顶层新增 `source_image`（绝对路径）；改名 `WriteMetadataNameAsync` 字段级重建拷贝该键；
  旧数据缺失 → null；**version 仍为 2**。
- **CLI / notify / engine.lock**：见 `FROZEN.md`「B.3 / B.4 / B.5」。
- **IShellContext**：新增 `App/Shell/IShellContext.cs`（`LoadSettings` / `TemplateDirectory` / `LaunchRequested`），
  `ShellService` 实现，`AppContext.Create(IShellContext)`；删除无头专用 context。
- **App 层新增（非契约）**：`EngineLock`、`NotifyWriter` / `NotifyMessage` / `NotifyStatus`、
  `HeadlessQuickRunner` / `IQuickRunHost` / `QuickRunHost`；`MainWindow.ApplyLaunchRequest` 先判 `IsQuick`
  走就地快捷路径并立即 return（P1-A，绝不 `_vm.ApplyRequest`）；`--image` 且非 quick 反查打开项目（P2-C）；
  编辑器手动任务在提交路径持 `engine.lock`。
- **`Template/commands.json` 的 `quick` 字段**：`quick:true` 的条目（本次为 `/去水印` `/去背景` `/全景`）
  由 ZIV 快捷菜单读取（授权只读通道）；`shortcut_label` 为菜单显示名，缺省回退到 `name`。
  `CommandParser.BuiltIn.cs` 兜底副本已同步。

## 23. 追加说明：设置功能（SettingsWriter / OpenFolder / SettingsWindow，2026-09-26）

> 编号说明：`## 22.` 已被占用，本节取下一个空闲编号 **23**。

- **settings.ini `[models]`**（非 Contracts，App 内部数据文件）：`dit_path` / `te_path` / `vae_path`（绝对路径，空 = 清除）。
  `SettingsLoader.Load` → `BackendSettings.DitPath` / `TePath` / `VaePath`（缺省 / 空 → null）。
- **`SettingsWriter`**（`App/Shell/SettingsWriter.cs`，`internal static`）：`WriteModelPaths` / `WritePythonExe`；
  共享 `WriteSectionValues`；只改目标键，保留注释 / 其他行 / 顺序；非空值 `Path.GetFullPath` 归一化；
  原子写（temp + `File.Move(overwrite:true)`）；失败抛 `ApplicationException`。
- **`IShellContext` 追加**（additive，App/Shell）：`OpenFolder(string)`、
  `PickFolderAsync(Window, string, string?, CancellationToken)`、`PickFileAsync(Window, string, string?, CancellationToken)`；
  `ShellService` 实现（打开失败不抛；选择器异常 → null）。
- **`AppContext.BuildBackendEnvironment(BackendSettings)`**：非空模型路径注入
  `ZIV_AI_DIT_PATH` / `ZIV_AI_TE_PATH` / `ZIV_AI_VAE_PATH`；空值不注入；`PythonBackendOptions` 类型不变。
- **A9 默认值清理**：`SettingsLoader` 的 `PythonExe` / `Script` 改为 `Load` 内按程序目录运行时解析；
  `PythonProcessManager.PythonExe` / `Script` 默认 `string.Empty`；`FindTemplate` 候选改 `settings.ini.template`。
- **App 层新增（非契约）**：`SettingsWindow.axaml(.cs)`、`MessageDialog.axaml(.cs)`；
  `MainWindow.PART_BtnSettings`（`IconSettings`）。
- **债务（A10）**：`COMFY_ROOT` / `REPO_ROOT` 不在本期范围（v1）。

## 24. 追加说明：settings.ini 运行时位置 + Template 同源（2026-09-27）

- **settings.ini 运行时唯一文件 = 程序目录**（Z14）；仓库根 `settings.ini` **不再**是构建拷贝源
  （`ZivAiEditor.App.csproj` 改为拷贝 `settings.ini.template`）。
- **首启播种**：程序目录无 `settings.ini` 时，`SettingsLoader.EnsurePresent` 从
  `settings.ini.template` 播种；程序目录模板直接采纳，祖先回退仍要求 `DOC/FROZEN.md` 守卫。
- **Template 同源**：`AppContext.BuildBackendEnvironment(BackendSettings, string templateDirectory)`
  新增注入 `ZIV_AI_MODELS_REGISTRY` / `ZIV_AI_LORA_REGISTRY`（C# `TemplateDirectory`，`File.Exists` 才注入），
  使 C# 与 Python 读同一份 `models.json` / `loras.json`；Python 零改。
- **签名变更（App 内部）**：`AppContext.BuildBackend(BackendSettings, string)` /
  `BuildBackendEnvironment(BackendSettings, string)`；`SettingsLoader.FindTemplate(string)`。

## 25. 追加说明：settings script 行 + 脚本前置校验（2026-09-27）

- `SettingsWriter.WriteScript(settingsPath, scriptPath)`：写 `[backend] script`（键值替换 / 非空绝对化 / 空清键）。
- `SettingsWindow`「Python 环境」组第二行「main.py 脚本」（`PART_Script` / `PART_BrowseScript`）：
  预填 `BackendSettings.Script`，保存写入 `[backend] script`。
- `PythonScriptValidator.Validate`（新文件 `src/ZivAiEditor.Backend/PythonScriptValidator.cs`）：
  `BuildStartInfo` 前置校验（非空 / 目录存在 / 文件存在），失败抛 `ApplicationException`（可读信息），
  替代 Win32「目录名称无效」。

## 26. 追加说明：COMFY_ROOT 纳入设置（2026-09-27）

- `SettingsWriter.WriteComfyRoot(settingsPath, comfyRoot)`：写 `[backend] comfy_root`（键值替换 / 非空绝对化 / 空清键）。
- `SettingsWindow`「Python 环境」第三行「ComfyUI 目录」（`PART_ComfyRoot` / `PART_BrowseComfy`）。
- `AppContext.BuildBackendEnvironment(settings, templateDirectory)` 注入 `ZIV_AI_COMFY_ROOT`（目录存在才注入）。
- Python：`config._resolve_comfy_root()`（env `ZIV_AI_COMFY_ROOT` > 开发默认 > `sys.executable` 反推）；
  `model_loader.prepare_environment` 对无效根抛可读 `RuntimeError`。

## 27. 追加说明：卡死判据 + 兜底机制（2026-09-27）

- **`PythonBackendOptions`**（追加）：`StuckTimeoutMs=60_000`（L1 无 progress 判卡，仅采样阶段）、
  `CancelConfirmTimeoutMs=5_000`（等 canceled 确认）；**移除** `SamplingTimeoutMs`（L4：采样首帧 `CancelAfter(Infinite)`，采样无上限，由 L1 兜底）。
- **`IpcInferenceClient`**（追加，不改既有签名）：
  - `public event Action<StuckRecoveryInfo>? StuckRecoveryTriggered`；`public sealed record StuckRecoveryInfo(string TaskId, string Reason, string Action)`。
  - `internal string? FailureLogPath`（测试缝）；`internal static bool IsMechanismFailure(string? message)`（L2 关键字）。
  - `internal void EnsureWatchdogStarted()`；私有 `HandleStuckAsync` / `RecordFailure`（L3）。
- **`AppContext`**（追加）：`public event Action? StuckRecovery`（转发 client 事件）。
- **`FlowRunner`**（追加）：`public void NotifyStuckRecovery()`；私有 flag + `internal string TakeFailureText(string)`。
- **`ConfirmDialog`**（追加）：`static Task<bool?> ShowAsync(Window, string message, string yesText, string noText)`。
- **`MainWindow`**（追加）：`internal int UserPromptAfterMs=300_000`（U1）；`internal void NotifyStuckRecovery()`；
  私有 `RunWithPatienceAsync<T>` / `WatchPatienceAsync`。
- 不改 `IInferenceClient` / IPC 契约 / 命令集；`diag_vram.py` 零改。

## 28. 追加说明：EditNode record 化 + 每节点耗时（2026-09-27）

- **`IEditNode`**（尾部追加）：`int? DurationMs { get; }`（端到端耗时毫秒；旧项目 → null）。
- **`IEditSessionWriter`**（尾部追加）：`void SetNodeDurationMs(string nodeId, int? durationMs)`（原地重建；未知 id no-op；null 清除）。
- **`EditNode`**：由 `EditSession.cs` 内嵌类改为独立文件 `src/ZivAiEditor.Agent/Session/EditNode.cs` 的 `public sealed record EditNode : IEditNode`；新增 `public int? DurationMs { get; init; }`。原有字段/`init` 语义不变；6 个原地重建站点改用 `with`。
- **持久化**：`SessionFileNode` 追加 `duration_ms`（`[JsonIgnore(WhenWritingNull)]`）；项目格式保持 **v2**（additive，同 `source_image` 先例）。
- **行为**：`RebuildContext` 气泡按 `DurationMs` 显示「XX.X秒 完成」；无则「完成」。

## 29. 追加说明：批次 1 收尾 A5 / A6 / B12（2026-09-27）

- **无契约签名变更**：本次未新增 / 修改任何 Contracts 接口成员。以下均为实现层语义变更，仅记录以免误用。
- **`ProjectService.GetDirectory(string) : string`**：签名不变；不安全 id（含分隔符 / `..` / 越界）改为返回 `PathSanitizer.InvalidDirectory`（`Directory.Exists == false`）。
- **`SessionStore`**：`SaveAsync` / `LoadAsync` 对不安全 id 抛 `ProjectCorruptException`；`DeleteNodeArtifacts` 对不安全 id 静默 return；私有 `GetDirectory` 经 `PathSanitizer.ResolveDirectory`。
- **`ProjectService.SetLastProjectIdAsync`**：新增拒收不安全 id（不落盘）。`RenameAsync` 不安全 id → no-op。
- **新增类型（Agent 内部，非 Contracts）**：`ZivAiEditor.Agent.Project.PathSanitizer`（static）：`bool IsSafe(string?, string)`、`string ResolveDirectory(string?, string)`、`string InvalidDirectory`。
- **`EditSession.RemoveSubtree(string)`**：签名不变；新增不变式「current 在被删后代中 → 重定向到保留的 `nodeId`」。
- **`ImageViewModel.ClampOffset()`**：签名不变；溢出轴范围改为 `[−ViewportSize, ScaledSize]`（允许完全露白）；缩态行为不变。
- **不做**：不改 IPC / 命令集 / 其它 Contracts 类型；不改缩放锚点 / 双击 / resize 逻辑。

## 30. 追加说明：批次 2A 清洁度修复（2026-09-27）

- **`DiagLog` 迁址（D1）**：`public static class DiagLog` 由程序集 `ZivAiEditor.Contracts`（命名空间 `ZivAiEditor.Contracts.Diagnostics`）迁到新程序集 `ZivAiEditor.Diagnostics`（命名空间 `ZivAiEditor.Diagnostics`）。成员 `IsEnabled` / `Log(string)` / `Exists(string?)` / `DescribeCrop(CropSpec?)` **签名不变**。消费者（Agent/Backend/App）改为 `using ZivAiEditor.Diagnostics;`。Contracts 不再包含诊断类型。
- **`OptimizationOptions` 线格式（D4）**：见下条批次 2A 记录 —— `MagCache` / `MagCacheThresh` 追加 `[JsonPropertyName("magcache")]` / `[JsonPropertyName("magcache_thresh")]`（注解，API 形状不变）。
- **不做**：不改 IPC 帧格式 / 命令集 / 其它 Contracts 类型。

## 31. D4 线格式确认：OptimizationOptions（2026-09-27）

- **`ZivAiEditor.Contracts.Inference.OptimizationOptions`**：`bool MagCache` → wire 键 `magcache`；`double MagCacheThresh`（默认 0.24）→ wire 键 `magcache_thresh`。实现方式为成员级 `[JsonPropertyName]`（**不改属性名/类型/默认值，非签名变更**）。
- **原因**：`IpcJsonContext` 的 `SnakeCaseLower` 策略会把 `MagCache` 渲染为 `mag_cache`，与 `contracts/ipc-protocol.md` 及 Python `handlers.py` 不一致；显式 `JsonPropertyName` 优先于命名策略。
- **锁定**：`IpcSubmitMapperTests.OptimizationOptions_Serialize_To_Documented_Keys` 经真实 `IpcJsonContext` 断言键名。
- **不做**：不改 Python / 不改 `ipc-protocol.md`。

## 32. P1-2 行为说明：SessionSignature 覆盖范围（2026-09-27）

- **`SessionSignature.Compute(IEditSession)`**：签名不变（`public static string Compute(IEditSession)`）。**语义变更**：新增覆盖 `SourceImage` 与每节点的 `Command` / `ImagePaths` / `UsedImagePaths` / `Rerun`（含 `ResolutionPolicy` 全 7 字段 + `AdditionalImages`）。
- **兼容性**：新签名 = 旧签名 + 独立尾块（`||src=…|`）；**旧签名字节不变**（严格前缀）→ 同一会话在升级前后不会因算法变化被判「脏」。
- **`DurationMs` 不纳入**（运行指标，非用户内容）。
- **不改**：`IEditSession` / `IEditNode` 契约成员；无签名变更。

## 33. Z-008 铺垫：Headless 测试宿主（2026-09-27）

- **契约**：无变化（未改 Contracts / 未改产品接口 / 未改 `MainWindow` 构造签名）。
- **测试侧宿主**：`ZivAiEditor.Tests/UI/TestApplication`（继承 `Application`）经 `[assembly: AvaloniaTestApplication]` 注册；`HeadlessTest.Run(Action)` 经 `Avalonia.Headless.HeadlessUnitTestSession` 在 UI 线程执行测试体。
- **依赖**：`Avalonia.Headless` 12.1.1（仅测试项目）。**不用** `Avalonia.Headless.XUnit`（其依赖 xUnit v3，与本仓 xUnit v2 不兼容）。
- **不改**：`ShellService` 仍为 `internal sealed` 具体类（测试用真实实例，接受其 Mutex+命名管道副作用）。

## 34. 8K 显示代理：新增端口与实现（2026-09-27，只增）

- **Contracts**：**无变化**（未新增/修改任何 Contracts 成员）。
- **`ZivAiEditor.Imaging.ProxyImageCache`**（新增 `public static`）：`DefaultMaxSide=2560`；`ProxiesRootDirectory`；`ReadPixelSize(string?) : (int,int)`；`TryGetOrCreate(string,int=2560) : string?`（≤上限直通源文件，否则内容寻址代理）；`SaveProjectProxy(string,int,string) : bool`；`CleanupAll()`。内部实现，非 Contracts。
- **`ZivAiEditor.App.Imaging`**（新增，App 层）：`DisplayImage`（record：`Bitmap` / `SourcePixelSize` / `ProxyPath?`）；`IDisplayImageLoader`（`const MaxDisplaySide=2560`；`LoadDisplay` / `LoadDisplayAsync`）；`DisplayImageLoader`（默认实现）；`DisplayProxyPersistence`（`internal static`，`WriteNodeProxies(IReadOnlyList<IEditNode>,string,int)`）。
- **`ZivAiEditor.UI.Imaging.ImageViewModel`**：新增 `public const int MinFitZoomPercent = 1`；`FitZoomPercent` / `FitWithMargin` 的底改为 `MinFitZoomPercent`（**签名不变**）；`MinZoomPercent=10` 语义收窄为「手动缩出下限」。
- **`ZivAiEditor.App.Controls.PanZoomCanvas`**：新增 `public Size SourceSize { get; set; }` 与 `public static Rect ComputeDrawRect(Size,int,Size,Vector)`；`Render` 改按 `SourceSize × Zoom`（**未改既有成员签名**）。
- **`ZivAiEditor.App.Controls.MaskOverlayBitmap`**：新增 `BuildScaled(byte[],int,int,int,out int,out int)`；既有 `Build` / `WriteRegion` 不变。
- **`ZivAiEditor.App.Controls.ImagePreview`**：新增构造重载 `ImagePreview(IImagingService, IDisplayImageLoader)`；既有 `ImagePreview()` / `ImagePreview(IImagingService)` 保留。
- **`ZivAiEditor.Imaging.ImagingService.CleanupAll`**：签名不变；行为追加清 `_cache/proxies/`。
- **`ZivAiEditor.App.App`**：退出清理由 `CleanupSession` 改为 `CleanupAll`（App 内部）。
- **不改**：`SessionStore` / `SessionLoader` / `SessionFileDto` / `ISessionPersistence` 签名；项目格式仍 **v2**；代理为同级文件（`{nodeId}_proxy.png`），不写入 JSON。
- **不做**：不改 IPC / 命令集 / Python；未加 NuGet。

## 35. 显示坐标系合并为单层 2.5K（2026-09-27，只增）

- **Contracts 签名**：无变化。**`MaskSpec.Width/Height` 语义修订**（非签名变更）：由「主图原始像素」改为「遮罩 PNG 实际尺寸（=缓冲=显示，≤2.5K）」。`MaskSpec.FeatherPx` 仍为原图像素值。
- **`ZivAiEditor.App.Imaging.DisplayImage`**（App 内部记录，替换 `SourcePixelSize`）：`(Bitmap Bitmap, PixelSize DisplayPixelSize, PixelSize OriginalPixelSize, string? ProxyPath)`。`DisplayPixelSize` = 唯一 UI 坐标空间；`OriginalPixelSize` 仅供尺寸徽标 + 裁切换算。
- **`ZivAiEditor.UI.Imaging.ImageViewModel.ImageWidth/Height`**：语义 = **显示尺寸**（≤2.5K），非原图；**签名不变**。
- **`ZivAiEditor.App.Controls.PanZoomCanvas.SourceSize`**：语义 = 显示尺寸（不再=原图）；**签名不变**。`ComputeDrawRect` 不变。
- **`ZivAiEditor.UI.Editing.CropState`**（新增成员，无既有签名变更）：`double DisplayScale { get; set; } = 1`；`MaxPixelCount` 的有效上限 = `MaxPixelCount · DisplayScale²`。
- **`ZivAiEditor.UI.Editing.MaskState`**（新增成员）：`double DisplayScale { get; set; } = 1`；`int EffectiveBrushDiameter`；`int EffectiveFeatherPx`。
- **`ImageCropper.CropAsync` / `IImagingService.CropAsync`**：**签名不变**（2.5K→原图换算在 `ImagePreview` 侧）。
- **`ZivAiEditor.App.MainWindow.Chat`**：遮罩叠加尺寸校验改用 `DisplayImage.DisplayPixelSize`（App 内部）。
- **不改**：`SessionStore` / `SessionLoader` / `SessionFileDto` 签名；项目格式 v2；`CropSpec` 语义（仍原图坐标）。
- **不做**：不改 IPC / 命令集 / Python；无新 NuGet。

## 36. 图像预览平移改为自由平移（PS 式 5% 留边）（2026-09-27，只增）

- **Contracts 签名**：无变化。
- **`ZivAiEditor.UI.Imaging.ImageViewModel`**（语义修订，非签名变更）：
  - 平移模型改为自由平移（无视图锁定）：`Origin = (Viewport - Scaled)/2 - Offset`，`Offset=0` 居中。
  - 新增 `public const double KeepVisibleRatio = 0.05`、`public static double MaxPanOffset(double viewport, double scaled)`。
  - `ClampOffset()` 每轴钳制 `|Offset| <= MaxPanOffset`（每轴至少 5% 图像可见，无回弹；取代旧 `[-Viewport, Scaled]`）。
  - `SetZoomAt` 锚点保持改用 post-zoom 尺寸。
- **`ZivAiEditor.App.Controls.PanZoomCanvas.ComputeDrawRect`**：改为居中 − offset；**签名不变**。
- **行为**：适应窗口 / 小图可拖动；溢出轴默认居中；拖到极限保留 5% 可见（**取代** B12 的整视图锁定与全白范围）。
- **不做**：不改 IPC / 命令集 / Python；无新 NuGet。

## 37. 插件标准形式（batch 1）（2026-09-28，只增）

- **注册**：`Template/plugins.json` 的 `plugins[]`；字段 `id` / `display_name` / `version` / `dir` /
  `entry` / `deps` / `enabled_by_default` / `description` / `kind`。
- **目录**：`dir` 相对程序目录（允许绝对）；入口默认 `__init__.py`（`entry` 覆盖）。
- **入口约定**：模块在 import 时**无副作用**（不加载模型 / 不占 GPU）；由加载器以
  `spec_from_file_location` 独立命名空间（`zivai_plugin_<id>`）导入；可选导出 `PLUGIN_META`
  （id / display_name / version / capabilities）与 capability 可调用对象（批 3 接线）。
- **依赖**：只在 `deps` 声明 Python import 名；`plugins.loader.check_deps` 用 `find_spec` 检查，
  **不安装**（无新依赖）。
- **开关**：`settings.ini [plugins] <id> = 1|0` → env `ZIV_AI_PLUGIN_<ID>`；缺省 registry 默认。
- **不做**：不改 IPC / 命令集；无新 NuGet。

## 38. 插件 capability 调度（batch 3）（2026-09-29，只增）

- **契约**：插件导出 `PLUGIN_META = {id, display_name, version, capabilities}`；capability 即同名
  模块级可调用对象。
- **调度**：`python/server/plugins/dispatch.py`（纯 CPU）：
  - `active_plugins()` = `loader.load_registry()` ∩ `loader.enabled(id, entry)` ∩ `loader.load_plugin(id)`；
  - `call(capability, context)`：按注册表顺序调用启用插件暴露的该 capability，返回**首个非 None**；
    未暴露者跳过，抛异常者记 warning 跳过——**永不抛到管线**。
- **capability `sampling_plan`**：`sampling_plan(context: dict) -> dict | None`。
  - context（只读）：`op` / `model` / `clip` / `vae` / `latent` / `mask` / `prompt` / `image_path` /
    `mask_path` / `steps` / `denoise` / `seed` / `cfg` / `sampler_preset` / `model_id`。
  - 返回 `None` = 放弃；否则可选键：`model`（已打补丁）/ `skip_shift`(bool) / `sigmas`(1-D tensor) /
    `sampler_name` / `scheduler` / `cfg` / `steps` / `cleanup`(finally 调用)。
  - 管线语义：`sigmas` 非 None → `comfy.sample.sample_custom` + `samplers.sampler_object`；否则原
    `comfy.sample.sample` 路径不变；`skip_shift` 缺省且带 sigmas → 跳过 ModelSamplingAuraFlow。
- **首个插件**：`qwen21-viggle-6step`（`plugin_packs/qwen21-viggle-6step/`，默认关）——仅无掩码图像编辑
  （`op=="inpaint"` ∧ 有主图 ∧ 无 mask ∧ `denoise≈1.0`）接 6 步蒸馏 LoRA；LoRA 路径由 id
  `qwen21-viggle-turbo-6step`（`Template/loras.json`）解析。
- **不做**：不改 IPC / 命令集；无新 NuGet；不新增 CLI。

## 39. 插件 META 上浮（数据驱动）（2026-09-29，只增）

- **背景**：插件的 `PLUGIN_META`（version / capabilities）原先只在 Python 模块内，C# / 设置窗口看不到。
- **方案**（**不改 IPC**，数据驱动）：`Template/plugins.json` 每条插件追加
  `version`（string）与 `capabilities`（string[]），取值与插件模块的 `PLUGIN_META` **保持一致**。
- **C# 读取**：`PluginFileDto.Version` / `Capabilities` → `PluginDescriptor.Version` / `Capabilities`
  （`PluginRegistry`）；设置窗口插件行显示 `v<version> · capabilities: <a, b>`。
- **同步约束**：**无跨语言校验**——插件作者改模块 `PLUGIN_META` 时**必须同步** `plugins.json`
  的 `version` / `capabilities`，否则 UI 显示与运行期能力可能不一致（风险已知，见 `FROZEN.md`）。
- **「检查安装」按钮**：原置灰的「检查依赖」改为 **「检查安装」**——C# 侧仅做「目录 + 入口文件」
  存在性检查（**不涉 IPC**）；Python 依赖探测（`find_spec`，`plugins.loader.check_deps`）仍留后续（需后端）。
- **不变**：IPC 契约 / 命令集 / 分层依赖方向；无新 NuGet / Python 依赖。

## 40. IPC error code `lora_unavailable`（2026-09-29，只增）

- **新增**：Python 后端在「**被请求使用**的 LoRA 权重文件缺失」时返回
  `{"type":"error","code":"lora_unavailable","message":"ValueError: LoRA 文件不存在：<path>"}`
  （`handlers._run_submit` ← `loras.resolve_path(validate=True)`，小收尾批 · 挂账-2）。
- **契约兼容**：`error.code` 为**自由文本**（`contracts/ipc-protocol.md:140`），**不是 schema 变更**，契约不动。
- **C# 消费**：`IpcInferenceClient.Receive.cs:211/287` 原样读取 `code` →
  `InferenceBackendException.Code`（`InferenceProgressExtensions.cs:75`）；**无枚举 / switch → 无需改 C#**；
  下游按 `code` 分支（UI 文案 / notify）需知晓该值。
- **触发面**：仅当请求**实际引用**了某 LoRA 且其文件不存在；未引用条目不受影响。
- **登记**：见 `DOC/FROZEN.md` 尾部「登记（2026-09-29）」R1。

## 41. 数据驱动接缝系统（地基批 S1+S2）（2026-09-29，只增）

- **背景**：加插件（尤其新类型接缝）当前需动基础管线（`pipeline.py` 硬编码 7 处 `plugin_sampling.*`
  调用 + 新建 `plugin_*.py`）。目标拆为「接缝系统地基批（S1+S2）→ S3 接 pipeline → S5 C# UI →
  S6 Viggle 迁移」；**本段只登记 S1+S2 地基，未接管线**。
- **接缝定义**：6 个固定锚点 `SEAMS = ("before_encode", "after_encode", "before_sample",
  "after_sample", "before_decode", "after_decode")`（`before_save` / `after_save` **不纳入**）。
- **新模块 `python/server/seams.py`**（纯 CPU，顶层无 torch / comfy）：
  - 每接缝一个**纯函数 reducer** `_apply_<seam>(ctx, patch) -> dict`：按**白名单**把 patch 键并入
    ctx 副本、非白名单忽略、不就地改原 ctx。白名单见源码 `_WHITELIST`。
    `after_decode` 以 `image.size` 为准归一 `width` / `height`。
  - `apply(anchor, ctx) -> dict`：**唯一对外入口 / 唯一白名单边界**；`anchor ∉ SEAMS` 抛
    `ValueError`（开发者错误）；内部**延迟 import** `plugins.dispatch` 调 `call_chain`，再经 reducer 过滤。
  - `collect_cleanup(ctx) -> list[callable]`：抽出 `cleanup` 键供 `_run_once` 的 `finally` 逆序执行；不改 ctx。
  - `LEGACY_CAPABILITY_MAP = {"sampling_plan": "before_sample"}`（旧 capability 名 → 接缝名，长期保留）。
- **`python/server/plugins/dispatch.py` 扩展**（`call` / `active_plugins` **不变**）：
  - `_seams_for(plugin_id, module, entry=None) -> list[str]`：同一条 registry entry 内字段级回退
    **`entry["seams"]` > `entry["capabilities"]`（经映射表）**；两者都不中 → `[]`。
    **不读 `PLUGIN_META.seams`**（seams 只放数据文件）。`entry` 由调用方一次性传入（避免每插件重读注册表）。
  - `call_chain(seam, ctx) -> dict`：按 `plugins.json` 数组顺序**链式**调用；不挂该接缝的插件跳过；
    传插件 **ctx 副本**（防就地改）；返回 dict patch → `ctx.update(patch)` 累积；插件异常 / 非 dict → warning
    跳过，**永不抛**。**不做白名单过滤**（过滤在 `seams.apply`）。
  - 函数名解析 `_fn_for`：先按接缝名，`before_sample` 无则回退旧名 `sampling_plan`（D6/D7）。
- **兼容性（双读）**：旧插件（`capabilities:["sampling_plan"]`，仅导出 `sampling_plan`）无需改动即被
  `call_chain("before_sample", …)` 路由；新插件用 `seams` 字段 + 与接缝同名函数。
- **不做**：不改 IPC / 命令集；无新 NuGet / Python 依赖；`pipeline.py` 零改（S3 才接）。
