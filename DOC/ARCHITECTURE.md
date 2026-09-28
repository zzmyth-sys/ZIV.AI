# ZIV.AI 架构设计（ARCHITECTURE）

- 文档状态：草案 v0.1（待评审）
- 目标：在**独立解决方案**内实现「三图 + 一 prompt → 计划 → 多步执行」，复用 `ZIV.Core` /
  `ZIV.Imaging`，与 ZIV 保持**进程隔离**，推理经**本地 HTTP 契约**访问 Python 后端
- 上游：[`SPEC.md`](SPEC.md)；冻结见 [`FROZEN.md`](FROZEN.md)；契约草案见
  [`../contracts/openapi.yaml`](../contracts/openapi.yaml)

---

## 1. 设计目标

| 目标 | 手段 |
|---|---|
| 独立解决方案 | 独立 `ZIV.AI.sln`，不进 `ZIV.sln`，不共享构建产物（Z25） |
| 进程外插件 | 与 ZIV 只经 CLI / URL 协议 / 命名管道通信（Z27） |
| 独立运行 | `ZivAiEditor.App.exe` 启动不要求 ZIV 存在（Z28） |
| 契约纯净 | `ZivAiEditor.Contracts` 只放接口与模型，不依赖 Avalonia / 平台（Z3） |
| 单向依赖 | `App → UI → Agent/Tools/Backend → Contracts → ZIV.Core/Imaging`（Z2） |
| 推理进程隔离 | C# 不加载 Python 运行时，只经 `IInferenceClient`（Z17） |
| GPU 串行 | 单队列 + 优先级 + OOM 降级（Z18） |
| 遮罩可信 | 二值 PNG，原图 / 遮罩分开传，C# 不预填充（Z19） |
| 状态可恢复 | 任务 / 步骤 / 参数 / 路径 / 耗时入 SQLite（Z20） |
| 显存可控 | 空闲卸载 + 保留最近一个 + 超时可配（Z21） |
| 规划可降级 | LLM 失败回退默认计划，`IPlanner` 可替换（Z22） |
| 版本解耦 | AI 模块与 Python 后端各自更新，经 OpenAPI 契约通信（Z23） |
| 原图安全 | 输出新文件，中间结果保留，可回溯（Z24） |
| 共享不复制 | `ZIV.Core` / `ZIV.Imaging` 仓库内一份，项目引用或 NuGet（Z26） |

## 2. 分层总览

```
 L6  ZivAiEditor.App          Avalonia 壳、装配、平台服务、单实例、CLI / URL / HTTP 入口
 L5  ZivAiEditor.UI           视图模型 / 纯规则库（无 Avalonia；视图在 App）—— 正名见 Z-007
 L4  ZivAiEditor.Agent        Planner / Executor（步级编排）
     ZivAiEditor.Tools        IEditTool 实现 + ToolRegistry
     ZivAiEditor.Backend      IInferenceClient 实现（IpcInferenceClient）+ Python 进程管理
     ZivAiEditor.Imaging      IImagingService 实现（crop / mask 栅格原语）
 L3  ZivAiEditor.Contracts    契约与模型（IInferenceClient / IEditTool / IPlanner /
                              IExecutor / IToolRegistry + EditPlan / EditStep / ToolInput /
                              ToolResult / MaskSpec / TaskState），不依赖 Avalonia / 平台
 L2  ZIV.Core / ZIV.Imaging   共享库：契约、值对象、编解码、图像变换（复用，不复制）
 ─────────────────────────── 进程边界（Z17）───────────────────────────
      Python 推理后端          ComfyUI 管线（D-7；独立进程，只经 IPC / Named Pipe，D-8）
```

**核心分工**

- `ZivAiEditor.Contracts`：**只有契约与值对象**，可被所有上层引用；不碰 Avalonia、不碰 IO
- `ZivAiEditor.Backend`：**只做推理访问与 Python 进程管理**，实现 `IInferenceClient`；不碰 UI
- `ZivAiEditor.Tools`：**只做单步编辑动作**，实现 `IEditTool` 并注册到 `ToolRegistry`
- `ZivAiEditor.Agent`：**只做步级编排**（Planner + Executor），经契约调用工具与推理，不碰 UI / IO；
  **流程级编排**（提交 / 重跑 / 取消 / `/生成`）在 `ZivAiEditor.App` 的 `App/Flows`（见 §4 修订说明）
- `ZivAiEditor.UI`：**只做视图模型 / 交互规则**（无 Avalonia；视图渲染在 `App`），解码经 `ZIV.Imaging`，
  推理只经 `IInferenceClient` 契约
- `ZivAiEditor.Imaging`：**只做图像栅格原语**（裁切 / 遮罩导出 / 羽化），实现 `IImagingService`
- `ZivAiEditor.App`：**唯一装配处与平台实现处**，构造依赖并注入；承载对外接口

> 命名：解决方案 `ZIV.AI.sln`；项目前缀统一 `ZivAiEditor.*`；可执行入口
> `ZivAiEditor.App.exe`（见 `SPEC.md` §1）。

## 3. 功能块地图

