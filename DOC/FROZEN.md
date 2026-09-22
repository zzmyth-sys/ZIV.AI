# ZIV.AI 冻结记录（FROZEN）

- 状态：随项目推进追加
- 用途：记录每个 Step 冻结的接口，防止未授权的改动
- 规则：只增不改；修改已有行必须先向用户说明原因并获授权

> ## 修改铁律
>
> 本文件**只增不改**。
>
> - 追加新行：允许
> - 修改已有行：**必须**先向用户说明原因并获授权
> - 删除已有行：**必须**先向用户说明原因并获授权
>
> 发现已有行有误时，不要改，单独报告给用户。

> **修订说明（2026-09-21，Step 1 · 裁判裁决 1–6）**
>
> 本段记录 Step 1 启动时裁判对 Step 0「遗留项 / 待裁判裁决」六项的裁决，以及对 0.4
> 示例路径的更正。以下内容**不改动 Step 0 已冻结行**，仅在此说明并落到对应文档。
>
> 1. **共享库引用方式**（原 D-3）：**采用项目引用（`ProjectReference`）起步**，NuGet 化留待
>    分发期再评估；仓库内仍只有一份 `ZIV.Core` / `ZIV.Imaging` 源码（Z26 不变）。
> 2. **发布目录**（原 D-9）：固定目录定为 **`D:\Program Files\ZIV.AI`**（可用环境变量
>    `ZIV_AI_PUBLISH_DIR` 覆盖），与 ZIV 的 Z16 思路一致。
> 3. **Python 后端选型**（原 D-7）：**Step 1 只支持 SGLang 路线**；LightX2V 作为
>    **Step 7 可选加速**再引入，接口仍统一在 OpenAPI 契约后。
> 4. **单实例与端口**（原遗留项 4）：沿用 D-15（单实例 + 本地回环 HTTP）；
>    端口**动态分配**——由 Python 后端启动时选择空闲端口，写入**程序目录** `settings.ini`
>    的 `backend.port` 键，C# 侧读取该值再访问后端（固定端口易冲突，故不固定）。
>    OpenAPI `servers` 仍写 `http://127.0.0.1:{port}`，`port` 运行时由 `settings.ini` 提供。
> 5. **输出目录策略**（原遗留项 5）：默认输出到**主图同目录**并追加 `_ai_{timestamp}` 后缀；
>    批量场景输出到**程序目录 `output/`**（见 `SPEC.md` §3.9）。
> 6. **遮罩画布坐标约定**（原遗留项 6）：内部一律以**主图原始像素坐标**为准；显示变换只在
>    UI 内做，导出时映射回原始像素（见 `SPEC.md` §3.9）。
>
> **ProjectReference 路径更正**：0.4 示例 `..\..\ZIV\src\ZIV.Core\ZIV.Core.csproj` 相对层级
> 有误（该示例假设 ZIV.AI 项目位于 `src\` 下一级）。实际项目位于 `src\ZivAiEditor.<X>\`，
> 距 `D:\devlop\` 为**三级**，正确路径为
> `..\..\..\ZIV\src\ZIV.Core\ZIV.Core.csproj` 与
> `..\..\..\ZIV\src\ZIV.Imaging\ZIV.Imaging.csproj`。以本更正为准，0.4 示例路径作废。

> **修订说明（2026-09-21，Step 2 · 后端转向 ComfyUI + IPC）**：Python 后端由 SGLang 改为
> ComfyUI v0.37.0 in-process 管线；跨进程契约新增 `contracts/ipc-protocol.md`（IPC 传输），
> `openapi.yaml` 降级为 Schema 参考。依据 `_test_step2/REPORT.md`。详见本文件末尾「## Step 2」段。

## 冻结规则

- 冻结项不可删改；确需修改，就地更新并在本表上方追加「修订说明」段落
- 修订说明必须包含：改了什么、为什么、影响哪些接口
- 内部实现（`private` 方法、数据结构、锁、日志）不冻结
- 每次 Step 结束追加新冻结项，旧项保持不动
- 如果是 Step N（N>0）新增的接口，另起 Step N 段，不改旧段
- ZIV.AI 的契约凡**引用** `ZIV.Core` / `ZIV.Imaging` 的类型（如 `SKImageRef`），
  以 ZIV 内签名为准，**不得在 ZIV.AI 重新定义**（Z26）
- 铁律编号自 ZIV 的 Z16 之后**续接**：Z17 起归 ZIV.AI；Z1–Z16 为继承约束，原文以
  `D:\devlop\ZIV\DOC\SPEC.md` §6 为准

---

## Step 0（日期：2026-09-21）

> **本段为 Step 0 新增冻结**。目标：建立 ZIV.AI 文档体系，冻结定位、铁律（Z17–Z28）、
> 项目结构、核心契约（签名草案）与共享库引用方式。**本步不写代码**，不改 ZIV / ImageGlass。

### 0.1 铁律（冻结）

| 编号 | 铁律 | 冻结于 | 备注 |
|---|---|---|---|
| Z17 | AI 推理进程隔离：C# 不加载 Python 运行时、不 P/Invoke Python C API，只经本地 HTTP 或命名管道 | Step 0 | C# 侧唯一入口 `IInferenceClient` |
| Z18 | GPU 资源串行：所有推理请求进单一队列，4080 16GB 不并发；交互优先；OOM 降级重试 | Step 0 | `ExecutionQueue`（Agent） |
| Z19 | 遮罩二值且不预填充：遮罩 PNG 只含 0 / 255；原图与遮罩分开传；禁止 C# 侧预填充 | Step 0 | `MaskSpec.IsBinary = true` |
| Z20 | 任务状态持久化：任务 / 步骤 / 参数 / 路径 / 耗时入 SQLite；可恢复、可取消 | Step 0 | `SqliteTaskStore`（App） |
| Z21 | 模型空闲卸载：空闲超时释放显存；保留最近一个；超时可配置 | Step 0 | 后端负责，C# 配置（`BackendOptions`） |
| Z22 | Planner 可降级：LLM 解析失败回退默认计划；Planner 可替换 | Step 0 | `LlmPlanner` + `FallbackPlanner` |
| Z23 | AI 模块独立更新：AI 模块可单独更新；Python 后端可单独升级 | Step 0 | 经 OpenAPI 契约解耦 |
| Z24 | 不破坏原图：输出到新文件；中间结果保留；可回溯 | Step 0 | `ToolResult.OutputImagePath` |
| Z25 | AI 模块独立解决方案：独立 `ZIV.AI.sln`；不进 `ZIV.sln`；不共享构建产物 | Step 0 | — |
| Z26 | 共享库不复制：`ZIV.Core` / `ZIV.Imaging` 仓库中只有一份；通过项目引用或 NuGet 引用 | Step 0 | 见 0.4 共享库引用声明 |
| Z27 | 进程隔离不破：AI Editor 与 ZIV 是独立进程；ZIV 只经命令行 / URL 协议 / 命名管道调用 | Step 0 | — |
| Z28 | 独立运行不依赖 ZIV：`ZivAiEditor.App.exe` 启动不要求 ZIV 存在或运行 | Step 0 | — |

> **继承约束**：ZIV 的 **Z1–Z16** 继续适用（无静态中枢 / 单向依赖 / 契约纯净 / 平台隔离 /
> 编解码可插拔 / 查看器不碰 IO / 设置唯一入口 / 无上帝模块 / 资源释放 / 批量解耦 /
> 异步取消 / 缓存有界 / 抽离不发明 / 仅便携版 / 逻辑搬耦合切 / 固定发布目录）。
> 原文以 `D:\devlop\ZIV\DOC\SPEC.md` §6 为准，**引用而非复制**；ZIV.AI 语境下的适用说明见
> `SPEC.md` §6.1。

### 0.2 项目结构（冻结）

| 项目 | 职责 | 依赖 |
|---|---|---|
| `ZivAiEditor.Contracts` | 契约与模型（5 接口 + 6 模型），不依赖 Avalonia / 平台 | `ZIV.Core`（+ BCL） |
| `ZivAiEditor.Agent` | Planner / Executor 编排、串行队列 | `ZivAiEditor.Contracts` |
| `ZivAiEditor.Tools` | `IEditTool` 实现 + `ToolRegistry` | `ZivAiEditor.Contracts` |
| `ZivAiEditor.Backend` | `IInferenceClient` 实现（HTTP）+ Python 进程管理 | `ZivAiEditor.Contracts` |
| `ZivAiEditor.UI` | 主界面、`MaskCanvas`、任务卡片流、模板面板 | `ZivAiEditor.Contracts`（+ Avalonia） |
| `ZivAiEditor.App` | 装配 + 平台 + 对外接口（CLI / URL / HTTP）+ 存储 | 全部 |
| （外部）`ZIV.Core` | 共享契约与值对象（`SKImageRef` 等） | 无（仅 BCL + SkiaSharp） |
| （外部）`ZIV.Imaging` | 共享编解码 / 变换 / 保存 | `ZIV.Core` |
| （外部）Python 推理后端 | Qwen-Image-2.1 + LightX2V / SGLang，独立进程 | 不属于 `ZIV.AI.sln` |

**依赖方向（冻结）**：
`ZivAiEditor.App → ZivAiEditor.UI → ZivAiEditor.Agent / Tools / Backend → ZivAiEditor.Contracts → ZIV.Core / ZIV.Imaging`。

- `UI` **编译期不引用** `Agent` / `Tools` / `Backend`，只依赖 `Contracts` 的接口
  （含 `IInferenceClient`）
- `Agent` / `Tools` / `Backend` 三者**禁止互相引用**，协作经 `Contracts`，由 `App` 装配
- 禁止反向 / 循环依赖、同层互相引用

### 0.3 核心契约（冻结 · 签名草案）

> 以下为 **Step 0 签名草案**，Step 1 可补充但不破坏既有成员；任何修改走修订说明。

#### 0.3.1 接口（5）

| 接口 | 文件 | 已冻结签名（草案） |
|---|---|---|
| `IInferenceClient` | `ZivAiEditor.Contracts/Inference/IInferenceClient.cs` | `: IDisposable`；`Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)`；`Task<InferenceTaskHandle> SubmitInpaintAsync(InpaintRequest, IProgress<InferenceProgress>?, CancellationToken ct = default)`；`Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default)`；`Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)` |
| `IEditTool` | `ZivAiEditor.Contracts/Tools/IEditTool.cs` | `string Name { get; }`；`string Description { get; }`；`IReadOnlyList<string> Capabilities { get; }`；`bool CanHandle(EditStep step)`；`Task<ToolResult> ExecuteAsync(ToolInput input, IProgress<StepProgress>?, CancellationToken ct = default)` |
| `IPlanner` | `ZivAiEditor.Contracts/Planning/IPlanner.cs` | `Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default)` |
| `IExecutor` | `ZivAiEditor.Contracts/Execution/IExecutor.cs` | `Task<TaskState> ExecuteAsync(EditPlan plan, IProgress<TaskProgress>?, CancellationToken ct = default)`；`Task<TaskState> RerunAsync(string taskId, IProgress<TaskProgress>?, CancellationToken ct = default)`；`Task<bool> CancelAsync(string taskId, CancellationToken ct = default)` |
| `IToolRegistry` | `ZivAiEditor.Contracts/Tools/IToolRegistry.cs` | `void Register(IEditTool tool)`；`bool Unregister(string toolName)`；`IEditTool? Get(string toolName)`；`IReadOnlyList<IEditTool> All { get; }` |

```csharp
// ZivAiEditor.Contracts/Inference/IInferenceClient.cs
public interface IInferenceClient : IDisposable
{
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default);
    Task<InferenceTaskHandle> SubmitInpaintAsync(
        InpaintRequest request,
        IProgress<InferenceProgress>? progress = null,
        CancellationToken ct = default);
    Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default);
    Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default);
}