```
┌── ZivAiEditor.Contracts（契约内核）──────────────────────┐
│  IInferenceClient · IEditTool · IPlanner · IExecutor      │
│  IToolRegistry                                            │
│  EditPlan · EditStep · ToolInput · ToolResult             │
│  MaskSpec · TaskState · PlanRequest · 进度 / 状态枚举     │
└───────────────────────────────────────────────────────────┘
        ▲                        ▲                    ▲
        │                        │                    │
┌── Agent ────────────┐  ┌── Tools ──────────┐  ┌── Backend ───────────┐
│ Planner（LLM / 降级）│  │ InpaintTool        │  │ IpcInferenceClient    │
│ Executor（串行队列） │  │ UpscaleTool        │  │ PythonProcessManager  │
│ 重跑 / 取消 / 进度   │  │ RemoveObjectTool   │  │ 健康检查 / 任务轮询   │
│                      │  │ StyleTransferTool  │  │ 空闲卸载（后端协作）  │
│                      │  │ ToolRegistry       │  └───────────────────────┘
└──────────────────────┘  └────────────────────┘
        ▲
┌── UI ─────────────────────────────────────────────────────┐
│  AiEditorWindow · MaskCanvas（画笔 / 橡皮 / 撤销 / 二值导出）│
│  TaskCardStream · PromptTemplatePanel · ImageSlotPanel     │
└────────────────────────────────────────────────────────────┘
        ▲
┌── App（装配 + 平台 + 对外接口）─────────────────────────────┐
│  依赖装配 · 单实例 · CLI · URL 协议（zivai://）· 本地 HTTP  │
│  设置持久化 · SQLite 任务库 · 平台路径解析                  │
└─────────────────────────────────────────────────────────────┘
```

### 归属速查

| 功能 / 子模块 | 归属 | 说明 |
|---|---|---|
| 推理访问（IPC） | `ZivAiEditor.Backend` | `IpcInferenceClient : IInferenceClient` |
| 裁切 / 遮罩导出 / 羽化 | `ZivAiEditor.Imaging` | `ImagingService : IImagingService` |
| Python 进程生命周期 | `ZivAiEditor.Backend` | 启动 / 健康检查 / 关闭 / 端口分配 |
| 计划生成（LLM） | `ZivAiEditor.Agent` | `LlmPlanner : IPlanner` + `FallbackPlanner` |
| 步骤执行 / 重跑 / 取消 | `ZivAiEditor.Agent` | `Executor : IExecutor`，单队列 |
| 单步编辑工具 | `ZivAiEditor.Tools` | `IEditTool` 实现 + `ToolRegistry` |
| 遮罩绘制 | `ZivAiEditor.UI` | `MaskCanvas`（Avalonia 自绘） |
| 任务卡片流 | `ZivAiEditor.UI` | `TaskCardStream` |
| 提示词模板 | `ZivAiEditor.UI` | `PromptTemplatePanel` |
| 任务持久化 | `ZivAiEditor.App` | SQLite（`Z20`），实现契约的存储接口 |
| 对外接口 | `ZivAiEditor.App` | CLI / URL 协议 / 本地 HTTP |
| 图像解码 / 变换 / 编码 | `ZIV.Imaging` | 复用，不新造 codec（Z26 / Z5） |
| 共享契约 / 值对象 | `ZIV.Core` | `SKImageRef` 等直接引用，不重新定义 |

## 4. 依赖规则

> **修订（2026-09-21，Step 1 · 裁判裁决）**：本节的 UI 编译期引用措辞已更新——
> `UI` **可以**在编译期引用 `Agent` / `Tools` / `Backend` 程序集（便于 `App` 装配与类型贯通），
> 但**代码中只允许使用 `Contracts` 的接口**，不得直接调用其实现类。同时明确 Step 1
> **不引用** `ZIV.Viewer`（查看器能力不进 ZIV.AI）。以下正文为修订后版本。
>
> **修订（2026-09-25，独立审查 R-3）**：上述"UI 可编译期引用 `Agent` / `Tools` / `Backend`"的许可
> **实现未采用**：当前 `ZivAiEditor.UI` **只引用 `Contracts`**（比正文规则更严），视图在 `App`。
> 同时新增域实现 `ZivAiEditor.Imaging`（`IImagingService`）。并明确：**步级编排在 `Agent`，
> 流程级编排在 `App/Flows`**。以下规则 3 与新增规则 9 为当前状态。

```
        ZivAiEditor.App（装配 + 平台 + 对外接口）
         │  构造并注入依赖
         ▼
   ZivAiEditor.UI
         │  只依赖 Contracts 的接口（IInferenceClient / IPlanner / IExecutor ...）
         │  可编译期引用 Agent / Tools / Backend 程序集，但不得使用其实现类
         ▼
   ZivAiEditor.Agent / ZivAiEditor.Tools / ZivAiEditor.Backend
         │  各自只实现 / 使用 Contracts 的接口
         ▼
   ZivAiEditor.Contracts
         │
         ▼
   ZIV.Core ──► ZIV.Imaging（Imaging 依赖 Core）
```

1. `ZivAiEditor.Contracts` 只依赖 `ZIV.Core`（+ BCL）；若需图像变换再依赖 `ZIV.Imaging`；
   **不依赖 Avalonia、不依赖平台 API、不依赖其他 `ZivAiEditor.*`**
2. `ZivAiEditor.Agent` / `ZivAiEditor.Tools` / `ZivAiEditor.Backend` 只依赖 `Contracts`
   （+ `ZIV.Core` / `ZIV.Imaging`）；**三者之间禁止互相引用**，协作经 `Contracts` 接口，
   由 `App` 注入装配
3. `ZivAiEditor.UI` **当前只依赖 `Contracts`**（视图在 `App`；UI 为纯视图模型 / 规则库，无
   Avalonia）。历史上曾许可"编译期引用 `Agent` / `Tools` / `Backend`"，实现未采用；若将来需要，
   仍**只允许使用 `Contracts` 的接口**（如 `IInferenceClient`），**不得直接调用其实现类**，
   能力一律经接口注入
4. `ZivAiEditor.App` 可依赖全部；负责构造实现、组装依赖、承载平台与对外接口
5. 禁止反向依赖、循环依赖、同层互相引用
6. 依赖方向严格单向：`App → UI → Agent/Tools/Backend → Contracts → ZIV.Core/Imaging`
7. `ZIV.Imaging` 只依赖 `ZIV.Core`；`ZIV.AI` 不得反向修改共享库（Z26）
8. Step 1 **不引用** `ZIV.Viewer`（查看器能力不进 ZIV.AI）；共享库只引用 `ZIV.Core` /
   `ZIV.Imaging`
9. `ZivAiEditor.Imaging` 只依赖 `Contracts`（+ `ZIV.Core` / `ZIV.Imaging`）；与 `Agent` /
   `Tools` / `Backend` 同属域实现层，四者**禁止互相引用**（2026-09-25 追加）

> **关于 UI 与 Backend 的关系**：`Backend` 是 `IInferenceClient` 的**实现方**，`UI` 是**使用方**。
> Step 1 起 `UI` 可在编译期引用 `Backend` 程序集，但**只通过 `Contracts` 中的接口**
> 使用它，不直接 new / 调用实现类；实现由 `App` 注入。这样 UI 仍可在不改代码的情况下
> 切换到命名管道实现或其他后端（Z17 / Z23）。

## 5. 跨模块接口契约草案

> 完整冻结见 [`FROZEN.md`](FROZEN.md)；下方为设计示意，字段可能省略。
> 契约先行：`contracts/openapi.yaml` 定义**跨进程**的 HTTP 契约，本节定义**进程内**的 C# 契约；
> 两者在 Step 1 一并冻结。

### 5.1 IInferenceClient（推理访问唯一入口）

```csharp
// ZivAiEditor.Contracts/Inference/IInferenceClient.cs
public interface IInferenceClient : IDisposable
{
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default);

    // 提交一次重绘 / 编辑请求，后端以任务形式异步执行
    Task<InferenceTaskHandle> SubmitInpaintAsync(
        InpaintRequest request,
        IProgress<InferenceProgress>? progress = null,
        CancellationToken ct = default);

    Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default);
    Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default);
}
```

- 实现：`ZivAiEditor.Backend.IpcInferenceClient`（Named Pipe + 长度前缀，D-8；见
  [`../contracts/ipc-protocol.md`](../contracts/ipc-protocol.md)）。
- 约束：C# 侧只经此接口访问推理（Z17）；不得 `Process.Start` 后直连 stdin/stdout 传二进制图像。

### 5.2 IEditTool（单步编辑动作）

```csharp
// ZivAiEditor.Contracts/Tools/IEditTool.cs
public interface IEditTool
{
    string Name { get; }                          // 稳定标识，写入 EditStep.ToolName
    string Description { get; }                   // 供 Planner / 模板参考
    IReadOnlyList<string> Capabilities { get; }   // 能力标签，供 Planner 选择

    bool CanHandle(EditStep step);
    Task<ToolResult> ExecuteAsync(
        ToolInput input,
        IProgress<StepProgress>? progress = null,
        CancellationToken ct = default);
}
```

### 5.3 IPlanner（计划生成，可降级）

```csharp
// ZivAiEditor.Contracts/Planning/IPlanner.cs
public interface IPlanner
{
    Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default);
}
```

- 实现：`LlmPlanner`（经 `IInferenceClient` 或本地 LLM）与 `FallbackPlanner`（默认单步计划）。
- 降级：`LlmPlanner` 解析失败 / 超时 → 返回 `FallbackPlanner` 的计划（Z22）。

### 5.4 IExecutor（步骤执行）

```csharp
// ZivAiEditor.Contracts/Execution/IExecutor.cs
public interface IExecutor
{
    Task<TaskState> ExecuteAsync(
        EditPlan plan,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    // 重跑：生成新任务，不覆盖旧记录（Z24）
    Task<TaskState> RerunAsync(
        string taskId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    Task<bool> CancelAsync(string taskId, CancellationToken ct = default);
}
```

### 5.5 IToolRegistry（工具查找）

```csharp
// ZivAiEditor.Contracts/Tools/IToolRegistry.cs
public interface IToolRegistry
{
    void Register(IEditTool tool);
    bool Unregister(string toolName);
    IEditTool? Get(string toolName);
    IReadOnlyList<IEditTool> All { get; }
}
```

### 5.6 模型

#### 5.6.1 EditPlan（计划）