// ZivAiEditor.Contracts/Tools/IEditTool.cs
public interface IEditTool
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<string> Capabilities { get; }
    bool CanHandle(EditStep step);
    Task<ToolResult> ExecuteAsync(
        ToolInput input,
        IProgress<StepProgress>? progress = null,
        CancellationToken ct = default);
}

// ZivAiEditor.Contracts/Planning/IPlanner.cs
public interface IPlanner
{
    Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default);
}

// ZivAiEditor.Contracts/Execution/IExecutor.cs
public interface IExecutor
{
    Task<TaskState> ExecuteAsync(
        EditPlan plan,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);
    Task<TaskState> RerunAsync(
        string taskId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);
    Task<bool> CancelAsync(string taskId, CancellationToken ct = default);
}

// ZivAiEditor.Contracts/Tools/IToolRegistry.cs
public interface IToolRegistry
{
    void Register(IEditTool tool);
    bool Unregister(string toolName);
    IEditTool? Get(string toolName);
    IReadOnlyList<IEditTool> All { get; }
}
```

#### 0.3.2 模型（6 + 辅助）

| 模型 | 文件 | 已冻结字段（草案） |
|---|---|---|
| `EditPlan` | `ZivAiEditor.Contracts/Planning/EditPlan.cs` | `string PlanId`；`string SourcePrompt`；`string MainImagePath`；`string? ReferenceImagePath`；`MaskSpec? Mask`；`IReadOnlyList<EditStep> Steps`；`DateTimeOffset CreatedAt` |
| `EditStep` | `ZivAiEditor.Contracts/Planning/EditStep.cs` | `string StepId`；`int Order`；`string ToolName`；`IReadOnlyDictionary<string,string> Parameters`；`IReadOnlyList<string> DependsOn`；`StepStatus Status`；`string? ErrorMessage` |
| `ToolInput` | `ZivAiEditor.Contracts/Tools/ToolInput.cs` | `string StepId`；`string MainImagePath`；`string? ReferenceImagePath`；`MaskSpec? Mask`；`IReadOnlyDictionary<string,string> Parameters`；`string WorkingDirectory` |
| `ToolResult` | `ZivAiEditor.Contracts/Tools/ToolResult.cs` | `string StepId`；`bool Success`；`string? OutputImagePath`；`string? ErrorMessage`；`TimeSpan Duration`；`IReadOnlyDictionary<string,string> Metadata` |
| `MaskSpec` | `ZivAiEditor.Contracts/Imaging/MaskSpec.cs` | `string MaskImagePath`；`int Width`；`int Height`；`bool IsBinary = true`；`bool Invert` |
| `TaskState` | `ZivAiEditor.Contracts/Execution/TaskState.cs` | `string TaskId`；`TaskStatus Status`；`EditPlan Plan`；`IReadOnlyList<StepState> StepStates`；`DateTimeOffset CreatedAt`；`DateTimeOffset? StartedAt`；`DateTimeOffset? FinishedAt`；`string? OutputImagePath`；`string? ErrorMessage` |

```csharp
// 6 个核心模型（签名草案）
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

public sealed class EditStep
{
    public string StepId { get; init; } = Guid.NewGuid().ToString("N");
    public int Order { get; init; }
    public string ToolName { get; init; } = "";
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? ErrorMessage { get; set; }
}