```csharp
public sealed class EditPlan
{
    public string PlanId { get; init; } = Guid.NewGuid().ToString("N");
    public string SourcePrompt { get; init; } = "";
    public string MainImagePath { get; init; } = "";
    public string? ReferenceImagePath { get; init; }
    public MaskSpec? Mask { get; init; }
    public IReadOnlyList<EditStep> Steps { get; init; } = Array.Empty<EditStep>();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
```

#### 5.6.2 EditStep（步骤）

```csharp
public sealed class EditStep
{
    public string StepId { get; init; } = Guid.NewGuid().ToString("N");
    public int Order { get; init; }                 // 从 1 递增
    public string ToolName { get; init; } = "";
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? ErrorMessage { get; set; }
}
```

#### 5.6.3 ToolInput（工具输入）

```csharp
public sealed class ToolInput
{
    public string StepId { get; init; } = "";
    public string MainImagePath { get; init; } = "";
    public string? ReferenceImagePath { get; init; }
    public MaskSpec? Mask { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();
    public string WorkingDirectory { get; init; } = "";   // 中间结果落点
}
```

#### 5.6.4 ToolResult（工具输出）

```csharp
public sealed class ToolResult
{
    public string StepId { get; init; } = "";
    public bool Success { get; init; }
    public string? OutputImagePath { get; init; }   // 新文件，绝不覆盖原图（Z24）
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
```

#### 5.6.5 MaskSpec（遮罩描述）

```csharp
public sealed class MaskSpec
{
    public string MaskImagePath { get; init; } = "";  // 二值 PNG，只含 0 / 255（Z19）
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsBinary { get; init; } = true;       // 必须为 true；C# 不做预填充
    public bool Invert { get; init; }
}
```

#### 5.6.6 TaskState（任务状态）

```csharp
public sealed class TaskState
{
    public string TaskId { get; init; } = "";
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public EditPlan Plan { get; init; } = new();
    public IReadOnlyList<StepState> StepStates { get; set; } = Array.Empty<StepState>();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? OutputImagePath { get; set; }
    public string? ErrorMessage { get; set; }
}
```

#### 5.6.7 辅助类型（草案）

| 类型 | 用途 |
|---|---|
| `PlanRequest` | 三图路径 + 提示词 + 选项，`IPlanner.PlanAsync` 输入 |
| `InpaintRequest` | 单次推理请求（主图 / 遮罩 / 参数），映射 OpenAPI `/v1/inpaint` |
| `InferenceTaskHandle` | 提交后返回的 `taskId` + 初始状态 |
| `InferenceTask` | 后端任务快照（状态 / 进度 / 输出），映射 `/v1/task/{id}` |
| `HealthStatus` | 后端健康与模型加载状态，映射 `/v1/health` |
| `StepState` | 单步运行记录（状态 / 耗时 / 输出路径） |
| `StepProgress` / `TaskProgress` / `InferenceProgress` | 三级进度（`IProgress<T>`） |
| `StepStatus` / `TaskStatus` | 步骤 / 任务状态枚举 |

## 6. 通知与订阅模型

ZIV.AI 不引入事件总线（与 ZIV 一致，Z1 / D-13）。跨模块通知只有两条：

1. **C# 事件（模块内）**：如 `IInferenceClient` 实现内部的任务状态变化，订阅方在 `App` 层接线
2. **注入回调 / 进度**：需要通知上层时，`App` 传入 `Action` / `IProgress<T>`，下层调用

```
Planner 产出计划 ──► Executor 执行（App 接线：Agent 内部）
Executor 进度 ──► UI 任务卡片（IProgress<TaskProgress> 注入）
InferenceClient 任务轮询 ──► 进度回传（IProgress<InferenceProgress>）
TaskState 变化 ──► SQLite 持久化（App 订阅 / 写入，Z20）
```

规则：**下层不引用上层**；所有跨模块接线发生在 `ZivAiEditor.App`（对应 Z2 / Z6）。

### 6.1 状态依赖规则（9C.5 追加）

- 工具域状态（`ToolStateMachine` / `CompareState`）**可读取**会话域状态（如 `HasImage`）
- 会话域状态**不可读取**工具域状态
- 后端域状态（`PythonBackendState`）**独立**，不被其他域读取
- UI 局部状态（pending 气泡 / 分辨率选择）只属于 `SessionViewModel`

## 7. 模块树

```
ZIV.AI.sln
├─ src/
│  ├─ Directory.Packages.props      # 中央包版本管理（CPM）
│  │                                # （global.json 固定 SDK 10.0.401，位于仓库根）
│  │
│  ├─ ZivAiEditor.Contracts/        # 契约与模型（net8.0；引用 ZIV.Core）
│  │   ├─ Inference/{IInferenceClient.cs, InpaintRequest.cs, InferenceTask.cs,
│  │   │             InferenceTaskHandle.cs, HealthStatus.cs, InferenceProgress.cs}
│  │   ├─ Tools/{IEditTool.cs, IToolRegistry.cs, ToolInput.cs, ToolResult.cs, StepProgress.cs}
│  │   ├─ Planning/{IPlanner.cs, EditPlan.cs, EditStep.cs, PlanRequest.cs}
│  │   ├─ Execution/{IExecutor.cs, TaskState.cs, StepState.cs, TaskProgress.cs}
│  │   ├─ Imaging/{MaskSpec.cs}
│  │   └─ Enums/{TaskStatus.cs, StepStatus.cs}
│  │
│  ├─ ZivAiEditor.Agent/            # Planner / Executor 编排（net8.0；引用 Contracts）
│  │   ├─ PlaceholderPlanner.cs     # Step 1 占位（throw-only）
│  │   └─ PlaceholderExecutor.cs    # Step 1 占位（throw-only）
│  │
│  ├─ ZivAiEditor.Tools/            # 工具与注册表（net8.0；引用 Contracts）
│  │   └─ PlaceholderToolRegistry.cs # Step 1 占位（throw-only）
│  │
│  ├─ ZivAiEditor.Backend/          # IInferenceClient 实现（net8.0；引用 Contracts）
│  │   └─ IpcInferenceClient.cs     # Named Pipe 实现（Step 2 起）
│  │
│  ├─ ZivAiEditor.Imaging/          # IImagingService 实现（net8.0；引用 Contracts + ZIV.Core/Imaging）
│  │   └─ ImagingService.cs         # crop / mask 导出 / 羽化原语（迁移 4）
│  │
│  ├─ ZivAiEditor.UI/               # 视图模型 / 规则库（net8.0；只引用 Contracts，无 Avalonia）
│  │   └─ Chat / Editing / Imaging / Projects   # 视图渲染在 App（正名见 Z-007）
│  │
│  ├─ ZivAiEditor.App/              # WinExe（net8.0-windows；AOT；引用全部）
│  │   ├─ Program.cs
│  │   ├─ App.axaml(.cs)
│  │   └─ MainWindow.axaml(.cs)     # 空窗口，标题 ZIV.AI Editor
│  │
│  └─ ZivAiEditor.Tests/            # xunit（net8.0；引用 Contracts/Agent/Tools）
│      └─ ContractsSmokeTests.cs
│
├─ contracts/
│  └─ openapi.yaml                  # 跨进程契约（Step 1 冻结，8 端点）
│
├─ publish.ps1                      # 发布到 D:\Program Files\ZIV.AI（D-9）
│
├─ python/                          # Python 推理后端（独立进程，不属于 .NET 解决方案；Step 1 未创建，待 Step 3）
│  ├─ server/                       # SGLang 主路线（LightX2V 为 Step 7 可选加速）
│  ├─ models/                       # Qwen-Image-2.1 权重（本地）
│  └─ requirements.txt
│
└─ DOC/
   ├─ SPEC.md
   ├─ ARCHITECTURE.md
   ├─ FROZEN.md
   ├─ ACCEPTANCE.MD
   └─ DEVLOG.md
```

> 模块树为**草案**，Step 1 起按实际落地修订；文件名以最终实现为准，但**项目边界与依赖方向**不可破。
> 当前落地为 **8 个 `ZivAiEditor.*` 项目**（新增 `ZivAiEditor.Imaging`；见 `FROZEN.md` 1.1），各项目内的类文件随
> 后续 Step 逐步补齐。

## 8. 状态与生命周期

- **无根状态**：不存在静态中枢；`AppContext`（App 层装配容器）在
  `App.OnFrameworkInitializationCompleted` 构造一次，注入 `AiEditorWindow`（Z1）
- **初始化顺序（草案，按依赖）**：
  1. `PathResolver`（程序目录，Z14）
  2. `SqliteTaskStore`（打开 / 建库 / 迁移）
  3. `PythonProcessManager`（探测端口 / 启动后端，可延迟）
  4. `IpcInferenceClient`（依赖 3）
  5. `ToolRegistry`（注册工具）+ `ModelProfileRegistry`（模型分辨率档位，Step 6.5）
  6. `Planner`（`LlmPlanner` + `FallbackPlanner`）→ `Executor` → `Agent`
  7. `AppContext` 组装 → `new AiEditorWindow(context)`
- **Python 进程管理**：
  - 启动：优先复用已在运行的实例（探测 `/v1/health`）；否则由 `PythonProcessManager` 拉起
  - 健康检查：定时 `/v1/health`；连续失败标记后端不可用并提示
  - 关闭：应用退出时优雅关闭由本应用拉起的进程；**不关闭外部实例**
- **模型加载 / 卸载**：由 Python 后端负责加载与**空闲卸载**（Z21），C# 侧只配置超时并展示状态；
  保留最近一个模型，超时可配（`BackendOptions`）
- **任务队列**：`ExecutionQueue` 单一串行队列；交互任务优先；OOM 按策略降级重试（Z18）；
  任务状态即时写入 SQLite（Z20）
- **UI 线程约束**：推理、解码、文件 IO 在后台线程（`Task` + `CancellationToken`）；
  控件更新经 Avalonia `Dispatcher.UIThread` 回主线程（Z11）；`MaskCanvas` 绘制在主线程，
  大图栅格化在后台
- **路径解析（Z14）**：程序根 = `Path.GetDirectoryName(Environment.ProcessPath)`；
  `settings` / `tasks.db` / `output` / `_cache` / `_logs` 一律 `Path.Combine(程序根, ...)`；
  注册表只允许用于 URL 协议注册（可撤销），禁止存应用配置

## 9. 决策记录