public sealed class ToolInput
{
    public string StepId { get; init; } = "";
    public string MainImagePath { get; init; } = "";
    public string? ReferenceImagePath { get; init; }
    public MaskSpec? Mask { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();
    public string WorkingDirectory { get; init; } = "";
}

public sealed class ToolResult
{
    public string StepId { get; init; } = "";
    public bool Success { get; init; }
    public string? OutputImagePath { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

public sealed class MaskSpec
{
    public string MaskImagePath { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsBinary { get; init; } = true;
    public bool Invert { get; init; }
}

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

**辅助类型（草案，Step 1 冻结）**

| 类型 | 用途 |
|---|---|
| `PlanRequest` | 三图路径 + 提示词 + 选项（`IPlanner.PlanAsync` 输入） |
| `InpaintRequest` | 单次推理请求，映射 OpenAPI `/v1/inpaint` |
| `InferenceTaskHandle` | 提交返回的 `taskId` + 初始状态 |
| `InferenceTask` | 后端任务快照，映射 `/v1/task/{id}` |
| `HealthStatus` | 后端健康与模型加载状态，映射 `/v1/health` |
| `StepState` | 单步运行记录（状态 / 耗时 / 输出路径） |
| `StepProgress` / `TaskProgress` / `InferenceProgress` | 三级进度（`IProgress<T>`） |
| `StepStatus` / `TaskStatus` | 步骤 / 任务状态枚举 |

#### 0.3.3 跨进程契约

| 文件 | 内容 | 状态 |
|---|---|---|
| `contracts/openapi.yaml` | OpenAPI 3.1：`/v1/health`、`/v1/inpaint`、`/v1/task/{id}` | **草案**，Step 1 冻结 |

### 0.4 共享库引用方式声明（冻结）

> **铁律 Z26**：`ZIV.Core` / `ZIV.Imaging` 在仓库中只有一份，禁止 fork / 拷贝源码副本。

- **声明**：ZIV.AI 通过**项目引用（`ProjectReference`）或 NuGet 包引用（`PackageReference`）**
  使用 `ZIV.Core` / `ZIV.Imaging`，二者**择一**，不混用。
- **Step 0 现状**：**未定稿**。两种方式权衡如下，最终由裁判裁决（见 `ARCHITECTURE.md`
  「遗留项」第 1 项）：
  - **项目引用**：`ZivAiEditor.Contracts` / `ZivAiEditor.Tools` 等以
    `..\..\ZIV\src\ZIV.Core\ZIV.Core.csproj` 形式引用；开发期同机方便，但要求两仓库相邻、
    版本联动，且 `ZIV.AI.sln` 的构建会连带构建 ZIV 项目（与「不共享构建产物」Z25 需协调）。
  - **NuGet 引用**：由 ZIV 侧产出 `ZIV.Core` / `ZIV.Imaging` 包并指定版本；分发干净、
    版本明确，但需要 ZIV 侧建立打包与版本策略。
- **约束**：无论哪种方式，`ZIV.AI` 都**不得修改** `ZIV.Core` / `ZIV.Imaging` 的源码或
  契约（Z26）；若需要新成员，在 ZIV 侧走 ZIV 的修改流程，再由 ZIV.AI 引用新版本。
- **共享类型引用而非复制**：例如 `SKImageRef` 是 `ZIV.Core` 的类型，ZIV.AI 直接引用，
  **不得在 `ZivAiEditor.Contracts` 重新定义**；凡文档 / 契约中出现的共享类型，一律指向
  `ZIV.Core` / `ZIV.Imaging`。

> **修订说明**：任何对 Step 0 冻结项的修改，须追加修订说明段落并获用户授权。
> Step 0 为初始冻结，暂无修订。

---

## Step 1（日期：2026-09-21）

> **本段为 Step 1 新增冻结**。目标：建立 `ZIV.AI.sln` + 七项目骨架 + `Contracts` 契约落地，
> 并把 OpenAPI 契约冻结提前到本步；不改 ZIV / ImageGlass，不复制共享库源码。

### 1.1 项目结构（冻结）

| 项目 | TFM | 输出 | 依赖（ProjectReference） |
|---|---|---|---|
| `ZivAiEditor.Contracts` | net8.0 | 类库 | `ZIV.Core` |
| `ZivAiEditor.Backend` | net8.0 | 类库 | `Contracts` |
| `ZivAiEditor.Agent` | net8.0 | 类库 | `Contracts` |
| `ZivAiEditor.Tools` | net8.0 | 类库 | `Contracts` |
| `ZivAiEditor.UI` | net8.0 | 类库 | `Contracts`、`Agent`、`Tools`、`Backend`、`ZIV.Core`、`ZIV.Imaging`（+ Avalonia / Avalonia.Skia / SkiaSharp） |
| `ZivAiEditor.App` | net8.0-windows | WinExe | `UI`、`Backend`、`Agent`、`Tools`、`Contracts`、`ZIV.Core`、`ZIV.Imaging`（+ Avalonia.Desktop / Avalonia.Themes.Fluent） |
| `ZivAiEditor.Tests` | net8.0 | 测试 | `Contracts`、`Agent`、`Tools`（+ xunit / Microsoft.NET.Test.Sdk） |

**依赖方向（冻结，Step 1 版）**：
`App → UI → Agent / Tools / Backend → Contracts → ZIV.Core / ZIV.Imaging`。

- `UI` **编译期可引用** `Agent` / `Tools` / `Backend` 程序集（便于 `App` 装配与类型贯通），
  但**代码中只允许使用 `Contracts` 的接口**，不得直接调用其实现类；此点取代 Step 0 0.2 中
  「UI 编译期不引用 Agent / Tools / Backend」的措辞（Step 0 原行不改，以本段为准）
- `Agent` / `Tools` / `Backend` 三者仍**禁止互相引用**
- Step 1 **不引用** `ZIV.Viewer` / `ZIV.Gallery` / `ZIV.Batch` / `ZIV.App`
- 禁止反向 / 循环依赖、同层互相引用

### 1.2 核心契约冻结确认

Step 0 0.3 的 **5 个接口 + 6 个模型 + 辅助类型**已在 `ZivAiEditor.Contracts` 落地，
**签名与 Step 0 完全一致**，无新增、无删改：

- 接口：`IInferenceClient` / `IEditTool` / `IPlanner` / `IExecutor` / `IToolRegistry`
- 模型：`EditPlan` / `EditStep` / `ToolInput` / `ToolResult` / `MaskSpec` / `TaskState`（类）
- 枚举：`TaskStatus` / `StepStatus`
- 辅助：`PlanRequest` / `InpaintRequest` / `InferenceTaskHandle` / `InferenceTask` /
  `HealthStatus`（含 `ModelStatus`）/ `InferenceProgress` / `StepProgress` /
  `StepState` / `TaskProgress`

> **澄清（不改 0.3）**：`TaskState` 是**类**，`TaskStatus` 是**枚举**；Step 1 启动文本曾
> 把 `TaskState` 误称为枚举，以 0.3 冻结为准。

### 1.3 共享库引用方式定稿（Step 1）

- 采用**项目引用**：`ZivAiEditor.Contracts` / `ZivAiEditor.UI` / `ZivAiEditor.App` 以
  `..\..\..\ZIV\src\ZIV.Core\ZIV.Core.csproj`、`..\..\..\ZIV\src\ZIV.Imaging\ZIV.Imaging.csproj`
  引用共享库（相对层级见上方修订说明）。
- 不复制源码；`ZIV.Core` / `ZIV.Imaging` 的 NuGet 化留待分发期再评估。
- 已知副作用：经 `ZIV.AI.sln` 构建时，ZIV 项目不在该 sln 内，MSBuild 对非 sln 的
  `ProjectReference` 会回退到默认配置（Debug），故 `bin` / `obj` 会写入 ZIV 目录；
  直接构建单个 `ZivAiEditor.*` 项目则按 Release 构建 ZIV。记录为已知现象，不违反 Z25
  （各自解决方案与发布产物仍独立）。

### 1.4 裁决落地表（裁决 1–6）

| 裁决 | 内容摘要 | 落地点 |
|---|---|---|
| 1 | 共享库用项目引用起步 | 本文件 1.3；`ARCHITECTURE.md` D-3 |
| 2 | 发布目录 `D:\Program Files\ZIV.AI` | `ARCHITECTURE.md` D-9；`publish.ps1` |
| 3 | Step 1 只支持 SGLang，LightX2V 为 Step 7 可选加速 | `ARCHITECTURE.md` D-7 |
| 4 | 单实例 + 回环 HTTP 沿用 D-15；端口**动态分配**，写入程序目录 `settings.ini` 的 `backend.port`，C# 侧读取 | `contracts/openapi.yaml` servers；`SPEC.md` §3.9 |
| 5 | 输出默认主图同目录 + `_ai_{timestamp}`；批量到程序目录 `output/` | `SPEC.md` §3.9 |
| 6 | 遮罩内部用主图原始像素坐标，显示变换只在 UI | `SPEC.md` §3.9 |

### 1.5 跨进程契约冻结（OpenAPI 3.1）

`contracts/openapi.yaml` 本步冻结为 **8 个端点**（字段 snake_case）：

`/v1/health`、`/v1/inpaint`、`/v1/img2img`、`/v1/upscale`、`/v1/segment`、
`/v1/outpaint`、`/v1/task/{id}`、`/v1/task/{id}/cancel`。

- 所有编辑类 POST 共享 `ImageEditRequest`（含 `image_path` / `mask_path` / `prompt` /
  `steps` / `seed` / `denoise` / `output_path`），统一返回 `TaskAccepted`（`task_id`）。
- 任务查询返回 `TaskStatusResponse`（`state` / `progress` / `output_path` / `error`），
  状态枚举命名为 `TaskRunState`，避免与 C# 的 `TaskState` 类混淆。
- 契约冻结提前于 Step 0 规划的 Step 2；Step 2 调整为「生成 DTO / 客户端」等后续工作。

> **遗留（Step 4）**：`IInferenceClient` 当前只有 `SubmitInpaintAsync` 一个提交入口，
> 而 OpenAPI 已冻结 6 个编辑端点（inpaint / img2img / upscale / segment / outpaint 等）。
> Step 4 实现 `HttpInferenceClient` 时需扩展提交入口（新增方法或泛化请求），
> **扩展走修订说明，不破坏既有成员**。

### 1.6 构建环境与 SDK（Step 1 定版）

- **SDK 固定**：`global.json` 置于**仓库根** `D:\devlop\ZIV.AI\global.json`，固定
  **SDK 10.0.401**（`rollForward latestFeature`）。放仓库根是为了让任意工作目录
  （仓库根 / `src`）都应用同一 SDK 钉版。
- **原因（重要）**：Avalonia 12.1.1 的 XAML 源生成器需要 **Roslyn 4.14（.NET SDK 10）**。
  在 SDK 8.0.203 下，`MainWindow.axaml.cs` 会报
  `CS0103: 名称"InitializeComponent"不存在`（生成器未运行），并伴随 `CS9057` 分析器版本警告。
  故 ZIV.AI **不能**沿用 ZIV 的 8.0.203；ZIV 自身的 `global.json` 未改动。
- **ZivAiEditor 项目**：`net8.0` / `net8.0-windows`，与 SDK 10 兼容（SDK 只影响工具链，不影响 TFM）。
- 影响：`global.json`；`ARCHITECTURE.md` §7；`ACCEPTANCE.MD` 1.4；`DEVLOG.md` 问题 5。

---

## Step 2（日期：2026-09-21）

> **修订说明（Step 2 · 后端转向 ComfyUI + IPC）**
>
> 本段记录 Step 2 启动时因后端选型变更而对 Step 0 / Step 1 冻结项的修订。
> 依据：`_test_step2/REPORT.md` 实测报告（11 项任务全部执行完毕）。
> 以下内容**不改动 Step 0 / Step 1 已冻结行**，仅在此说明并落到对应文档。
>
> 1. **D-7 修订**：Python 后端从「Step 1 只支持 SGLang」修订为
>    「采用 ComfyUI v0.37.0 便携版源码，**in-process 直接调管线**（不启动 HTTP server）；
>    SGLang 路线废弃；LightX2V / Lightning LoRA 作为 Step 7 可选加速再引入」。
>    依据：实测确认 ComfyUI v0.37.0 原生支持 Qwen-Image-2.1（`QwenImage21Transformer2DModel`、
>    `TextEncodeQwenImage21` 节点、`CLIPType.QWEN_IMAGE`），模型三件套可加载，
>    端到端编辑闭环可跑通（512² 约 16.1s）。
>
> 2. **D-8 修订**：`contracts/openapi.yaml` 的 8 端点**降级为 Schema 参考**（保留定义，
>    不再作为跨进程传输契约）；新增 `contracts/ipc-protocol.md` 作为 **IPC 传输契约**
>    （Step 2 冻结）。OpenAPI 中的 `ImageEditRequest` / `TaskAccepted` / `TaskStatusResponse`
>    等 schema 映射为 IPC 消息的 payload 结构。
>    依据：实测确认 Named Pipe + 长度前缀协议性能充足（7.91MB 数据 2.71ms，2918 MB/s），
>    且 IPC 无需 HTTP 栈开销。
>
> 3. **0.3.3 跨进程契约修订**：从「只有 `openapi.yaml`」修订为
>    「`openapi.yaml`（Schema 参考）+ `ipc-protocol.md`（IPC 传输契约，Step 2 冻结）」。
>
> 4. **1.5 跨进程契约冻结修订**：Step 1 冻结的 8 个 OpenAPI 端点**保留定义不变**，
>    但状态从「跨进程唯一契约」调整为「Schema 参考」；IPC 协议独立冻结于 Step 2 段。
>
> 5. **Z23 补充**：原文「经 OpenAPI 契约解耦」补充为「经 OpenAPI（Schema 参考）
>    或 **IPC 协议**解耦」；Z23 的核心约束（AI 模块与 Python 后端可各自升级）不变。
>
> 6. **管道方向与消息类型收敛（Step 2.1 实测后修订）**：
>    - 管道方向从「Python = server，C# = client」修订为「C# = Server，Python = Client」。
>      理由：C# 的 PythonProcessManager 掌控生命周期，启动时序无竞态。
>    - 消息类型收敛为 ping / pong，删除 health / health_result。
>      理由：ping/pong 是 IPC 惯例，pong 的 payload 已承载健康状态字段。
>    - preview 帧明确为 0x02 二进制帧，承载 JPEG 字节流，
>      来源为 ComfyUI 的 latent_preview.get_previewer()。
>    - 上述修订仅涉及 contracts/ipc-protocol.md，不改动 Step 0 / Step 1 冻结行。
> 7. **progress 帧扩展与 vram 口径（Step 2.2 修订）**：
>    - `progress` 新增**可选字段** `stage`（`loading_model` / `sampling` / `vae_decode`）
>      与 `sub_stage`（`stage="loading_model"` 时为 `dit` / `te` / `vae`；`stage="sampling"`
>      时为 `ready` 或 null）；新增 §3.3「加载阶段进度序列」记录实测 7 帧。
>      属**向后兼容的协议扩展**（新字段可选，旧端忽略），不改变既有消息语义。
>    - `pong.vram_used_mb` 口径明确为 **NVML 当前 GPU 占用**；并记录 ComfyUI
>      **延迟加载权重**（load 后未推理前接近基线属正常）。
>    - `ipc_version` 由 **`0.2` 升至 `0.3`**（§7 记录变更点）。
>    - 依据 Step 2.2 复测：`load_models_gpu()` 前 `torch.cuda.memory_allocated() == 0`、
>      DiT 参数 device 为 `cpu`；调用后约 **6920 MB**、device 为 `cuda:0`。
>    - 上述修订仅涉及 contracts/ipc-protocol.md，不改动 Step 0 / Step 1 冻结行。
>
> **Z17 不改**：原文「只经本地 HTTP 或命名管道」已覆盖 Named Pipe，本次后端转向
> **未突破 Z17**（C# 不加载 Python 运行时、不 P/Invoke Python C API）。
>
> **实测确认的关键事实（写入冻结记录）**：
> - ComfyUI v0.37.0 **不存在官方 Embedding API**（无 `comfy/client/`、
>   无 `embedded_comfy_client.py`）—— 必须自封装推理进程。
> - `InterruptProcessingException` 继承 **`BaseException`**（非 `Exception`）——
>   Python 侧取消捕获必须用 `except BaseException` 或指定异常类型。
> - **共享内存不比 Named Pipe 快**（实测 7.91MB：Named Pipe 2.71ms vs 共享内存 5.37ms）——
>   不引入共享内存。
> - **Named Pipe 默认 ACL 允许 Everyone / Anonymous 读** ——
>   必须显式收紧为**仅当前用户**（`PipeSecurity`）。
> - **Python 3.13 free-threading 不可用**（`_is_gil_enabled() == True`，无 `python3.13t.exe`）——
>   不启用 free-threading。
> - **pywin32 未安装** —— Python 侧同步使用 `ctypes`（已验证可行）。
> - **无显存泄漏**：3 次加载/卸载循环后显存稳定；进程退出后回到基线。
> - **模型路径绝对直传**：无需 `extra_model_paths.yaml`，不触碰 `C:\AI\ComfyUI_PIC`。

### 2.1 后端选型（冻结）

- **后端 = ComfyUI v0.37.0 便携版**（`D:\devlop\ZIV.AI\Comfyui`），**in-process 直接
  `import comfy` 源码调管线**，不启动 HTTP server。
- **模型三件套**（绝对路径直传，只读引用 `C:\AI\ComfyUI_PIC`，不拷贝、不修改）：
  - DiT：`...\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors`
  - TE：`...\text_encoders\qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors`
  - VAE：`...\vae\qwen_image_2.1_vae_bf16.safetensors`
- **加载 API**：`comfy.sd.load_diffusion_model` / `load_clip(..., CLIPType.QWEN_IMAGE)` /
  `comfy.sd.VAE`；**采样**：`CFGGuider` + `sampler_object("euler")` +
  `nodes_flux.get_schedule`，`cfg=1.0`。

### 2.2 IPC 传输契约（冻结）

- **传输**：Windows **Named Pipe**（`PipeOptions.Asynchronous`），**仅当前用户** ACL
  （`PipeSecurity` + `SetAccessRuleProtection(true, false)` + `NamedPipeServerStreamAcl.Create`）。
- **帧格式**：`[4 字节小端长度][1 字节帧类型][载荷]`（详见 `contracts/ipc-protocol.md`）。
- **消息**：`health` / `submit` / `accepted` / `progress` / `preview` / `result` /
  `cancel` / `canceled` / `error`。
> 以修订说明第 6 条为准：`health` / `health_result` 作废，改为 `ping` / `pong`；
> 管道方向为 **C# = Server / Python = Client**。
- **payload schema** 沿用 `contracts/openapi.yaml` 的 `ImageEditRequest` / `TaskAccepted` /
  `TaskStatusResponse`（作为 Schema 参考）。
- **取消**：C# 发 `cancel` → Python 调 `interrupt_current_processing()`，捕获
  `InterruptProcessingException`（`BaseException`）→ 回 `canceled`。
- **进度**：Python 采样 `callback(step, x0, x, total)` → `progress`（含 `fraction`）；
  预览帧走独立二进制帧。

### 2.3 实测事实（依据 `_test_step2/REPORT.md`）

见上方修订说明的「实测确认的关键事实」。关键性能：Named Pipe 7.91MB 单程 **2.71ms**；
512² 编辑端到端约 **16.1s**；进程退出显存回基线（~813 MiB）。

### 2.4 项目结构与依赖

Step 1 冻结的 7 项目结构与依赖方向**不变**（见 1.1）。Step 2 起：
- `ZivAiEditor.Backend` 新增 `IpcInferenceClient : IInferenceClient`（Step 2 实现）；
  `HttpInferenceClient` 保留为备用。
- Python 推理进程新建于 `D:\devlop\ZIV.AI\python\`（Step 2 起）。

> **遗留**：`IInferenceClient` 目前只有 `SubmitInpaintAsync`；Step 2/4 按需扩展提交入口
> （走修订说明，不破坏既有成员）。

---

## Step 3（日期：2026-09-21）

> **修订说明（Step 3 · 重新定义：空闲卸载 + heartbeat）**
>
> 原 `ACCEPTANCE.MD` 把 Step 3 规划为「SGLang 服务」，但 SGLang 路线已随
> **D-7 修订**废弃（后端改为 ComfyUI in-process + IPC）。本步**重新定义** Step 3 为
> **空闲卸载（Z21）+ heartbeat**；不改动 Step 0/1/2 已冻结行，仅在此追加。

### 3.1 范围（冻结）

- **空闲卸载（Z21）**：模型空闲超时后释放 DiT / TE / VAE 与显存，回到 `not_loaded`；
  **进程与管道保持连接**，下次 `submit` 惰性重载。超时与检查间隔可配置
  （`config.IDLE_UNLOAD_SECONDS` / `IDLE_CHECK_INTERVAL_S`，可用环境变量覆盖以便测试）。
- **heartbeat（新增消息，IPC 契约 0.3 → 0.4）**：Python 周期发送
  `{"type":"heartbeat","vram_used_mb":<NVML>,"current_task_id":<id|null>}`；
  C# 侧超时未收（默认 30 s）触发 `HeartbeatLost` 事件。**自动重启留待 Step 4**。
- **主循环非阻塞轮询**：Python 主循环改为 `PeekNamedPipe` 轮询（不再阻塞读），
  以使 heartbeat 线程的写不被 fd 锁饿死（Step 2.4 的 fd 锁约束）。
- **关闭「智能显存优化」（等效官方 `--disable-smart-memory`）**：由
  `model_loader.prepare_environment()` 置 `comfy.cli_args.args.disable_smart_memory = True`，
  **必须在首次 `import comfy.model_management` 之前**（该模块在 import 时把该值读成模块
  常量）。依据 klein 启动器 `aimdo_init.py` 实测；本步复测采样峰值 **16020 → 9144 MiB**、
  `torch alloc` **11.4 GB → 0.7 GB**，耗时基本不变。

### 3.2 契约变更

- `contracts/ipc-protocol.md`：新增 `heartbeat` 消息（§3.2），**ipc_version 0.3 → 0.4**；
  追加修订记录与 §7 变更点。属**向后兼容扩展**（旧端忽略未知消息）。
- `IInferenceClient` / `InferenceProgress` **签名不变**；C# 侧以事件
  （`HeartbeatReceived` / `HeartbeatLost`）与 `LastHeartbeatAt` 暴露给 App 层。

### 3.3 实测事实（本步）

- 采样峰值（关闭智能显存优化后）：`nvidia-smi` **9144 MiB**、`torch alloc` **0.7 GB**
  （此前开启时为 16020 MiB / 11.4 GB）；512²/4 步单次推理 **~13.7 s**（与此前相当）。
- 空闲卸载后 `torch.cuda.memory_allocated()` 降至 **8.8 MB**；`nvidia-smi` 因 WDDM
  计账滞后 ~1.5 s，最终回落至 **~1038 MiB**。
- 卸载后重载：惰性 load ≈ **1.6–2.6 s**；下次推理首个采样步前的 `moving_to_gpu` 约
  **5–6 s**（卸载后需重新上 GPU；与 Step 2.3 的 2.51 s 热态不同）。
- heartbeat：每 10 s 一帧；`HeartbeatLost` 阈值 30 s。
- 依据：Step 3 实测（`DOC/DEVLOG.md` Step 3 段）。

### 3.4 运行时默认策略：默认关闭 smart memory

- ZIV.AI **默认关闭** ComfyUI smart memory（`disable_smart_memory = True`，等效官方
  `--disable-smart-memory`）——**与 ComfyUI 原生默认不同**。
- 实测（512²/4 步，RTX 4080 16GB）：峰值 `nvidia-smi` **16020 → 9144 MiB**、
  `torch alloc` **11.4 GB → 0.7 GB**（显存降约 **43%**），单次推理 **13.0 → 13.7 s**
  （速度损失约 **5%**）。
- 回退：环境变量 **`ZIV_AI_DISABLE_SMART_MEMORY=0`**（或 `false`）即恢复原生行为。
- 顺序约束：必须在首次 `import comfy.model_management` **之前**设置（该模块在 import 时
  读取该值），由 `model_loader.prepare_environment()` 落地。
- 性质：**运行时策略**，不改动 Step 0 / Step 1 / Step 2 的冻结行。

> **遗留**：Python 主循环改为轮询后，`submit` 期间仍为同步执行；heartbeat 线程与
> 采样写入经 `FrameIO._write_lock` 串行。`unload_all_models()` 会卸载全部 ComfyUI
> 托管模型（当前仅本模型）。

---

## Step 4（日期：2026-09-22）

> **修订说明（Step 4 · 自动重启 + App 装配 + 优化接入预留）**
>
> 本段为 Step 4 追加。目标：① 后端自愈（`HeartbeatLost` → 自动重启）② App 层装配通路
> ③ pipeline 预留优化钩子 ④ 基线性能实测。**不改动 Step 0/1/2/3 已冻结行**，唯一契约
> 变更见下方 4.1（`InpaintRequest` 追加可选属性，走修订说明）。

### 4.1 契约修订：`InpaintRequest` 追加可选属性（冻结）

- `ZivAiEditor.Contracts/Inference/InpaintRequest.cs` 追加：
  - `LoraOptions? Lora`（默认 `null`）
  - `OptimizationOptions? Optimizations`（默认 `null`）
- 新增契约类型：`LoraOptions { string Path; double StrengthModel = 1.0; double StrengthClip = 1.0; }`、
  `OptimizationOptions { bool MagCache; double MagCacheThresh = 0.24; }`。
- **非破坏性**：既有成员与默认值不变，`IInferenceClient` 签名不变；`null` 时序列化被忽略，
  行为与 Step 3 完全一致。
- 对应 `contracts/ipc-protocol.md` `ipc_version 0.4 → 0.5`（`submit.payload` 可选字段）。

### 4.2 自动重启（冻结）

- `PythonProcessManager` 新增配置：`AutoRestartEnabled`（默认 `true`）、
  `MaxRestartAttempts`（默认 `3`）、`RestartBackoffMs`（默认 `2000`，指数退避 2s / 4s / 8s）。
- 订阅 `IpcInferenceClient.HeartbeatLost` → 触发重启；流程：标记 `Restarting` → 停止旧 Python
  （`shutdown` → 5s 超时 → `taskkill /T /F` 进程树）→ 关闭旧管道 → 退避 → **新管道名**
  `zivai.infer.{C#进程PID}.{seq}` → 拉起新 Python → `ping` 成功。
- 事件：`Restarting` / `Restarted` / `RestartFailed`；状态枚举 `PythonBackendState`
  （`Stopped` / `Running` / `Restarting` / `Failed`）。
- **计数语义**：每次 `HeartbeatLost` 触发的重启记 1 次；**成功 `submit` 后清零**；
  超过 `MaxRestartAttempts` 标记 `Failed` 且不再重试。
- 在飞 `SubmitInpaintAsync` 因管道断开以 `InferenceBackendException(code="BACKEND_RESTARTED")`
  结束（`InferenceProgressExtensions.InferenceBackendException`）。
- 性质：新增实现与配置，`IInferenceClient` 签名不变。

### 4.3 优化接入预留（冻结）

- Python 侧新增 `python/server/pipeline_hooks.py`：`register_pre_sampling_hook` /
  `apply_pre_sampling_hooks(model, clip, params) -> (model, clip)`；`pipeline.run` 在
  **模型加载后、`encode_prompt` 之前**调用（使 LoRA 对 `clip` 的修改影响文本条件编码）。
  **只预留注册点，不实现任何具体 hook**。
- `handlers.handle_submit` 每次请求先 `clear_pre_sampling_hooks()`，再按 `submit.payload`
  的可选 `lora` / `optimizations` 注册占位 hook（仅记录 intent，不加载）。
- 性质：**运行时策略 + 新文件**，不改动 Step 0/1/2/3 冻结行；IPC 契约按 4.1 走 0.5。

### 4.4 基线性能（Step 4 实测）

- 512² 编辑 / `steps=20` / smart memory 关闭 / RTX 4080 16GB：
  模型加载 **6.09 s**；稳态 `moving_to_gpu` **2.31 s**、采样 **1.64 s**、VAE decode **0.99 s**、
  总 **7.53 s**；峰值显存（torch alloc）**10995 MB**。
- 明细见 `DOC/OPTIMIZATION.md` §6「基线数据」；脚本 `_test_step2/baseline_bench.py`。

### 4.5 采样配置修正 + OOM 降级（Step 4 修正，2026-09-22）

> 本小节为 Step 4 收尾修正的**修订说明**，不改动 Step 0/1/2/3 冻结行，也不改
> `IInferenceClient` 签名与 IPC 消息类型（`ipc_version` 仍为 `0.5`）。

- **pipeline 采样配置修正**：`pipeline.py` 由 `comfy_extras.nodes_flux.get_schedule`
  （Flux 经验 mu）+ `CFGGuider` 手工采样，改为 **`ModelSamplingAuraFlow(shift=3.1)` +
  官方 `comfy.sample.sample()`**（`euler` / `simple` / `cfg=1.0`）。依据 1024 端到端实测
  （`DOC/OPTIMIZATION.md` §6.1）。`denoise` 参数改为经 `sample(denoise=...)` 生效（此前忽略）。
- **分辨率与 OOM 降级**：`config.MAX_RESOLUTION`（默认 **1024**，面积口径）为目标分辨率，
  `RESOLUTION_FALLBACK=[1024,768,640]`；`pipeline.run` 捕获 CUDA OOM 逐级降级重试。
  性质：**运行时策略**，不改契约。
- **测试断言增强**：新增画面结构断言（`src/ZivAiEditor.Tests/ImageQuality.cs`）并在
  `IpcInferenceTests` 中启用；`baseline_bench.py` 同步。
- **TE 修订**：`config.TEXT_ENCODER_PATH` 由 Qwen3.5-9B 改为 **Qwen3-VL-8B**
  （`qwen3vl_8b_int8_convrot.safetensors`），修复纯噪声输出（见 `_test_step2/diagnose/DIAGNOSIS.md`）。

---

## Step 5（日期：2026-09-22）

> **新增铁律（2026-09-22）**：
>
> **Z29 · 测试按影响面执行** — 每次代码改动后，只跑受影响的测试类；
> 全量测试仅在 Step 收尾或明确需要时执行。
>
> **核心动机：保护硬件资产。** 端到端 GPU 测试每次加载 16GB 模型、峰值显存
> 16004/16376 MiB（1024 分辨率下）；GPU 长期高负载会缩短寿命。RTX 4080 16GB
> 市价约 1 万元，硬件损耗的成本远高于多跑一次测试的收益。
>
> **原则：出错了再修。** 测试是为了发现已发生的问题，不是为了预防所有可能性。
>
> 实测教训：pipeline.py 采样路径改动后仅需跑 IpcCancelTests（约 40s），
> 无差别跑全量（约 4 分钟）会造成不必要的 GPU 高负载。
>
> 影响：ACCEPTANCE.MD 的通用验收清单 G1（代码能编译）与测试执行策略需同步更新。

### 5.1 铁律（冻结）

> 编号续接 Z28；Z17–Z28 的既有行不改（见 0.1）。

| 编号 | 铁律 | 冻结于 | 备注 |
|---|---|---|---|
| Z29 | 测试按影响面执行：每次代码改动后，只跑**受影响的测试类**；全量测试仅在 **Step 收尾**或**明确需要**时执行 | Step 5 | **核心动机：保护硬件资产**（GPU 长期高负载缩短寿命）；原则：**出错了再修**；选择依据：改动文件的引用关系 + 测试类的覆盖范围；全量触发：Step 收尾 / 发布前验证 / 跨模块重构；**禁止**无差别的「每次改动都跑全量」 |

> **理由（核心动机：保护硬件资产）**：端到端 GPU 测试每次加载 16GB 模型、跑满显存峰值
> （1024 下 16004/16376 MiB）；GPU 长期高负载缩短寿命。RTX 4080 16GB 市价约 1 万元，
> 硬件损耗成本远高于多跑一次测试的收益。**原则：出错了再修**——测试是为了发现已发生的
> 问题，不是为了预防所有可能性。实测教训：pipeline.py 采样路径改动仅需 `IpcCancelTests`
> （约 40s），全量约 4 分钟。
>
> **关联**：`SPEC.md` §6.2（Z29）、`ACCEPTANCE.MD`「测试执行原则（Z29 · 保护硬件资产）」。

### 5.2 测试选择指引（非冻结，供参考）

| 改动范围 | 建议执行 |
|---|---|
| `pipeline.py` 采样 / 归一化路径 | `IpcCancelTests`（必要时加 `IpcInferenceTests`） |
| `pipeline.py` 输出 / 结果帧 | `IpcInferenceTests` |
| `config.py` 新增配置（默认值不变） | 无测试需跑（或冒烟 `IpcSmokeTests`） |
| `PythonProcessManager` / `IpcInferenceClient` | `IpcSmokeTests` / `IpcModelLoadTests` / `IpcAutoRestartTests` |
| `handlers.py` / 心跳 / 空闲卸载 | `IpcIdleUnloadTests` |
| Backend 契约变更 | `ContractsSmokeTests` + 相关 IPC 用例 |
| Step 收尾 / 发布前 / 跨模块重构 | **全量** `dotnet test ZIV.AI.sln` |
| `LlmPlanner` / `FallbackPlanner` / `ResilientPlanner` | `PlannerTests`（无 GPU） |
| `ILlmClient` 契约 | `ContractsSmokeTests` + `PlannerTests` |

### 5.3 Planner 实现与新增契约（冻结）

> 本小节为 Step 5 的 Planner 落地追加。**不改动 Step 0 0.3 的 `IPlanner` / `EditPlan` /
> `EditStep` 签名**；唯一新增契约见 5.3.1（`ILlmClient`，走新增说明，不改既有成员）。

#### 5.3.1 新增契约 `ILlmClient`（冻结）

- **文件**：`ZivAiEditor.Contracts/Inference/ILlmClient.cs`
- **签名**：

```csharp
public interface ILlmClient : IDisposable
{
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}
```

- **定位**：`LlmPlanner` 用于生成 `EditPlan` 的纯文本补全入口；与 `IInferenceClient` 一样，
  C# **不加载** Python / LLM 运行时（Z17），实现由 App 层装配注入（Z22 可替换）。
- **性质**：**新增接口**，不修改 Step 0 / Step 1 冻结的任何签名；`IPlanner` / `EditPlan` /
  `EditStep` **原样不动**。
- **实现（Step 5 补完）**：`ZivAiEditor.Backend.LocalLlmClient`（HTTP，OpenAI 兼容
  `/v1/chat/completions`）+ `LlmClientOptions`。见 5.3.2。

#### 5.3.2 实现清单（冻结）

| 类 | 文件 | 职责 |
|---|---|---|
| `FallbackPlanner` | `ZivAiEditor.Agent/FallbackPlanner.cs` | 确定性单步兜底：主图缺失抛 `ArgumentException`，否则返回 1 步计划（有 mask → `inpaint`，无 mask → `img2img`），参数 `{prompt, steps:"25", denoise:"1.0"}`；**永不失败**（Z22） |
| `LlmPlanner` | `ZivAiEditor.Agent/LlmPlanner.cs` | 注入 `ILlmClient` + `IToolRegistry`；构建系统提示词 → 调用 → 解析 JSON 为 `EditPlan`；解析失败 / 空响应 / 超时抛 `PlannerException`；默认超时 **30 s** |
| `ResilientPlanner` | `ZivAiEditor.Agent/ResilientPlanner.cs` | Z22 降级链：`primary`（LlmPlanner）失败 → 回调 `onDegrade` → 返回 `fallback`（FallbackPlanner）；取消（`ct` 触发）不降级、直接抛 |
| `LlmClientOptions` | `ZivAiEditor.Backend/LlmClientOptions.cs` | LLM 客户端配置：`Endpoint`（默认 `http://127.0.0.1:8080/v1/chat/completions`）、`Model?`、`Timeout`（30s）、`Temperature`（0.1）、`MaxTokens`（2048）、`EnableThinking`（false） |
| `LocalLlmClient` | `ZivAiEditor.Backend/LocalLlmClient.cs` | `ILlmClient` 的 HTTP 实现：注入 `HttpClient`；OpenAI 兼容请求（`messages` / `stream=false` / `temperature` / `max_tokens` / `chat_template_kwargs.enable_thinking`）；失败去参重试 1 次（klein 经验）；HTTP 错误 / 超时 / 非法 JSON 抛 `LlmClientException` |
| `EmptyToolRegistry` | `ZivAiEditor.App/EmptyToolRegistry.cs` | **临时**空 `IToolRegistry`，仅供装配 `LlmPlanner`；Step 7 落地真实 `ToolRegistry` 后替换 |

- **降级链**：`LlmPlanner` 成功 → 用其计划；抛 `PlannerException` → `FallbackPlanner` 接管；
  `FallbackPlanner` 对合法请求永不失败。
- **`FallbackPlanner.DefaultSteps = "25"`**：与 `InpaintRequest.Steps` 默认值（FROZEN Step 6.3）
  对齐，避免两处默认值分叉。
- **`PlaceholderPlanner` 已删除**（被上述实现取代）。
- **App 装配（Step 5 补完）**：`AppContext` 构造单例 `HttpClient` + `LocalLlmClient`
  （planner options 来自 `settings.ini` 的 `[llm.planner]`）→ `LlmPlanner` → `ResilientPlanner`，
  暴露 `IPlanner`（供 UI 注入）与 planner 作用域的 `ILlmClient`。分层：`ILlmClient` 在
  Contracts、实现 + options 在 Backend、Agent 只经接口使用。
- **后期接缝**：提示词重写 / 多图任务复用 `LocalLlmClient` + 各自 `LlmClientOptions`，
  在 `AppContext` 按场景构造；**不引入** factory / 多实现框架（见 `OPTIMIZATION.md` §2.1）。

#### 5.3.3 测试（冻结）

- **文件**：`ZivAiEditor.Tests/PlannerTests.cs` + `LocalLlmClientTests.cs` + `PlannerIntegrationTests.cs`
  （无 GPU / 不加载模型，符合 Z29）
- **用例**：
  - `PlannerTests`：Fallback 单步（有/无 mask）、Fallback 缺主图抛 `ArgumentException`、
    LlmPlanner 解析有效 JSON、容忍 fenced JSON、无效 JSON / 空 steps / 缺 tool / 空响应抛
    `PlannerException`、超时抛 `PlannerException`、**多步 JSON 解析（Order 递增 + `depends_on`）**、
    ResilientPlanner 降级与主路成功。
  - `LocalLlmClientTests`：请求载荷（`enable_thinking=false` / `temperature=0.1` / `max_tokens`）、
    解析有效响应、HTTP 500 抛错、超时抛错、非法 JSON 抛错、options 温度生效。
  - `PlannerIntegrationTests`：真实 llama-server 调用（**服务可达才真跑，不可达则 2s 内自跳过**；
    运行前须确认 GPU 空闲，Z30）。
- **结果（Step 5 补完）**：`dotnet test` 过滤后 **21 通过 / 0 失败 / 1 跳过**
  （`LocalLlmClientTests` 6 + `PlannerTests` 12 + `ContractsSmokeTests` 3；集成 1 跳过）。
- **结果（真实调用验证，2026-09-22）**：用 `llmctl` 拉起 `qwythos-9b`（llama-server :8080）后，
  过滤测试 **24 通过 / 0 失败**（`LocalLlmClientTests` 6 + `PlannerTests` 13 + `ContractsSmokeTests` 3
  + `PlannerIntegrationTests` 2）；集成用例 **`path=llm`**（LlmPlanner 成功、未降级），
  `Steps.Count = 1`（`img2img`）；复杂 prompt 亦为单步（工具注册表为空 + 提示词要求最少步骤）。
  验证后 `llmctl stop`，GPU 由 10720 MiB 回落至 **847 MiB**。

---

## Step 6（日期：2026-09-22）

> **修订说明（Step 6 · 推理管线优化：SageAttention + 长边分辨率 + Steps 默认）**
>
> 本段为 Step 6 追加。目标：① 默认启用 SageAttention ② 分辨率默认语义改为「目标长边 1536」
> ③ `InpaintRequest.Steps` 默认 20 → 25 ④ 启用 ComfyUI Dynamic VRAM（见 6.6）。
> **不改动 Step 0–5 已冻结行**；唯一契约数值变更见下方 6.3（`InpaintRequest.Steps` 默认值）。

### 6.1 分辨率默认语义：长边（side）口径（冻结）

- **改了什么**：新增 `config.RESOLUTION_MODE`（`"side"` / `"area"`）、`config.RESOLUTION_SIDE`
  （默认 **1536**）与 `config.RESOLUTION_SIDE_FALLBACK`（默认 `1536,1280,1024,768,640`）；
  `pipeline` 新增 `_target_size(width, height, value, mode)`，把「目标分辨率」映射到目标
  width/height：`side` 令 `max(w,h)==value`（目标**长边**），`area` 保持 Step 4 的
  `w*h≈value²` 面积口径。
- **为什么**：Qwen-Image-2.1 下短边过小会明显损失细节；以长边（1536）为口径更直观，且与
  常用「长边 N 像素」描述一致。
- **兼容规则（保留 Step 4 行为）**：显式设置环境变量 `ZIV_AI_MAX_RESOLUTION` 会**强制**
  `RESOLUTION_MODE="area"`（使既有测试 / 脚本仍按面积口径工作）；未设置时默认 `"side"`。
  `ZIV_AI_RESOLUTION_MODE` 可显式覆盖（env 优先于兼容规则）。
- **影响哪些接口**：`pipeline.encode_prompt(...)` 追加可选参数 `mode=None`（缺省取
  `config.RESOLUTION_MODE`），**向后兼容**；`run()` / `_run_once()` 内部传递 `mode`。
  `config.MAX_RESOLUTION` / `RESOLUTION_FALLBACK` / `DEFAULT_RESOLUTION` **原样保留**。

### 6.2 SageAttention 默认开启（冻结）

- **改了什么**：新增 `config.SAGE_ATTENTION`（默认 `"1"` 即开启）；
  `model_loader._apply_runtime_defaults()` 去掉 `DISABLE_SMART_MEMORY` 的提前返回，改为分别
  判断——保留原有 disable-smart-memory 行为，并新增 SageAttention 门控：仅当
  `config.SAGE_ATTENTION` 且 `importlib.util.find_spec("sageattention")` 非 `None` 时才设
  `comfy.cli_args.args.use_sage_attention = True`。
- **为什么**：降低注意力开销（与 Step 3 关闭 smart memory 同属运行时策略）。
- **包缺失时保持关闭**：`sageattention` 未安装时**不**设置该 flag（否则 ComfyUI
  `comfy/ldm/modules/attention.py:30-36` 会 `exit(-1)`），并记一条 warning。
- **回退**：设 `ZIV_AI_SAGE_ATTENTION=0`（或 `false`）恢复原生 attention。
- **影响哪些接口**：无契约变更；仅 `model_loader` 运行时默认。

### 6.3 `InpaintRequest.Steps` 默认 20 → 25（冻结）

- **改了什么**：`ZivAiEditor.Contracts/Inference/InpaintRequest.cs` 的 `Steps` 默认值由
  **20** 改为 **25**（其余成员不动）。
- **为什么**：提高默认采样质量（相关实测数字由主会话实测后另行登记）。
- **推翻 Step 4.1 的冲突措辞**：Step 4.1 称「既有成员与默认值不变」；本项**明确推翻**该措辞中
  与 `Steps` 默认值相关的部分——仅 `Steps` 默认值变更，Step 4.1 新增的 `Lora` /
  `Optimizations` 与其余默认值保持不变。Step 4.1 原行不改，以本段为准。
- **影响哪些接口**：`InpaintRequest.Steps` 默认值（`IInferenceClient` 签名不变）；显式传
  `Steps` 的调用方不受影响。IPC 契约 `contracts/ipc-protocol.md` 的 `submit.payload` 示例
  `steps` 同步为 25。

### 6.4 文档同步

- `contracts/ipc-protocol.md`：`submit.payload` 示例 `"steps"` 由 20 同步为 25（结构与
  `ipc_version` 不变，仍为 `0.5`）。
- `DOC/OPTIMIZATION.md`：本段**不写入** ZIV.AI 改后的实测数字（由主会话实测后补）。

### 6.5 新增铁律 Z30（冻结）

| 编号 | 铁律 | 冻结于 | 备注 |
|---|---|---|---|
| Z30 | GPU 任务先确认空闲：启动任何会占用 GPU 的任务前**必须先确认 GPU 空闲**（无其他计算进程、显存接近基线）；**不空闲时必须先与用户确认才能继续**，不得自行启动 | Step 6 | **核心动机：保护硬件资产**；正文见 `SPEC.md` §6.2（Z30） |

> **理由（核心动机：保护硬件资产）**：2026-09-22，在未确认 GPU 空闲、且未按
> `DOC/OPTIMIZATION.md` §6.1「1024 面积口径峰值 16004/16376 MiB，仅余约 370 MiB」预留余量
> 的情况下，同进程连跑两次 1536×960 采样，导致整机崩溃。**关联**：`SPEC.md` §6.2（Z30）、
> `ACCEPTANCE.MD`「测试执行原则」。

### 6.6 运行时显存策略：启用 ComfyUI Dynamic VRAM（Step 6 修正）

- **改了什么**：新增 `config.DYNAMIC_VRAM`（默认开启，env `ZIV_AI_DYNAMIC_VRAM`），并在
  `model_loader.prepare_environment()` 增加 `_enable_dynamic_vram()`：复现官方
  `ComfyUI/main.py` 的 DynamicVRAM 引导（`comfy_aimdo.control.init()` → `init_devices()` →
  `model_patcher.CoreModelPatcher = ModelPatcherDynamic` → `memory_management.aimdo_enabled = True`）。
- **为什么（根因）**：该引导只写在官方入口 `ComfyUI/main.py` 里。ZIV.AI 后端是进程内直连
  ComfyUI（入口 `python/server/main.py`），从不执行它，于是落到 legacy `ModelPatcher`：粗粒度
  offload、卸载不彻底；16GB 卡上 side 1536 时 TE(~8.7G) + VAE encode(~9G) + DiT(~9.4G) 叠加
  冲顶（实测峰值 16030 MiB、冷跑 41.9s，并曾导致整机崩溃，见 `SPEC.md` §6.2 Z30）。启用
  Dynamic VRAM 后权重以 vbar 按需换入换出，峰值 **13091 MiB**、冷跑 **19.73s**（优于官方
  server 流的 22.15s / 14968 MiB）。
- **初始化顺序（关键约束）**：`comfy_aimdo.control.init()` 必须在首次 import
  `comfy_aimdo.host_buffer` 之前执行 —— `host_buffer` 在 import 时绑定 `lib = control.lib`，
  若先 import `comfy.model_management` 会把该引用冻结成 `None`，加载模型时报
  `'NoneType' object has no attribute 'hostbuf_allocate'`。
- **回退**：设 `ZIV_AI_DYNAMIC_VRAM=0`（或 `false`）回到 legacy `ModelPatcher`。
- **未采用**（本步初稿，实测后移除）：`RESERVE_VRAM_GB`（保留显存，无收益）；`pipeline._release`
  手动分时卸载（A/B 对照 19.96/18.12s vs 19.73/18.13s，无收益，Dynamic VRAM 下冗余）。
- **Step 3.4 关系**：Step 3.4「运行时默认关闭 smart memory」**继续有效**（`DISABLE_SMART_MEMORY`
  维持 `1`，与 Dynamic VRAM 无冲突，实测生效）。
- **影响哪些接口**：无契约变更；仅运行时显存/加载策略（`config` / `model_loader`）。

---

## Step 6（Executor）（日期：2026-09-22）

> **命名说明**：上方「Step 6（推理管线优化）」是另一条工作流（SageAttention / 长边 1536 /
> Dynamic VRAM / Z30）；本段是 `ACCEPTANCE.MD` 规划表所指的 **Step 6 = Executor**
> （多步执行 + 进度 + 取消 + 重跑占位 + 串行队列）。**两段互不改动**，标题显式标注
> 「Executor」以区分。本段**无新增契约**，不改 Step 0–5 已冻结行。

### 6E.1 无新增契约（冻结确认）

本步**不新增、不修改**任何契约。`IExecutor` / `IEditTool` / `IToolRegistry` / `TaskState` /
`StepState` / `TaskProgress` / `StepProgress` / `ToolInput` / `ToolResult` / `IInferenceClient`
签名与 Step 0 0.3 / Step 1.2 **完全一致**。

### 6E.2 实现清单（冻结）

| 类 | 文件 | 职责 |
|---|---|---|
| `ToolRegistry` | `ZivAiEditor.Tools/ToolRegistry.cs` | `ConcurrentDictionary` 实现的 `IToolRegistry`；`Register`（同名替换）/ `Unregister` / `Get` / `All`（快照）线程安全；无额外抽象（§11） |
| `InpaintTool` | `ZivAiEditor.Tools/InpaintTool.cs` | `IEditTool`；`Name="inpaint"`；注入 `IInferenceClient`；`ToolInput.Parameters`（`prompt` 必填，`steps`/`seed`/`denoise` 可选）→ `SubmitInpaintAsync` → `ToolResult`；输出新文件（Z24，`output_path` 优先，否则 `WorkingDirectory/{StepId}.png`）；同步转发 `InferenceProgress` → `StepProgress` |
| `ExecutionQueue` | `ZivAiEditor.Agent/ExecutionQueue.cs` | `SemaphoreSlim(1,1)` 单槽串行（Z18）；`RunAsync<T>`；优先级留待后续 |
| `Executor` | `ZivAiEditor.Agent/Executor.cs` | `IExecutor`；注入 `IToolRegistry` + `ExecutionQueue`；按 `Order` 编排、串联中间结果、`DependsOn` 校验、失败保留中间结果、取消、进度；`RerunAsync` → `NotSupportedException`；**不注入 / 不调 `IInferenceClient`** |
| （修改）`IpcInferenceClient.SubmitInpaintAsync` | `ZivAiEditor.Backend/IpcInferenceClient.cs` | 新增 `ct` 取消分支：用 `CancellationToken.None` 发 `cancel` 帧并等 `canceled` 后抛 `OperationCanceledException(ct)`；**签名不变**、超时路径不变 |
| （删除）`PlaceholderToolRegistry` / `PlaceholderExecutor` / `EmptyToolRegistry` | — | 由真实实现取代 |

- **App 装配**：`AppContext` 构造真实 `ToolRegistry` + 注册 `InpaintTool` + `ExecutionQueue` +
  `Executor`，暴露 `Tools` / `Executor`；`LlmPlanner` 改用真实注册表。

### 6E.3 职责划分（冻结）

`Planner`（生成 `EditPlan`）/ `Executor`（只编排，不调推理）/ `Tool`（经 `IInferenceClient`
执行单步）/ `PromptOptimizer`（独立前置层，未实现，仅登记 `OPTIMIZATION.md` §2.1）四者边界
见 `DEVLOG.md`「Step 6（Executor）」的职责划分表。

### 6E.4 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release`：**0 错误 0 警告**。
- `dotnet test`（按 Z29 只跑受影响类，无 GPU）：
  `ExecutorTests` + `ToolRegistryTests` + `InpaintToolTests` + `ExecutionQueueTests`
  → **21 通过 / 0 失败**。
- GPU 端到端**未跑**（`nvidia-smi` = 1587 MiB，不满足 Z30；未获用户明确同意）。

### 6E.5 收尾修正（2026-09-22，追加）

> 依据独立只读验证确认的两个遗留假设，做两处小修正。**无契约变更**，不改任何公开方法签名，
> 不改 Step 0–5 冻结行，不改 `python/server/*`。详见 `DEVLOG.md`「Step 6 收尾修正」。

1. **`InpaintTool` 的 `output_path` 守卫**：`output_path` 与 `MainImagePath` 规范化后相同
   （`Path.GetFullPath` + 不区分大小写）时，后端会忽略该路径并改用默认路径，导致回填路径
   与磁盘文件不符、多步链式断裂。现改为回退 `WorkingDirectory/{StepId}.png`。
   `IEditTool` / `ToolInput` / `ToolResult` 签名不变。
2. **`FallbackPlanner` 无 mask 改用 `inpaint`**：有 / 无 mask 统一 `ToolName="inpaint"`，
   使 `Executor` 能用已注册的 `InpaintTool` 执行无 mask 计划。`ImageToImageToolName` 常量
   **保留并标记 `[Obsolete]`**（不改公开签名；计划 Step 7 移除）。
   `IPlanner.PlanAsync` 签名不变。
3. **行为澄清（冻结记录）**：后端无 mask 路径是「零 latent + 参考图条件」
   （`pipeline._encode`），**不是经典 img2img**；`denoise<1` 亦非从输入图 latent 部分去噪。
   「经典 img2img」为**候选特性**（需后端 `_encode` 加 flag），非当前范围，
   登记于 `OPTIMIZATION.md` §2.1.2。

- **收尾测试结果**：`dotnet test --filter "ExecutorTests|InpaintToolTests|PlannerTests"`
  → **31 通过 / 0 失败**（无 GPU）。

### 6E.6 工具命名统一修订（2026-09-22，追加）

> 依据 Qwen-Image-2.1 编辑机制调研与用户裁决：编辑工具命名**统一为 `QW21edit`**
> （QW21 = Qwen-Image-2.1）。**无契约变更**（`IEditTool` / `IToolRegistry` / `IExecutor` 签名
> 不变），不改 Step 0–5 冻结行，不改 `python/server/*`，不改 `ipc-protocol.md` 消息类型。
> 本段**取代** 6E.2 中 `InpaintTool` 的工具名与 6E.5 第 2 条的工具名措辞（旧行不改，以本段为准）。

1. **新增工具标识 `"QW21edit"`（替换 `"inpaint"`）**：类 `QwenImage21EditTool`，文件
   `ZivAiEditor.Tools/QwenImage21EditTool.cs`（由 `InpaintTool.cs` 重命名）；
   `Name` / `ToolName = "QW21edit"`；
   `Description = "Qwen-Image-2.1 图像编辑（有 mask 时局部编辑；无 mask 时参考条件编辑）"`；
   `Capabilities = ["edit","inpaint","reference-edit","background-replace"]`。
   `CanHandle` 仅接受 `"QW21edit"`（**不保留** `"inpaint"` / `"img2img"` 别名）。
2. **`FallbackPlanner` 统一产出 `QW21edit`**：常量 `EditToolName = "QW21edit"`；
   删除 `ImageToImageToolName` 与旧 `InpaintToolName`。`IPlanner.PlanAsync` 签名不变。
   有 / 无 mask 的区别由 `PlanRequest.Mask` → `ToolInput.Mask` 承载。
3. **IPC `op` 保持 `"inpaint"`**：传输层标识，与 C# 工具名分层；不改 `ipc-protocol.md`。
4. **语义澄清（冻结）**：`QW21edit` 有 mask = 输入图 latent + `noise_mask`（局部编辑）；
   无 mask = 纯噪声（`torch.zeros`）+ `reference_latents`（**参考条件编辑**）。
   **经典 img2img**（输入图 latent + `denoise<1` 部分去噪）**不实现**，登记为
   `OPTIMIZATION.md` §2.1.2 候选（Qwen-Image 系列在 ComfyUI 有已知未解决问题
   GitHub Issue **#9702** / **#10063**）。
5. **`LlmPlanner` 提示词对齐（附加，非契约）**：工具清单 / 规则 / few-shot 统一为 `QW21edit`。
6. **App 装配**：`AppContext` 注册 `QwenImage21EditTool`；`Get("QW21edit")` 命中，
   `Get("inpaint")` / `Get("img2img")` 返回 null。

- **命名收尾测试结果**：
  `dotnet test --filter "ExecutorTests|QwenImage21EditToolTests|ToolRegistryTests|PlannerTests"`
  → **36 通过 / 0 失败**（无 GPU）。

---

## Step 6.5（日期：2026-09-22）

> **修订说明（Step 6.5 · 分辨率策略 ResolutionPolicy + ModelProfile）**
>
> 本段为 Step 6.5 追加。目标：让用户/UI 能指定输出分辨率——此前分辨率写死在
> `python/server/config.py`（`RESOLUTION_MODE=side` / `RESOLUTION_SIDE=1536`），C# 侧完全不感知。
> 影响 QW21edit（输出清晰度）、upscale（放大倍数）、outpaint（扩图尺寸）三类工具。
> **不改动 Step 0–6 已冻结行的既有成员**；契约变更为**新增可选字段 / 新类型**（非破坏性）。
> 设计裁决：① IPC 传**绝对分辨率**（C# 翻译 tier → 数值；Python 无状态，Z23）；
> ② Qwen-2.1 三档 Fast=1024 / Balanced=1536 / HighQuality=2048；
> ③ 独立 Step 6.5，不与 Step 7 工具混合；④ 引入 `ModelProfile` 支持未来模型扩展；
> ⑤ UI 不在本步范围（Step 9）。

### 6.5.1 新增契约（冻结）

| 类型 | 文件 | 说明 |
|---|---|---|
| `ResolutionMode`（枚举） | `Contracts/Imaging/ResolutionMode.cs` | `Side` / `Area` / `Scale` / `Explicit` |
| `ResolutionPolicy`（值对象） | `Contracts/Imaging/ResolutionPolicy.cs` | `Mode`；`Side?` / `Area?` / `Scale?` / `Width?` / `Height?`；`MaxPixels`（默认 `4_194_304` = 2048²，OOM 安全上限） |
| `ResolutionTier`（枚举） | `Contracts/Models/ResolutionTier.cs` | `Fast` / `Balanced` / `HighQuality` / `Custom` |
| `AspectPreset`（值对象） | `Contracts/Models/AspectPreset.cs` | `Name` / `Width` / `Height` |
| `ModelProfile`（值对象） | `Contracts/Models/ModelProfile.cs` | `ModelId` / `DisplayName` / `NativeSide` / `SafeMaxSide` / `MinSide` / `MultipleOf` / `TierSides` / `Presets` |
| `IModelProfileRegistry`（接口） | `Contracts/Models/IModelProfileRegistry.cs` | `ModelProfile? Get(string)`；`ModelProfile Default`；`IReadOnlyList<ModelProfile> All` |

### 6.5.2 三层可选字段扩展（冻结，非破坏性）

- `InpaintRequest.Resolution`（`ResolutionPolicy?`，默认 `null`）
- `EditPlan.Resolution`（`ResolutionPolicy?`，默认 `null`）
- `ToolInput.Resolution`（`ResolutionPolicy?`，默认 `null`）

**默认 `null` = 不传 = 各层使用缺省行为（Python 用 `config` 默认 1536）**，旧调用方行为与 Step 6
完全一致。`IInferenceClient` / `IExecutor` / `IEditTool` / `IToolRegistry` 签名不变。

### 6.5.3 实现清单（冻结）

| 类 / 改动 | 文件 | 职责 |
|---|---|---|
| `ModelProfileRegistry` | `ZivAiEditor.Backend/ModelProfileRegistry.cs` | `IModelProfileRegistry` 实现；注册 Qwen-Image-2.1（NativeSide/SafeMaxSide=2048、MinSide=512、MultipleOf=16、三档 1024/1536/2048、7 组比例预设）；`Default` = Qwen-2.1；预留 `IModelProfileProvider` 扩展位（不实现） |
| `ResolutionResolver` | `ZivAiEditor.Backend/ResolutionResolver.cs` | `FromTier(tier, profile)`：`Custom` 抛 `ArgumentException`；否则返回 `Mode=Side`、`Side=TierSides[tier]`、`MaxPixels=SafeMaxSide²` |
| （修改）`IpcInferenceClient` | `ZivAiEditor.Backend/IpcInferenceClient.cs` | `SubmitPayload` 新增 `Resolution`；新增 `ResolutionPayload`（snake_case：`mode/side/area/scale/width/height/max_pixels`）并注册进 `IpcJsonContext` |
| （修改）`Executor` | `ZivAiEditor.Agent/Executor.cs` | 构造 `ToolInput` 时透传 `plan.Resolution` |
| （修改）`QwenImage21EditTool` | `ZivAiEditor.Tools/QwenImage21EditTool.cs` | `InpaintRequest.Resolution = input.Resolution` |
| （修改）`AppContext` | `ZivAiEditor.App/AppContext.cs` | 构造并暴露 `IModelProfileRegistry ModelProfiles` |
| （修改）Python | `python/server/pipeline.py` / `handlers.py` / `config.py` | 读 `submit.payload.resolution`（`side`/`area`/`scale`/`explicit`），覆盖 `config` 默认；`max_pixels` 超限降级并记日志；无 payload 时用 config 默认；`PROTOCOL_VERSION` 0.5 → 0.6 |
| 跨进程契约 | `contracts/ipc-protocol.md` | `ipc_version 0.5 → 0.6`；`submit.payload.resolution` 可选字段（§3.4 / §7） |

- **边界**：`tier → Side` 仅用于 QW21edit；upscale 用 `Scale`、outpaint 用 `Explicit`（Step 7 再定），
  **不引入** `UpscaleResolver` / `OutpaintResolver`（ARCHITECTURE.md §11）。

### 6.5.4 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release`：**0 错误 0 警告**。
- `dotnet test`（按 Z29 只跑受影响类，无 GPU）：`ResolutionPolicyTests`（5）+
  `ModelProfileRegistryTests`（4）+ `QwenImage21EditToolTests`（12）+ `ExecutorTests`（11）+
  `ToolRegistryTests`（4）+ `ContractsSmokeTests`（3）→ **39 通过 / 0 失败**。
- Python 侧 `py_compile` 通过；**未跑 GPU 端到端**（Z29 / Z30）。

---

## Step 7（日期：2026-09-22）

> **修订说明（Step 7 · 工具集扩展 + 泛化提交入口 + T2I + MaxPixels 修正）**
>
> 本段为 Step 7 追加。目标：① 泛化提交入口（`EditRequest` + `SubmitEditAsync`）
> ② 新增 `QW21outpaint` ③ 支持 T2I（文生图）④ 修正 `MaxPixels`（16:9 预设不再被 clamp）
> ⑤ Segment / Upscale 记为 Step 7.5 候选。**不改动 Step 0–6.5 已冻结行的既有成员**；
> 契约变更为**新增类型 / 新增方法 / 默认值修正**，走修订说明。

### 7.1 可行性调研结论（冻结）

| 工具 | 能否用 Qwen-Image-2.1 原生 | 本步处置 | 依据 |
|---|---|---|---|
| Segment 去背景 | ❌ 无原生 RGBA 输出 | **不注册**，Step 7.5 候选 | ComfyUI 核心 `nodes_bg_removal.py` 存在，但 `models/background_removal/` 无权重 |
| Upscale 放大 | ❌ 非超分模型 | **不注册**，Step 7.5 候选 | 盘上已有 Real-ESRGAN x4plus / 4x-UltraSharp；ComfyUI 核心有 `ImageUpscaleWithModel` |
| Outpaint 扩图 | ✅ 标准 inpaint 变体（扩展画布 + anchor 贴入 + mask 覆盖新区域） | **实现** `QW21outpaint` | `pipeline._encode` masked 路径已支持 |
| T2I 文生图 | ✅ 无源图路径（零 latent + 文本条件） | **实现**（作为 `EditRequest` 的一种 op） | `pipeline._size_for_no_source` 已就绪 |

- **不引入独立超分 / 分割模型**（硬约束）；Segment / Upscale 的具体模型与显存需求登记于
  `OPTIMIZATION.md` §2.2 / §2.3，作为 Step 7.5 候选。

### 7.2 新增契约（冻结）

| 类型 / 成员 | 文件 | 说明 |
|---|---|---|
| `EditRequest`（类） | `Contracts/Inference/EditRequest.cs` | 泛化单次推理请求；字段见下 |
| `IInferenceClient.SubmitEditAsync`（方法） | `Contracts/Inference/IInferenceClient.cs` | `Task<InferenceTaskHandle> SubmitEditAsync(EditRequest, IProgress<InferenceProgress>?, CancellationToken ct = default)` |

**`EditRequest` 字段**：

- `Op`（默认 `"inpaint"`）：取值 `t2i` / `inpaint` / `outpaint`。
- `ImagePath`（`string?`，可空）：`t2i` 时为 `null`。
- `MaskPath`（`string?`）、`Prompt`、`Steps`（默认 25）、`Seed`（默认 -1）、
  `Denoise`（默认 1.0）、`OutputPath`、`Resolution`（`ResolutionPolicy?`）、
  `Lora`（`LoraOptions?`）、`Optimizations`（`OptimizationOptions?`）。
- `Anchor`（`string?`）：`outpaint` 的原图 9 宫格位置；缺省 = `center`。

> **说明**：`Lora` / `Optimizations` 一并纳入 `EditRequest`，使 `SubmitInpaintAsync`
> 委托 `SubmitEditAsync` 时**不丢失 Step 4 行为**（计划原稿未列，属必要保留）。

**约束**：

- `SubmitInpaintAsync` **保留不变**（签名 / 行为），实现内部构造 `Op="inpaint"` 的
  `EditRequest` 并委托 `SubmitEditAsync`。
- `InpaintRequest` **原样不动**。
- `Anchor` 语义（9 宫格）：`center` / `left` / `right` / `top` / `bottom` /
  `top-left` / `top-right` / `bottom-left` / `bottom-right`。

### 7.3 MaxPixels 修正（冻结）

- **问题**：`ResolutionResolver.FromTier` 用 `SafeMaxSide² = 4,194,304` 计算 `MaxPixels`，
  与官方 16:9 预设（2752×1536 = **4,227,072**）冲突，导致 16:9 被 clamp。
- **修正**：
  - `ModelProfile` 新增字段 `MaxPixels`（默认 **4,700,000**）。
  - `ResolutionResolver.FromTier` 改读 `profile.MaxPixels`（不再用 `SafeMaxSide²`）。
  - `ResolutionPolicy.MaxPixels` 默认值 **4,194,304 → 4,700,000**。
  - `ModelProfileRegistry` 的 Qwen-2.1 profile 设 `MaxPixels = 4,700,000`。
- **语义**：`SafeMaxSide` 表达「最长边」安全上限；`MaxPixels` 表达「总像素」安全上限，
  二者**独立**。

### 7.4 SPEC 修订（冻结）

- `SPEC.md` §3.1：原「主图缺失时禁止提交」修订为「主图缺失时，若 `Prompt` 非空则生成
  T2I 任务；若 `Prompt` 也为空，禁止提交」。通过标准同步为「三图齐全 / 仅主图 /
  仅 `Prompt`（T2I）三种情形都能产出可执行计划；主图与 `Prompt` 同时缺失被拒绝」。

> **Phase 1 范围**：本节仅冻结契约。实现清单与测试结果见 Phase 2 追加（7.5）。

### 7.5 实现清单（冻结）

> Phase 2 落地。**契约以 7.2–7.4 为准**；`EditOps` 为 C# 常量类（非契约修订）。

| 类 / 改动 | 文件 | 职责 |
|---|---|---|
| `EditOps`（新增，静态常量类） | `Contracts/Inference/EditOps.cs` | `T2I` / `Inpaint` / `Outpaint` 常量，替代 C# 侧 op 字符串硬编码 |
| `IpcSubmitMapper`（新增） | `Backend/IpcSubmitMapper.cs` | `internal static`；`BuildSubmitRequest(requestId, taskId, EditRequest)` 构造 IPC `submit` 载荷（Op/ImagePath(null)/Anchor/Resolution）；无平台属性，供测试直接调用 |
| （修改）`IpcInferenceClient` | `Backend/IpcInferenceClient.cs` | `SubmitEditAsync` 真实实现（原 `SubmitInpaintAsync` 主体参数化）；`SubmitInpaintAsync` 构造 `EditRequest{Op=Inpaint, Anchor=null}` 委托，签名/语义不变；`SubmitPayload` 增 `Anchor` |
| （修改）`Backend.csproj` | — | 加 `InternalsVisibleTo("ZivAiEditor.Tests")`（测试 `IpcSubmitMapper`） |
| `QwenImage21OutpaintTool`（新增） | `Tools/QwenImage21OutpaintTool.cs` | `Name="QW21outpaint"`；`Capabilities=["outpaint","expand","extend-canvas"]`；要求 `Resolution.Mode=Explicit`（否则失败不提交）；`Parameters["anchor"]`（缺省 `center`）→ `EditRequest{Op=Outpaint}` |
| `ToolOutputPath`（新增） | `Tools/ToolOutputPath.cs` | 从 `QwenImage21EditTool` 抽出的共享输出路径逻辑；无工作目录时回退 `%TEMP%/zivai/{StepId}.png`（T2I 也有可用路径） |
| （修改）`QwenImage21EditTool` | `Tools/QwenImage21EditTool.cs` | `MainImagePath` 空白 + prompt 非空 → `Op=T2I`（`ImagePath=null`、`MaskPath=null`）；否则 `Op=Inpaint`；改用 `SubmitEditAsync` |
| （修改）`FallbackPlanner` / `LlmPlanner` | `Agent/` | 守卫放宽为「主图与 prompt **皆空**才抛」；`LlmPlanner` 系统提示词加 T2I 规则（无主图 → 单步 `QW21edit`，参数不含 `image_path`） |
| （修改）`AppContext` | `App/AppContext.cs` | 注册 `QwenImage21OutpaintTool`；Segment / Upscale **不注册** |
| （修改）Python | `python/server/{outpaint,pipeline,handlers,config}.py` | `outpaint.py`（纯 CPU 几何：`snap16`/`normalize_anchor`/`anchor_position`/`build_outpaint`）；`pipeline.run_outpaint`（画布扩展 + 掩膜 + 复用 masked inpaint + 临时目录清理）；`handlers` op 分发（`outpaint`→run_outpaint，`t2i`/`inpaint`/缺省→run，未知→warn+回退）；`PROTOCOL_VERSION` 0.7 |

- **Anchor 非法值**：Python `outpaint.normalize_anchor` 对未知值 / `None` 一律降级为 `center`。
- **T2I 语义**：无源图 → `op="t2i"`，`image_path` 为 `null`，后端走 `_size_for_no_source`（零 latent + 文本条件）。
- **Outpaint 语义**：按 `_snap16` 目标尺寸建画布，原图按 anchor 贴入，掩膜新区域 255 / 原图区域 0，复用 masked inpaint；画布恰为目标尺寸，故 `_target_size_from_spec` 的 explicit resize 为 no-op。

### 7.6 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（按 Z29 只跑受影响类，**无 GPU**）：`EditRequestTests` + `IpcSubmitMapperTests` +
  `ResolutionPolicyTests` + `QwenImage21EditToolTests` + `QwenImage21OutpaintToolTests` +
  `PlannerTests` + `ToolRegistryTests` + `ContractsSmokeTests` + `ExecutorTests` +
  `ModelProfileRegistryTests` + `ExecutionQueueTests` → **76 通过 / 0 失败**。
- Python `py_compile`（`outpaint.py` / `pipeline.py` / `handlers.py` / `config.py`）通过；
  `python -m unittest test_outpaint`（`python/server/test_outpaint.py`，纯 CPU，无 comfy 依赖）
  → **9 通过 / 0 失败**（anchor 归一化 / `snap16` / `anchor_position` / `build_outpaint` 画布+掩膜）。
- **未跑 GPU 端到端**（Z29 / Z30）；outpaint 画布合成与 `run_outpaint` 委派为**只读代码确认**。

### 7.7 修订说明：Segment 定位更正（2026-09-22）

> 本小节为**只增**修订，**不改动 7.1–7.6 已冻结行**。7.1 表内「Segment 去背景：❌ 无原生
> RGBA 输出」的判断**有误**，以本小节为准。原文不改，仅在此更正。

- **事实更正**：Qwen-Image-2.1 的 VAE 为 **64 通道 RGBA**，alpha 通道是潜空间的**一等公民**，
  模型在去噪过程中**直接生成透明度**。ComfyUI v0.37.0 官方提供「Remove Background」模板，
  从采样器出来即为透明 PNG，**不需要** matting 模型或背景去除节点。
- **Segment 正确路径**：走 **Qwen-Image-2.1 原生 RGBA**（`QW21segment`，复用 `QW21edit`
  同管线，提示词含 `transparent background` / `RGBA` / `alpha channel`），**无需独立模型**；
  状态为 **Step 7.5 候选**（本轮未实测）。
- **BiRefNet / RMBG-2.0 重新定位**：它们是**独立的抠图模型**，输入为**已有的 RGB 图**、
  输出透明 PNG；用于「对已生成 / 已有图做**后处理抠图**」，是**替代路径 / 独立入口**，
  **不是 Segment 的依赖**，也不与 Qwen-Image-2.1 叠加。二者解决的问题相同（得到透明背景
  PNG），机制不同。
- **交叉引用更正**：7.1 表下注释「登记于 `OPTIMIZATION.md` §2.2 / §2.3」应为 **§7.1 / §7.3**
  （该注释为冻结行，本小节不改原文，仅在此更正）。
- **影响范围**：仅文档定位（`OPTIMIZATION.md` §7.1/§7.2/§7.3、`DEVLOG.md` Step 7、
  `ACCEPTANCE.md` Step 7）；**不改任何契约 / 代码 / 冻结成员**。

### 7.8 修订说明：Outpaint 对齐官方工作流（2026-09-22）

> 本小节为**只增**修订，**不改动 7.1–7.7 已冻结行**。7.5 实现说明中「outpaint 画布黑填充 +
> 掩膜新区域 255 / 原图区域 0」的措辞**以本小节为准**（原文不改）。

- **依据**：ComfyUI 官方蓝图 `ComfyUI/blueprints/Image Outpainting (Qwen-Image).json`
  （`ImagePadForOutpaint` + `Grow and Blur Mask` + InstantX Inpainting ControlNet）。
- **改了什么**（仅实现，不改契约）：
  - `python/server/outpaint.py`：画布填充由**黑 (0,0,0)** 改为**灰 0.5 (128,128,128)**；
    掩膜由**硬二值**改为**软掩膜**——按 `feathering=40` 的 `v²` 斜坡 + `grow=20` 膨胀 +
    `blur=31` 高斯（等价 `ImagePadForOutpaint` + `Grow and Blur Mask`）。
  - `python/server/pipeline.py`：`_load_mask_tensor(path, binary=True)` 支持软掩膜；
    `run(..., mask_binary=True)` / `encode_prompt` / `_encode` 透传；`run_outpaint` 传
    `mask_binary=False`。**Z19 不变**（用户 / C# 掩膜仍强制二值 0 / 255）；仅**后端生成的
    outpaint 掩膜**放行软值。
- **未采用**：官方 v1 蓝图所用的 **InstantX Inpainting ControlNet**（面向 Qwen-Image v1，
  与 Qwen-Image-2.1 兼容性未验证）；登记为候选 `OPTIMIZATION.md` §7.4，后续有需求再评估。
- **实测**（GPU，2026-09-22）：`QW21outpaint` 在**无黑边源图**上**无缝外扩**，中心逐像素保留
  （MAD 1.4 / corr 0.9994）；含 letterbox 黑边的源图仍呈「框中景」（**输入所致**，非算法缺陷）。
  详见 `_test_step2/e2e_step7/RESULT.md`。
- **影响范围**：`python/server/outpaint.py` / `pipeline.py`（实现）、`test_outpaint.py`（单测）；
  **不改任何 C# 契约 / 冻结签名 / `ipc-protocol.md`**（op / anchor / payload 不变）。

---

## 修订说明（2026-09-22 · Step 7 审计修正：Z8 拆分 + 1.1 项目表同步）

> 本段为**只增**修订，**不改动 Step 0–7 已冻结行**。依据 Step 7 审计发现，处理 Z8
> 违规与 1.1 项目表同步；均不涉及契约 / 签名 / 行为变更。

### 7.9 代码拆分（Z8）

- **改了什么**：`ZivAiEditor.Backend/IpcInferenceClient.cs`（896 物理行 / 778 非空行）
  拆为 5 个文件，均为**纯物理拆分**，不改行为 / 签名 / 可见性：
  - `IpcFraming.cs`：帧格式（长度前缀读写 / `ReadFrameAsync` / `WriteJsonAsync`）
  - `IpcDtos.cs`：`PingRequest` / `SubmitPayload` / `ResolutionPayload` / `SubmitRequest` / `CancelRequest`
  - `IpcJsonContext.cs`：`JsonSerializerContext` + `JsonSerializable`
  - `IpcInferenceClient.cs`：核心公共 API（提交 / 取消 / 健康检查 / 生命周期），`partial`
  - `IpcInferenceClient.Receive.cs`：后台接收循环 / 心跳 / 帧分发与解析，`partial`
- **为什么**：`Z8 · 无上帝模块`（单文件超过 600 行必须拆分）。拆分后各文件
  323 / 422 / 93 / 37 / 17 物理行，均 < 600（非空行口径 284 / 365 / 78 / 31 / 15）。
- **死代码清理**：删除零调用点的三参 `HandleBinaryFrame(byte[], string, int, int)`；保留单参版本。
- **性质**：纯重构，`IInferenceClient` / DTO 字段 / IPC 消息均不变；`ipc-protocol.md` 不变。

### 7.10 1.1 项目表同步（修订说明，不改原表）

- **事实**：`FROZEN.md` 1.1 项目表将 `ZivAiEditor.Tests` 的依赖列为
  `Contracts`、`Agent`、`Tools`（+ xunit / Microsoft.NET.Test.Sdk）。
  Step 7 为 `ZivAiEditor.Backend` 增加 `InternalsVisibleTo("ZivAiEditor.Tests")`
  （测试 `IpcSubmitMapper`），使 Tests **同时引用 `Backend`**。
- **处理**：1.1 原表**不改**（只增不改），以本修订说明为准——Tests 依赖更新为
  `Contracts`、`Agent`、`Tools`、**`Backend`**（+ xunit / Microsoft.NET.Test.Sdk）。
- **影响**：仅项目引用说明；不改契约 / 签名 / 行为。

### 7.11 Z14 便携性缺口登记

- **处理**：新建 `DOC/RELEASE-CHECKLIST.md` 记录发布前必做项（路径硬编码 /
  publish 不打包后端 / `FindTemplate` 找错文件）。
- **性质**：仅文档登记；本步不改运行时代码。