> **修订说明（2026-09-21，Step 1 · 裁判裁决）**：本节按用户明确授权修订 **D-3 / D-7 / D-8 / D-9**
> 四行（§9 原约定「只增不改」，本次为例外并已获授权）。修订点：
>
> - **D-3**：共享库引用方式**定稿为项目引用（起步）**，NuGet 化留待分发期再评估（裁决 1）。
> - **D-7**：Python 后端**Step 1 只支持 SGLang**；LightX2V 作为 **Step 7 可选加速**（裁决 3）。
> - **D-8**：OpenAPI 契约先行，本步已冻结 **8 个端点**（原为 3 个草案）（裁决 4）。
> - **D-9**：发布目录**定稿 `D:\Program Files\ZIV.AI`**（可用 `ZIV_AI_PUBLISH_DIR` 覆盖）（裁决 2）。
>
> 其余决策行保持 Step 0 原文不变。

| 编号 | 决策 | 理由 |
|---|---|---|
| D-1 | **独立解决方案**：`ZIV.AI.sln` 独立，不进 `ZIV.sln`，不共享 `bin` / `obj` / publish | 发布节奏不同；避免构建耦合（Z25） |
| D-2 | **进程外插件**：ZIV 经 CLI / URL 协议 / 命名管道调用 ZIV.AI，不加载托管程序集 | 插件崩溃不拖垮主程序；主程序升级不迫使 AI 模块重编译（Z27） |
| D-3 | **共享库引用方式**：`ZIV.Core` / `ZIV.Imaging` **采用项目引用（Step 1 起步）**，NuGet 化留待分发期再评估；仓库内只有一份源码 | 复制会分叉（Z26）；项目引用开发期最简；分发期再评估 NuGet（裁决 1） |
| D-4 | **UI 框架沿用 Avalonia** | 与 ZIV 一致，复用既有经验与控件；`MaskCanvas` 自绘可控 |
| D-5 | **遮罩用 Avalonia 自绘**（`MaskCanvas`），不引第三方画布 | 需求简单（画笔 / 橡皮 / 撤销 / 二值导出）；避免大依赖与 AOT 风险 |
| D-6 | **任务存储用 SQLite** | 单文件、随程序目录（Z14）、事务可靠、支持恢复与重跑（Z20） |
| D-7 | **Python 后端**：**Step 2 起改为 ComfyUI v0.37.0 便携版源码，in-process 直接调管线**（不启 HTTP server）；SGLang 路线**废弃**；LightX2V / Lightning LoRA 作为 **Step 7 可选加速**再引入 | 实测确认 v0.37.0 原生支持 Qwen-Image-2.1，512² 编辑闭环可跑通；in-process 免 HTTP 栈、免官方 Embedding API（实测不存在）（Step 2 修订，取代裁决 3） |
| D-8 | **跨进程契约**：Step 2 起改为 **IPC 传输契约** `contracts/ipc-protocol.md`（Named Pipe + 长度前缀，Step 2 冻结）；`contracts/openapi.yaml` 的 8 端点**降级为 Schema 参考**（保留定义，作为 payload 形状来源） | 实测 Named Pipe 性能充足（7.91MB 2.71ms）且免 HTTP 栈；版本解耦（Z23）（Step 2 修订，取代裁决 4） |
| D-9 | **发布目录**：固定为 **`D:\Program Files\ZIV.AI`**（可用 `ZIV_AI_PUBLISH_DIR` 覆盖） | 与 Z16 保持一致；URL 协议注册依赖绝对路径（裁决 2） |
| D-10 | **Planner 可降级**：`LlmPlanner` + `FallbackPlanner` | LLM 输出不稳定，必须有确定性兜底（Z22） |
| D-11 | **推理请求串行 + 优先级**：单队列，交互优先，OOM 降级重试 | 单卡 16GB 不并发（Z18） |
| D-12 | **模型空闲卸载由 Python 后端负责** | 显存归后端管理最直接；C# 只配置超时与展示（Z21） |
| D-13 | **不设事件总线**：C# `event` + `IProgress<T>` | 与 ZIV 一致，避免隐式依赖（Z1 / Z15） |
| D-14 | **不破坏原图**：输出新文件，中间结果保留 | 编辑安全底线，可回溯（Z24） |
| D-15 | **单实例 + 本地回环 HTTP** | 重复唤起复用进程并转发；HTTP 只绑 `127.0.0.1`（§4 隐私） |
| D-16 | **图像读写复用 `ZIV.Imaging`**，不新造 codec | Z26 / Z5；避免重复实现与格式覆盖回退 |
| D-17 | **工具经 `IToolRegistry` 注册**，Planner 按能力标签选择 | 工具可增删而不改 Planner；`IPlanner` 可替换（Z22） |

> 决策编号自 D-1 起**连续**；后续新增从 D-18 续接，**只增不改**（见 `FROZEN.md` 修改铁律）。

## 10. 与 ZIV 的对照

| 维度 | ZIV | ZIV.AI |
|---|---|---|
| 定位 | 轻量图片查看器 | AI 图像编辑独立应用 + 进程外插件 |
| 解决方案 | `ZIV.sln`（不进 ZIV.AI） | `ZIV.AI.sln`（不进 `ZIV.sln`，Z25） |
| 共享库 | 拥有 `ZIV.Core` / `ZIV.Imaging` | **引用**同一份，不复制（Z26） |
| 与对方关系 | 不感知 ZIV.AI | 可被 ZIV 可选调用；不依赖 ZIV（Z27 / Z28） |
| 进程模型 | 单进程 | 至少两进程：C# 前端 + Python 推理后端（Z17） |
| 重资源 | 解码 / 渲染（CPU / 内存） | 推理（GPU / 显存），需串行与空闲卸载（Z18 / Z21） |
| 状态 | 会话内为主 | 任务持久化到 SQLite，可恢复 / 重跑（Z20） |
| 输出安全 | 另存为 | 一律新文件，原图只读（Z24） |
| 通知 | C# event + 回调 | 同左，另加 `IProgress<T>` 三级进度（D-13） |
| 契约 | 进程内 C# 接口 | 进程内 C# 接口 + 跨进程 OpenAPI（D-8） |
| UI 复用 | Avalonia | Avalonia（D-4） |

## 11. 反过度设计原则

1. **不造框架**：通知用原生 C# `event` + `IProgress<T>`，不做事件总线 / 中间件（D-13）
2. **契约只放真正共享的**：`Contracts` 只放跨项目必需的类型，不预留未用抽象
3. **薄层可合并**：某层只剩转发就并回上一层
4. **不预留多实现**：`IInferenceClient` 先做 HTTP 一份；命名管道等有真实需求再加
5. **复用优先**：图像读写 / 变换走 `ZIV.Imaging`，不重写（Z26 / Z13）
6. **工具按需**：先做「重绘 / 局部编辑」一条主线，其余工具随需求增加
7. **契约先行但不贪多**：OpenAPI 在 Step 1 冻结 8 个端点（health / inpaint / img2img /
   upscale / segment / outpaint / task 查询 / task 取消），其余端点待有真实调用方再加
8. **Planner 先确定性兜底**：先保证 `FallbackPlanner` 可用，再增强 LLM 规划

## 12. 技术选型利弊（调研支撑）

> 本节为**初版权衡**，用于支撑 D-4–D-8；具体版本 / 性能数字在 Step 1 调研核实后补入，
> 未核实处一律标注「待核实」，不写推测数字。

### 12.1 UI 框架：Avalonia（沿用 ZIV）

| 维度 | 说明 |
|---|---|
| 一致性 | 与 ZIV 同框架，抽离 / 复用经验成本最低（D-4） |
| 自绘能力 | `MaskCanvas` 可用 Avalonia 绘制 API 自绘，满足画笔 / 橡皮需求 |
| AOT | 与 ZIV 相同约束（编译绑定、资源内嵌），沿用 ZIV 的结论 |
| 风险 | 复杂控件树性能争议（ZIV 已评估，对自绘视图影响小） |

### 12.2 任务存储：SQLite

| 维度 | 说明 |
|---|---|
| 优点 | 单文件、事务、查询方便；随程序目录（Z14）；支持恢复 / 重跑（Z20） |
| 代价 | 需引入 SQLite 包并处理 AOT（待 Step 1 核实托管提供程序） |
| 备选 | JSON 文件（并发 / 查询弱，不选）、LiteDB（生态较小，待评估） |

### 12.3 Python 后端：LightX2V vs SGLang

| 维度 | LightX2V | SGLang |
|---|---|---|
| 定位 | 低延迟图像 / 视频生成推理 | 高吞吐 LLM / 多模态服务 |
| 适合 | 交互式单请求、低首帧 | 批量 / 并发规划请求 |
| 结论 | **Step 1 单主线 SGLang**（D-7）：先跑通一条路线；LightX2V 作为 **Step 7 可选加速**再引入；两者统一在 OpenAPI 契约后 | |

> 模型：Qwen-Image-2.1（本地权重）。显存与吞吐数字**待 Step 1 实测**。

### 12.4 跨进程契约：OpenAPI 3.1

| 维度 | 说明 |
|---|---|
| 优点 | 语言中立（C# / Python 都能生成客户端 / 服务端）；版本化清晰（Z23） |
| 代价 | 需维护契约与生成代码的同步；生成工具与 AOT 兼容性待核实 |
| 备选 | gRPC（需 proto 工具链）、命名管道 + 自定义协议（仅 Windows，先不做） |

> **修订（2026-09-25，R-3）**：D-8 已定稿——跨进程传输采用 **IPC（Named Pipe + 长度前缀）**
> [`../contracts/ipc-protocol.md`](../contracts/ipc-protocol.md)；`contracts/openapi.yaml` 的 8 端点
> **降级为 Schema 参考**。实现为 `IpcInferenceClient`（草案的 HTTP 客户端未采用）。

### 12.5 遮罩画布：Avalonia 自绘 vs 第三方

| 维度 | 说明 |
|---|---|
| 自绘（选） | 需求简单、依赖少、AOT 友好；坐标映射可控（D-5） |
| 第三方 | 功能过剩、依赖重、AOT 风险，且需重新验证二值导出 |

---

## 遗留项 / 待裁判裁决

> 以下事项**不在 Step 0 自行拍板**，提交裁判裁决；裁决后按修改铁律追加修订说明。
>
> **裁决结果（2026-09-21，Step 1）**：下列 6 项已由裁判裁决，原文保留作为历史记录；
> 结论见 `FROZEN.md` 顶部「修订说明（Step 1 · 裁判裁决 1–6）」与 1.4 裁决落地表。
> 1 → D-3 项目引用起步；2 → D-9 `D:\Program Files\ZIV.AI`；3 → D-7 Step 1 仅 SGLang；
> 4 → 沿用 D-15，端口动态分配并写入 `settings.ini` 的 `backend.port`；5 → `SPEC.md` §3.9；
> 6 → `SPEC.md` §3.9。

1. **共享库引用方式**（D-3）：`ZIV.Core` / `ZIV.Imaging` 最终用**项目引用**还是 **NuGet 包**？
   - 项目引用：开发期同机方便，但要求 ZIV 仓库与 ZIV.AI 仓库相邻且版本联动
   - NuGet：分发干净、版本明确，但需要 ZIV 侧产出包与版本策略
2. **发布目录**（D-9）：是否沿用 ZIV 的 `D:\Program Files\ZIV` 模式，改为
   `D:\Program Files\ZIV.AI`？还是用其他固定目录？
3. **Python 后端选型**（D-7）：LightX2V 与 SGLang 是否都纳入 Step 1 骨架，
   还是先做单一主路线？
4. **单实例与端口分配**：固定端口还是动态端口？固定端口便于 ZIV 调用，
   动态端口避免冲突——需要权衡。
5. **输出目录策略**：默认输出到主图同目录，还是程序目录 `output/`？
6. **遮罩画布坐标约定**：以主图原始像素坐标为准（推荐），还是以显示坐标为准？

---

## 修订说明（2026-09-25，独立审查 R-3）

> 本节记录一轮文档同步（对应独立审查报告的 A 类问题 R-3）。**只增不改**：不改历史 Step 段，
> 仅修正当前状态描述并在此登记。

1. **项目数**：7 → **8**，新增 `ZivAiEditor.Imaging`（§2 分层图 / §3 归属表 / §7 模块树 / §4 规则 9）。
2. **依赖描述**：`ZivAiEditor.UI` 当前**只引用 `Contracts`**（比 §4 旧措辞更严；视图在 `App`）。
3. **传输实现**：`HttpInferenceClient` → `IpcInferenceClient`（Named Pipe，D-8；§2 / §3 / §5.1 / §7 / §8 / §12.4）。
4. **编排分层澄清**：**步级编排在 `Agent`（Planner / Executor），流程级编排在 `App/Flows`**
   （`FlowRunner`；§2 核心分工）。
5. **B 类遗留登记**：Z-007（UI 正名 / 拆视图）、Z-008（UI 层 / MainWindow 重构），见
   `FROZEN.md`「妥协/挂账清单」。

## 插件层定位（batch 1）（2026-09-28，只增）

- **定位**：可选「可执行插件」是数据驱动的外置模块（`Template/plugins.json` 登记 + `<dir>` 放置），
  与既有 TE-Speed / WD14「外置模块」同语义，但由统一 `plugins.loader` 加载（唯一包名 import-by-file），
  开关经 `settings.ini [plugins]` → env `ZIV_AI_PLUGIN_<ID>`。
- **分层**：Python 加载器在被推理后端进程内（`python/server/plugins/`，纯 CPU）；C# 侧
  `PluginRegistry` 在 **Backend** 层（机制），设置窗口在 **App** 层（视图）；契约不新增。
- **边界**：批 1 只做「发现 / 加载 / 依赖探测 / 开关」，capability 调度（姿态转换等）留批 3；
  Python 侧姿态图注入沿用 `additional_images`，不改 IPC。
- **不做**：不改既有分层依赖方向；无新 NuGet。

## 插件 capability 边界（batch 3）（2026-09-29，只增）

- **定位**：批 3 落地**通用 capability 调度**（`python/server/plugins/dispatch.py`，纯 CPU）：
  启用插件导出 `PLUGIN_META` + 同名可调用 capability（首个 `sampling_plan`），调度器遍历启用插件、
  取首个非 None 结果，异常隔离（永不失败任务）。
- **分层**：Python 契约（`PLUGIN_META` / context / 返回 dict）与调度在**后端进程内**
  （`python/server/plugins/` + `pipeline._run_once` 消费点）；C# 仍只管注册表与开关
  （`PluginRegistry` / `settings.ini [plugins]` → env），**契约不新增**。
- **边界**：插件自带算力（LoRA 权重 / 调度）与降级策略；主图 / 掩码 / op 由 context 只读传入，
  插件**不改 IPC、不改命令集、不改既有分层依赖方向**。默认关，关闭时**路由 / 参数与改动前一致**
  （未做逐字节输出对比）。
- **不做**：无新 NuGet；不新增 CLI / 公开签名。

## 姿态转换废弃（2026-09-29，只增）

- 本文件「插件层定位（batch 1）」段中「capability 调度（**姿态转换等**）留批 3」的举例，
  **姿态转换已废弃**（DEMO 阶段效果不佳，不再实施）——完整声明见 `DOC/DEVLOG.md` 尾部同名段，以该段为准。
- 仓库内**无** SDPose / BodyRatioMapper 代码（仅测试夹具把 `pose-map` / `sdpose.ood` 当 env 名示例）。
- **保留**：插件 capability 机制本身（首个 capability = `sampling_plan`，边界见本文件「插件 capability 边界（batch 3）」段）。
