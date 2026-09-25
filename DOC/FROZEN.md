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

---

## Step 8（日期：2026-09-22）

> **修订说明（Step 8 · 对话式交互逻辑层）**
>
> 本段为 Step 8 追加。目标：实现 `DOC/INTERACTION.md` 的**逻辑层**——
> `CommandParser`（正则 / `commands.json`，不走 LLM）+ `EditSession`（内存 DAG，不持久化）+
> `SessionExporter`（关闭时导出）。UI（聊天流 + 历史节点列表）留 Step 9；自然语言 LLM 重写、
> `@图片N` 多图引用后置。**不改动 Step 0–7 已冻结行的既有成员**；契约变更为**新增接口 / 类型**
> （非破坏性），且**不新增 `Contracts` 项目类型**（新接口按归属放 Agent / App 层，见
> `INTERACTION.md` §5）。

### 8.1 新增契约（冻结）

| 类型 | 文件 | 说明 |
|---|---|---|
| `ICommandParser`（接口） | `ZivAiEditor.Agent/CommandParser.cs` | `Task<ParseResult> ParseAsync(string input, EditSession session, CancellationToken ct = default)` |
| `ParseResult`（类） | 同上 | `bool Success`；`EditPlan? Plan`；`string? ErrorMessage`；`string? MatchedCommand` |
| `CommandDefinition`（类） | 同上 | `Name` / `Params` / `Tool` / `Template` / `Description`（`commands.json` 条目） |
| `CommandParser`（类） | 同上 | `ICommandParser` 实现；构造 `CommandParser(string commandsJsonPath = "Template/commands.json")`；构造时加载，失败用内置默认集 |
| `EditSession`（类） | `ZivAiEditor.Agent/EditSession.cs` | `SessionId` / `RootImagePath?` / `Nodes` / `CurrentNodeId?` / `CreatedAt`；`SetRoot` / `AppendNode` / `NavigateTo` / `GetHistory` / `GetCurrentImagePath` |
| `EditNode`（类） | 同上 | `NodeId` / `ParentNodeId?` / `ImagePath` / `Command` / `CreatedAt` |
| `ISessionExporter`（接口） | `ZivAiEditor.App/SessionExporter.cs` | `Task<string?> ExportAsync(EditSession session, string outputDirectory, CancellationToken ct = default)`；失败返回 `null`，不抛 |
| `SessionExporter`（类） | 同上 | `ISessionExporter` 实现；写 `session.json` + 拷贝节点图 `{NodeId}.png` |

> **归属**：`CommandParser` / `EditSession` / `EditNode` 是编排逻辑，归 Agent（只依赖
> `Contracts`）；`SessionExporter` 属平台 / 文件 IO，归 App。与 `INTERACTION.md` §5 一致。

### 8.2 `Template/commands.json` 结构（冻结）

- 路径：`Template/commands.json`（仓库根；App 以 `Link="Template/commands.json"` 拷贝到输出）。
- 结构：`{ "version": "1.0", "commands": [ { name, params[], tool, template, description } ] }`。
- 初始命令集：`/换背景 <target>`、`/去水印`、`/去物体 <object>`、`/扩图 <width> <height>`。
- 模板遵循 Qwen-Image-2.1 官方提示词规范：`<image1>` 标签引用主图（多图后置）、编辑目标与
  保持内容分列、肯定性表述（"Keep ... unchanged"）。
- `tool` 取值必须是已注册工具名 `QW21edit` / `QW21outpaint`。
- 缺失 / 解析失败 → `CommandParser` 使用**内置默认集**（含上述 4 条；Z28 无外部依赖）。

### 8.3 解析与 DAG 语义（冻结）

- 输入以 `/` 开头 → 按空白分词；首词精确匹配（Ordinal）命令名；参数数量须与 `params` 一致；
  `{param}` 纯字符串替换；产出单步 `EditPlan`（`ToolName` = 命令 `tool`，`MainImagePath` =
  当前工作图）。命令不匹配 / 参数不符 / 无图且无 prompt → `Success=false` + `ErrorMessage`。
- 输入不以 `/` 开头 → 原文作为 prompt，产出单步 `QW21edit` 计划（无主图 → T2I）。
- **`/扩图` 特例**：`QW21outpaint` 要求 `Resolution.Mode=Explicit`（FROZEN 7.5），故
  `width`/`height` 参数同时被翻译为 `ResolutionPolicy{Mode=Explicit, Width, Height}`；否则该
  命令无法执行。此为 Step 8 的定向集成，不改变任何契约。
- `EditSession` 为内存 DAG，不持久化、无并发控制（UI 保证单线程）；发送命令 = 从
  `CurrentNodeId` 出发 `AppendNode`；点击历史节点 = `NavigateTo`（后续从该节点分支）。
- `SessionExporter` 只读导出：`session.json`（含全部 `EditNode`，snake_case、不转义非 ASCII）+
  节点图拷贝为 `{NodeId}.png`；不自动恢复、不抛异常导致关闭失败。

### 8.4 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（按 Z29 只跑受影响类，**无 GPU**）：`CommandParserTests` + `EditSessionTests` +
  `SessionExporterTests` → **19 通过 / 0 失败**；非 GPU 全量（排除 `Ipc*` / 集成）→ **98 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）；本步纯逻辑，无 GPU 参与。

### 8.5 项目配置变更（非冻结契约）

- `ZivAiEditor.Tests` 的 TFM 由 `net8.0` 改为 **`net8.0-windows`**，并新增对 `ZivAiEditor.App`
  的 `ProjectReference`——以便测试 App 层的 `SessionExporter`（`net8.0` 无法引用
  `net8.0-windows` 的 App）。不改变 `Contracts` / `Agent` / `Tools` / `Backend` 的依赖方向，
  不新增项目；1.1 项目表以本段为准（Tests 依赖再加 `App`）。

---

> **修订说明（Step 8 归属修正，2026-09-22）**
>
> 本段为**只增**修订，**不改动 Step 0–7 已冻结行，也不改 Step 8 段（8.1–8.5）原文**。
> 依据 Step 8 收尾复盘：`SessionExporter` 的归属错位（放 App 层）引发了连锁问题——
> 它操作的是 Agent 层的 `EditSession`（同层本该同归属）、**零平台依赖**（只用 BCL 的
> `System.IO` + `System.Text.Json`），且为测试它被迫把 `Tests` 从 `net8.0` 改为
> `net8.0-windows` 并引用 `App`，**违反 FROZEN 1.1 表的 TFM 约定**。现更正如下。

#### 8R.1 归属更正（取代 8.1 中 `ISessionExporter` / `SessionExporter` 的层）

- **更正**：`ISessionExporter` / `SessionExporter` 的归属由 **App 层**更正为 **Agent 层**。
  - 文件：`ZivAiEditor.App/SessionExporter.cs` → **`ZivAiEditor.Agent/SessionExporter.cs`**。
  - namespace：`ZivAiEditor.App` → **`ZivAiEditor.Agent`**。
  - 依据 `ARCHITECTURE.md` §2 / §3：会话 DAG 与导出是**编排 / 会话逻辑**，与 `EditSession` 同层；
    且实现无任何平台 API（无注册表 / 无 Win32 / 无对话框），不需要归 App。
- **签名与行为不变**：`ISessionExporter.ExportAsync(EditSession, string, CancellationToken)` 与
  `SessionExporter` 方法体**原样不动**；`CommandParser` / `ParseResult` / `EditSession` /
  `EditNode` 均不变。
- **`AppContext`**：仅 `using` 解析来源变化（`ZivAiEditor.Agent`），**无代码改动**——仍构造并暴露
  `ISessionExporter SessionExporter`，由 App 注入 UI（Step 9）。

#### 8R.2 1.1 项目表恢复（取代 8.5）

- **`ZivAiEditor.Tests` 的 TFM 恢复为 `net8.0`**（8.5 的 `net8.0-windows` 作废）。
- **删除 `Tests` 对 `ZivAiEditor.App` 的 `ProjectReference`**；依赖恢复为
  `Contracts` / `Agent` / `Tools` / `Backend`（+ xunit / Microsoft.NET.Test.Sdk），
  与 1.1 表 / 7.10 修订说明一致。
- 理由：`SessionExporter` 移入 Agent 后，测试经既有 `Agent` 引用即可覆盖，无需 App。
- **性质**：仅项目配置与文件归属；不改契约 / 签名 / 行为 / 依赖方向。

#### 8R.3 命令参数翻译规则（补充登记）

> 补充说明 `CommandParser` 的职责边界（Step 8 已实现，此处登记规则，不改行为）。

- **`CommandParser` 负责将命令参数翻译为工具所需的类型化字段**（写入 `EditPlan` / `EditStep`）。
- **当前规则**：
  - `/扩图 <width> <height>` → `ResolutionPolicy{Mode=Explicit, Width, Height}`
    （`QW21outpaint` 强制要求 `Resolution.Mode=Explicit`，见 FROZEN 7.5）；
  - **其他命令参数当前均为 `string`**，写入 `EditStep.Parameters`（简单 `{param}` 字符串替换，
    不做类型转换）。
- **扩展约定**：新增工具时，若其**必需字段类型化**（如 outpaint 的 `Resolution`），
  `CommandParser` 需补充对应翻译；否则命令产出的计划无法执行。

#### 8R.4 修正后验证

- `dotnet build ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（按 Z29，**无 GPU**，排除 `Ipc*` / `PlannerIntegration`）→ **98 通过 / 0 失败**
  （含 `SessionExporterTests` 3 例，经 Agent 引用覆盖）。

---

## Step 9A（日期：2026-09-22）

> **修订说明（Step 9A · CLI 入口 + 单实例 + UI 层）**
>
> 本段为 Step 9A 追加。目标：① 对外 CLI 入口（`--image` / `--prompt` / `--mask`，AOT 友好
> 手写解析）② 单实例（Mutex + Named Pipe，JSON payload）③ UI 层（聊天流 + 历史节点列表 +
> 关闭询问导出）④ `SPEC.md` §3.4 / §7 授权修订。**不改动 Step 0–8 已冻结行的既有成员**；
> **无契约变更**（`Contracts` 零新增、零修改）。UI 全部放 ZIV.AI 侧（App 层承载），
> ZIV 侧只提供一个按钮调用（实际联调留 Step 9B）。

### 9A.1 CLI 参数约定（冻结）

| 参数 | 含义 | 缺省行为 |
|---|---|---|
| `--image <path>` | 主图路径（设为会话根 `EditSession.RootImagePath`） | 空会话（可手动导入或直接 T2I） |
| `--prompt <text>` | 初始提示词（预填输入框） | 空 |
| `--mask <path>` | 遮罩路径（后置） | 空 |

- 手写解析（AOT 友好，**不用** `System.CommandLine`）；解析失败 / 未知参数 / 缺值 →
  **不退出**，用默认会话启动（无参数时行为与之前一致）。
- 类型：`ZivAiEditor.UI/LaunchOptions.cs`（`ImagePath` / `Prompt` / `MaskPath` + `Parse`），
  JSON 走源生成（`LaunchOptionsJsonContext`）。

### 9A.2 单实例协议（冻结）

- Mutex 名：`Local\ZIV.AI.SingleInstance.{sid}.{session}`（用户 SID + 会话 id）。
- Named Pipe 名同名；**单行 UTF-8 JSON payload**（snake_case：`image_path` / `prompt` /
  `mask_path`）。
- 首次启动：创建 Mutex + Pipe Server → 正常启动。
- 重复唤起：连接已有 Pipe → 发送 payload → 退出（不显示窗口）。
- 主进程收到 `PathReceived` 事件 → 激活窗口 + 加载新请求（`MainWindow.ApplyLaunchRequest`）。
- **与 Python 后端 IPC（`contracts/ipc-protocol.md`）无关**（Z23：允许 C# 不校验
  `ipc_version`；本 IPC 不参与推理传输）。
- 类型：`ZivAiEditor.App/SingleInstance.cs`（`internal`，`InternalsVisibleTo("ZivAiEditor.Tests")`）。

### 9A.3 配色资源字典（冻结）

- 文件：`ZivAiEditor.App/Themes/ZivColors.axaml`（由 `App.axaml` 合并）。
- 常量（从 ZIV `MainWindow.axaml` 抄，**不引用 ZIV 控件**）：背景 `#1A1A1A`、标题栏
  `#252525`、主文字 `#DDDDDD`、次级文字 `#AAAAAA`、按钮 hover `#33FFFFFF`、pressed
  `#22FFFFFF`、关闭 hover `#C42B1C`、关闭 pressed `#B0241A`。

### 9A.4 SPEC 授权修订（冻结）

- `SPEC.md` §3.4：UI 范围「任务卡片流」→ **「聊天流 + 历史节点列表」**（以
  `INTERACTION.md` 为准）。
- `SPEC.md` §7 评审清单原「`ZIV.sln` 未被改动；`D:\devlop\ZIV` 下无本步修改」→
  「ZIV 侧改动限 App 层（插件入口），不动共享库（`ZIV.Core` / `ZIV.Imaging`）；ZIV.AI 不
  反向依赖 `ZIV.App` / 不加载 ZIV 托管程序集（Z27 / Z28 不变）」。

### 9A.5 实现清单（冻结）

| 类 / 改动 | 文件 | 职责 |
|---|---|---|
| `LaunchOptions`（新增） | `UI/LaunchOptions.cs` | CLI 解析 + 单实例 payload；AOT 源生成 JSON |
| `SessionViewModel`（新增） | `UI/Chat/SessionViewModel.cs` | 聊天流 + 历史节点（Agent 契约驱动，无 Avalonia） |
| `SingleInstance`（新增） | `App/SingleInstance.cs` | Mutex + Named Pipe；`PathReceived` 事件 |
| `ZivColors.axaml`（新增） | `App/Themes/ZivColors.axaml` | 配色资源字典 |
| `MainWindow`（重写） | `App/MainWindow.axaml(.cs)` | 自绘 chrome + 历史列表 + 聊天流 + 关闭询问 |
| `ConfirmDialog`（新增） | `App/ConfirmDialog.axaml(.cs)` | 自绘「保存本次会话？」对话框 |
| `Program` / `App`（修改） | `App/Program.cs` / `App.axaml.cs` | CLI 解析 + 单实例接线 + UI 依赖注入 |
| `AppContext`（不变） | `App/AppContext.cs` | 已暴露 `Session` / `CommandParser` / `Executor` / `SessionExporter` |

- **UI 只经契约访问 Agent 层**：`SessionViewModel` 只用 `ICommandParser` / `IExecutor` /
  `EditSession`（Step 8 冻结）；不直接调 `IpcInferenceClient`（Z17 / ARCHITECTURE §4）。
- **关闭询问**：会话非空时弹窗；「是」→ `IStorageProvider` 选目录 →
  `ISessionExporter.ExportAsync`；「否」→ 直接关闭；弹窗 / 导出失败不阻塞关闭
  （INTERACTION §4；不自动恢复）。
- **范围外**：URL 协议注册、LLM 意图理解、`@图片N` 多图、与 ZIV 实际联调（Step 9B）。

### 9A.6 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（按 Z29 只跑受影响类，**无 GPU**）：`LaunchOptionsTests`（4）+
  `SingleInstanceTests`（2）+ `SessionViewModelTests`（4）→ **10 通过 / 0 失败**。
- 非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **108 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）；本步 UI 层不直接跑推理。

### 9A.7 Tests 依赖修订（取代 8R.2 的 TFM 部分）

- **`ZivAiEditor.Tests` 的 TFM 由 `net8.0` 改为 `net8.0-windows`**，并新增对
  `ZivAiEditor.UI` / `ZivAiEditor.App` 的 `ProjectReference`。
- **理由**：Step 9A 需测 App **平台层**逻辑（`SingleInstance`：Mutex + Named Pipe，Z4 要求
  平台 API 只在 App 层，**无法下移**）。8R.2 的「Tests 恢复 `net8.0`」是针对可下移的
  `SessionExporter`；本步的 CLI / 单实例属 App 平台层，测试必须能引用 App。
- **性质**：仅项目配置与 TFM；不改契约 / 签名 / 行为 / 依赖方向。`App` 通过
  `InternalsVisibleTo("ZivAiEditor.Tests")` 暴露 `internal SingleInstance`。

### 9A.8 收尾修正：状态栏复位 + 生成中预览接线（2026-09-22）

> 本小节为**只增**修订，**不改动 Step 0–9A 已冻结行**。修复 Step 9A UI 的两个缺陷；
> **无契约变更**（`Contracts` 零改动、`IInferenceClient` / `IProgress<T>` 签名不变），
> 不改 Python / Tool / Executor / Backend 逻辑。

- **缺陷 1（状态栏不复位）**：`MainWindow.SetBusy(false)` 只恢复控件、不重置状态文本，
  解析失败后停留「处理中…」。改为 `SetStatus(busy ? "处理中…" : "就绪")`。
- **缺陷 2（生成中预览未接通）**：`IpcInferenceClient.PreviewReceived`（Step 2.3 的
  `0x02` JPEG 帧）在生产路径无人订阅。接线方案 **C**（ARCHITECTURE §6「跨模块接线发生在
  App」）：
  - `AppContext` 订阅 `client.PreviewReceived` → 暴露 `event Action<byte[]> PreviewReceived`
    （只传 JPEG 字节；UI 不引用 `PreviewFrame` / `IpcInferenceClient`，符合 §4）；
  - `App.axaml.cs` 订阅该事件 → `Dispatcher.UIThread.Post` → `MainWindow.ShowPreview(byte[])`；
  - `MainWindow` 在 pending 气泡内挂一个 `Image`，`ShowPreview` 直接更新其 `Source`
    （不重建聊天流，避免闪烁）；
  - `ChatMessage` 新增 `IsPending`（UI 自有类型，**非契约**），标记「生成中…」气泡。
  - **未选方案 A / B**：把二进制 JPEG 塞进冻结的文本进度契约（`TaskProgress` /
    `StepProgress` / `InferenceProgress`）需改 1–3 个契约 + Backend + Tool + Executor，
    改动大且语义扭曲（preview 是独立事件，不在 `IProgress` 回调内）。
- **顺带修复**：`MainWindow.RenderChat` 每次重建前释放旧 `Bitmap`（`ReleaseBitmaps`），
  消除先前每次消息变更累积 bitmap 的泄漏。
- **验证**：`dotnet build` 0 错误 0 警告；`dotnet test`（非 GPU 全量）**109 通过 / 0 失败**
  （新增 `Submit_Marks_Pending_Bubble_While_Executing`）；真机验证生成中预览已显示。
- **提交**：`bb5593f`。

---

## Step 9C.1（日期：2026-09-22）

> **新增说明（Step 9C.1 · 图像预览窗口）**
>
> 本段为 Step 9C.1 **只增**记录。目标：为当前会话图像提供独立大图预览窗口（适配 / 滚轮
> 锚点缩放 / 左键拖拽 / 双击切换 / Esc），点击聊天流图片打开。**不改动 Step 0–9A 已冻结行**；
> **无契约变更**（`Contracts` 零新增、零修改）。UI 窗口由 App 层承载，缩放 / 平移纯逻辑
> 下沉 `ZivAiEditor.UI`。

### 9C.1.1 新增第三方依赖（冻结）

- **包**：`UVtools.AvaloniaControls` **5.0.1**（`AdvancedImageBox`：平移 / 缩放 / 光标图像框）。
- **声明**：版本写入 `src/Directory.Packages.props`（CPM）；`ZivAiEditor.App.csproj` 的
  `PackageReference` **不带版本号**；仅在 **App 层**引用（`AdvancedImageBox` 的 ControlTheme
  经 `App.axaml` 的 `StyleInclude` 合并）。
- **兼容性**：5.0.1 依赖 `Avalonia 12.1.1`，与仓库现有 `Avalonia 12.1.1` 一致；
  `dotnet restore` 无版本冲突（**不降级** Avalonia / 其他 ZIV.AI 包）。
- **裁决依据**：Step 9C.1 启动裁决「主选 `UVtools.AvaloniaControls` 的 `AdvancedImageBox`；
  若与 Avalonia 12.1.1 依赖冲突，改用 `PanAndZoom` 的 `ZoomBorder`」。实测 5.0.1 无冲突，
  故采用主选，**未使用**备选。

### 9C.1.2 新增 UI 类型（冻结 · 非契约）

| 类型 | 文件 | 归属 | 说明 |
|---|---|---|---|
| `ImageViewModel` | `ZivAiEditor.UI/Imaging/ImageViewModel.cs` | UI（net8.0，无 Avalonia） | 缩放 / 平移状态与视口 ↔ 图像坐标映射；适配比例、锚点缩放、平移钳制 |
| `ImagePreview` | `ZivAiEditor.App/Controls/ImagePreview.axaml(.cs)` | App（`Window`） | 大图预览窗口；自绘无边框 chrome（同 `MainWindow`）；以 `AdvancedImageBox` 渲染，输入驱动 `ImageViewModel` |

- **`Contracts` 零新增、零修改**：`IInferenceClient` / `IEditTool` / `IToolRegistry` /
  `IExecutor` / `IPlanner` 与 6 个模型签名**未改**。
- **`MainWindow`**：布局恢复 Step 9A 两栏（历史节点 + 聊天流）；聊天流图片新增点击 → 打开
  `ImagePreview`。**不新增契约**。

### 9C.1.3 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（按 Z29，**无 GPU**）：`ImageViewModelTests` **11 通过 / 0 失败**；
  非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **120 通过 / 0 失败**。
- **无头实测**（`Avalonia.Headless` + `UseSkia`，真实位图 1020×543，视口 800×600，模拟输入）：
  初始适配 78%；滚轮 → 94%（锚点保持）；拖拽 → 偏移钳制；双击 78% ↔ 100%；单击不变；Esc 关闭。
- **未跑 GPU 端到端**（Z29 / Z30）；本步不加载模型。

---

## Step 9C.2（日期：2026-09-23）

> **新增说明（Step 9C.2 · 抽公共 chrome + 编辑器工具栏）**
>
> 本段为 Step 9C.2 **只增**记录。目标：A 抽公共 chrome（纯重构，行为 / 视觉不变）；
> B `ImagePreview` 顶部工具栏 + 纯工具状态机（框架 / 状态，不接绘制）。**不改动 Step 0–9C.1
> 已冻结行**；**无契约变更**（`Contracts` 零新增、零修改）。子任务 B **未改**子任务 A 的
> chrome 控件（`ChromeTitleBar` / `ChromeResizeBorders` / `ChromeBehavior`）。

### 9C.2.1 新增 App 层公共 chrome 控件（冻结 · 非契约）

| 类型 / 文件 | 职责 |
|---|---|
| `Styles/ChromeStyles.axaml` | 共享 `Button.tb` / `Path.winIcon` 样式；`App.axaml` 以 `StyleInclude` 合并 |
| `Controls/ChromeTitleBar.axaml(.cs)` | `UserControl`；ShadowWrapper+ChromeRoot+TitleBar；`LeftContent` / `CenterContent` / `Body`（object?）StyledProperty（`Auto,*,Auto`：左槽 / 居中槽 / 窗口按钮，同 ZIV）；暴露部件（TitleBar / BtnMinimize / BtnMaximize / BtnClose / IconMaximize） |
| `Controls/ChromeResizeBorders.axaml(.cs)` | `UserControl`；8 条 resize Border + `WindowDecorationProperties.ElementRole` |
| `Controls/ChromeBehavior.cs` | `internal static`；`Init(Window, ChromeTitleBar)` 设装饰角色 + 最小 / 最大 / 关闭 + 最大化图标 |

- `MainWindow` / `ImagePreview` 改为直接承载 `<c:ChromeTitleBar>`（内容经 `Body`）。
- 配色：`Themes/ZivColors.axaml` **追加** `ZivCanvasBackgroundBrush` / `ZivOverlayBrush`
  （既有 Key 未改）。
- 图标：`Assets/Icons/TablerIcons.axaml`（**Tabler Icons, MIT**，与 ZIV 同款；几何编译为
  `StreamGeometry`），`App.axaml` 以 `ResourceInclude` 合并；工具栏 6 按钮使用其几何。
  不引图标库 / 新 NuGet。

### 9C.2.2 新增 UI 类型：工具状态机（冻结 · 非契约）

| 类型 / 文件 | 归属 | 说明 |
|---|---|---|
| `ToolMode`（枚举） | `ZivAiEditor.UI/Editing/ToolMode.cs` | `None` / `Crop` / `MaskBrush` / `Eraser` |
| `ToolStateMachine` | `ZivAiEditor.UI/Editing/ToolStateMachine.cs` | 纯逻辑（无 Avalonia）；`CurrentTool`、`HasImage`、`CanUndo`、`CanClearMask`、`CanCrop => HasImage`；`SetTool` / `NotifyImageChanged` / `NotifyUndoStackChanged` / `NotifyMaskChanged`；`event StateChanged` |
| `EditorToolbar` | `ZivAiEditor.App/Controls/EditorToolbar.axaml(.cs)` | 6 按钮工具栏；`Attach(ToolStateMachine)`；`ClearMaskRequested` / `UndoRequested` / `ResetViewRequested` 事件 |

- **ResetView 不进状态机**（一次性动作，由工具栏事件承载）。
- **`Contracts` 零新增、零修改**：`IInferenceClient` / `IEditTool` / `IToolRegistry` /
  `IExecutor` / `IPlanner` 与 6 个模型签名**未改**。

### 9C.2.3 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`ToolStateMachineTests` **9 通过 / 0 失败**；
  非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **129 通过 / 0 失败**。
- **外部无头探针**（`Avalonia.Headless` + `UseSkia`）→ **ALL PASS**（chrome 角色 / 布局 /
  标题 / 最大化图标；工具栏 6 按钮 / 初始禁用 / 工具点击→状态+IsChecked+光标 / 重置视图→适配）。
- **未跑 GPU 端到端**（Z29 / Z30）；本步不加载模型。

---

## Step 9C.2-C（日期：2026-09-23）

> **新增说明（Step 9C.2-C · 划像对比）**
>
> 本段为 Step 9C.2-C **只增**记录。目标：预览窗口「当前图 vs 父图」的划像对比（垂直分割线
> 拖动）。**不改动 Step 0–9C.2 已冻结行**；**无契约变更**（`Contracts` 零新增、零修改）。
> 本段为**纯追加**（位于文件尾部），未修改 9C.2-A / 9C.2-B 的任何既有行。

### 9C.2-C.1 新增 UI 类型（冻结 · 非契约）

| 类型 / 文件 | 归属 | 说明 |
|---|---|---|
| `CompareState` | `ZivAiEditor.UI/Editing/CompareState.cs` | 纯逻辑（无 Avalonia）；`CanCompare` / `IsCompareMode` / `Divider`（钳制 `[0,1]`）/ `SetCanCompare` / `SetCompareMode` / `Toggle` / `SetDivider` / `Reset`；`event StateChanged`。**方案 A**：对比为独立开关，不占 `ToolMode`、不与工具互斥 |
| `CompareOverlay` | `ZivAiEditor.App/Controls/CompareOverlay.axaml(.cs)` | `UserControl`；自绘（`Render`）父图（左）+ 分割线；外扩区域以画布背景色填充（**简化对齐**） |
| `ImagePreview.Compare.cs` | `ZivAiEditor.App/Controls/ImagePreview.Compare.cs` | `ImagePreview` 的 `partial` 半边：父图加载 / 释放、`SetCompareSource`、分割线命中 / 拖动（**为满足 Z8 拆分**） |

- **`ImagePreview`**：新增 `SetCompareSource(string?)`（**独立于 `LoadImage`**）与
  `CompareState`；对比按钮（标题栏右侧槽）点击 → `Toggle`；Esc **先退出对比**、再次 Esc 关窗。
- **`SessionViewModel`**：新增只读 `GetParentImagePath(string?)`（父节点输出 / 根图 / `null`）。
- **`MainWindow`**：打开预览时传入 `GetParentImagePath` 结果。
- **`Contracts` 零新增、零修改**：`IInferenceClient` / `IEditTool` / `IToolRegistry` /
  `IExecutor` / `IPlanner` 与 6 个模型签名**未改**；`contracts/ipc-protocol.md` **未改**。

### 9C.2-C.2 前置调查结论（冻结 · 事实）

- **outpaint 几何信息当前不可得**：`EditNode` / `ToolResult.Metadata` / `InferenceResultDetail` /
  IPC `result` 帧（§3.2 / §3.5）均**无**父图 offset / 尺寸 / anchor / 画布尺寸字段。
- 故本步采用**简化对齐**（父图居中 + 背景填充）；精确对齐需扩展冻结结构，
  登记为遗留项（`DOC/OPTIMIZATION.md` §7.5），**本步不改**。

### 9C.2-C.3 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`CompareStateTests` **13 通过 / 0 失败**；
  非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **145 通过 / 0 失败**。
- **外部无头探针**（`Avalonia.Headless` + `UseSkia`）→ **ALL PASS**（根图禁用 / 非根启用 /
  进入退出 / 分割线钳制 / 切工具不退出 / Esc 先退对比 / 渲染：背景填充 + 父图内容）。
- **未跑 GPU 端到端**（Z29 / Z30）；本步不加载模型。

---

## Step 9C.3（日期：2026-09-23）

> **新增说明（Step 9C.3 · 图片导入行）**
>
> 本段为 Step 9C.3 **只增**记录。目标：主窗口底部输入区上方新增「图片导入 / 拖入」条
> （虚线 `+` 框 / 多选 picker / 拖入 / 悬停 `×` 移除 / 单张设为主图）。**不改动 Step 0–9C.2-C
> 已冻结行**；**无契约变更**（`Contracts` 零新增、零修改）。本段为**纯追加**（文件尾部）。

### 9C.3.1 新增 UI 类型（冻结 · 非契约）

| 类型 / 文件 | 归属 | 说明 |
|---|---|---|
| `ImageImportList` | `ZivAiEditor.UI/Editing/ImageImportList.cs` | 纯逻辑（无 Avalonia）；有序去重路径列表（`OrdinalIgnoreCase`）、`AddRange`（一批一次 `Changed`）、`RemoveAt`、`Clear`；`Changed` 携带 `CountBefore` / `CountAfter` |
| `ImageImportBar` | `ZivAiEditor.App/Controls/ImageImportBar.axaml(.cs)` | `UserControl`；虚线缩略图 + 末尾虚线 `+` 框；缩略图后台解码 + `Dispose`；悬停 `×`；`AddRequested` / `ImagesChanged` / `AddFiles` / `RemoveAt` / `Dispose` |
| `MainWindow.Import.cs` | `ZivAiEditor.App/MainWindow.Import.cs` | `MainWindow` 的 `partial` 半边：picker / Window 级拖拽 / 提升规则（**为满足 Z8 拆分**） |

### 9C.3.2 内部方法（冻结 · 非契约）

| 方法 | 文件 | 说明 |
|---|---|---|
| `EditSession.ResetToRoot(string)` | `ZivAiEditor.Agent/EditSession.cs` | `SetRoot` + `Nodes.Clear` + `CurrentNodeId = null`（变更起始图即重置会话） |
| `SessionViewModel.SetRootImage(string?)` | `ZivAiEditor.UI/Chat/SessionViewModel.cs` | `ResetToRoot` + `RefreshHistory` + `RebuildContext`；空 = no-op |

- **提升规则**：仅 `CountBefore == 0 && CountAfter == 1`（由添加把列表从空变为恰好一张）设主图；
  移除**不**重新提升（避免 2→1 移除静默重置会话）；移除到 0 **不改变**已生效 root。
- **`Contracts` 零新增、零修改**：`IInferenceClient` / `IEditTool` / `IToolRegistry` /
  `IExecutor` / `IPlanner` 与 6 个模型签名**未改**；`contracts/ipc-protocol.md` **未改**。

### 9C.3.3 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`ImageImportListTests` **11 通过 / 0 失败**；
  非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **158 通过 / 0 失败**。
- **外部无头探针**（`Avalonia.Headless` + `UseSkia`，真实 `MainWindow`）→ **ALL PASS**（初始单框 /
  导入追加 / 单张设主图 / 多张不改主图 / 2→1 移除不重置 / 0 时 root 保留）。
- **未跑 GPU 端到端**（Z29 / Z30）；本步不加载模型。

### 9C.3.4 收尾修订：网页聊天式附件条（2026-09-23）

> 本小节为 Step 9C.3 的**收尾修订说明**（用户反馈「不够易用」）。**不改动 9C.3.1–9C.3.3 既有行**，
> 仅在此追加；以本小节为准。

- **改了什么**：`ImageImportBar` **去掉常驻虚线 `+` 框**，改为**默认隐藏**、有附件时才在输入框
  **上方弹出**（`PART_Scroll.IsVisible = Count > 0`）；新增 `HasImages` / `Clear`，移除
  `AddRequested` 事件。`MainWindow` 输入行新增「图片」按钮 `PART_BtnAddImage`
  （Tabler `IconPhoto`）→ 打开文件选择器；拖入仍走 Window 级 `DragDrop`。
- **为什么**：对齐网页 DeepSeek / ChatGPT 的附件交互（干净默认 + 拖入即弹出），提升易用性。
- **影响哪些接口**：仅 App 层 UI 与 `ImageImportBar` 成员（`AddRequested` 移除、`HasImages` /
  `Clear` 新增）；**`ImageImportList` 纯逻辑不变**；**`Contracts` 零变更**；
  `SessionViewModel.SetRootImage` / `EditSession.ResetToRoot` 不变。
- **测试**：`dotnet build` 0/0；非 GPU **158 通过 / 0 失败**；无头探针更新为「初始隐藏 /
  导入弹出 / 移除到空收起」→ **ALL PASS**。

### 9C.3.5 收尾修订 2：标题栏侧栏按钮 + 输入区圆角化（2026-09-23）

> 本小节为 Step 9C.3 的**第二次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.4 既有行**，
> 仅在此追加；以本小节为准。

- **改了什么**：① 标题栏左槽新增**侧栏切换按钮** `PART_BtnToggleSidebar`（Tabler `IconSidebar`），
  点击切换历史节点栏 `PART_HistoryPane.IsVisible`；② 底部输入区改为**居中、`MaxWidth=760`、
  不拉通**，`PART_Input` 加 `CornerRadius="8"`（与窗体 `CornerRadius=8` 统一）；③ 发送 / 附件
  按钮改 **opencode 风格**（`Button.ocSend` 灰底圆角 + `IconArrowUp`；`Button.oc` 透明圆角）。
- **为什么**：对齐 opencode / DeepSeek 的输入区与侧栏交互。
- **影响哪些接口**：仅 App 层 UI 样式与 `MainWindow.axaml(.cs)`；新增 `IconSidebar` /
  `IconArrowUp`（Tabler, MIT）与 `Button.oc` / `Button.ocSend` / `Path.sendIcon` 样式；
  **`Contracts` 零变更**；`ImageImportList` / `SessionViewModel` / `EditSession` 不变。
- **测试**：`dotnet build` 0/0；非 GPU **158 通过 / 0 失败**；无头探针 **25 项 ALL PASS**。

### 9C.3.6 收尾修订 3：侧栏按钮移到右上角（2026-09-23）

> 本小节为 Step 9C.3 的**第三次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.5 既有行**，
> 仅在此追加；以本小节为准。

- **改了什么**：侧栏切换按钮 `PART_BtnToggleSidebar` 从标题栏**左槽**（`LeftContent`）移到
  **右侧槽**（`RightContent`，渲染在最小化 / 最大化 / 关闭按钮之前）；左槽恢复为仅标题文字。
- **为什么**：用户要求按钮位于**右上角**（与窗口按钮同侧）。
- **影响哪些接口**：仅 `MainWindow.axaml` 布局位置；控件名 / 行为不变；**`Contracts` 零变更**。
- **测试**：`dotnet build` 0/0；无头探针 **25 项 ALL PASS**（侧栏按钮仍可解析并切换历史栏）。

### 9C.3.7 收尾修订 4：侧栏按钮 User 角色修复（2026-09-23）

> 本小节为 Step 9C.3 的**第四次收尾修订说明**。**不改动 9C.3.1–9C.3.6 既有行**，仅在此追加。

- **改了什么**：`MainWindow` 对标题栏内的侧栏按钮 `PART_BtnToggleSidebar` 设
  `WindowDecorationProperties.SetElementRole(..., WindowDecorationsElementRole.User)`。
- **为什么**：按钮在自绘标题栏（caption）区域内，OS 命中测试会吞掉点击（当作拖动/双击最大化）；
  必须标记为 `User` 客户内容才可点击（与 `EditorToolbar` / `ImagePreview` 一致）。
- **影响哪些接口**：仅 `MainWindow.axaml.cs` 一行角色设置；**`Contracts` 零变更**。
- **测试**：`dotnet build` 0/0；无头探针 **26 项 ALL PASS**（新增 `GetElementRole == User` 断言）。

### 9C.3.8 收尾修订 5：历史栏全高、输入区只在右列（2026-09-23）

> 本小节为 Step 9C.3 的**第五次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.7 既有行**。

- **改了什么**：`MainWindow.axaml` 的 `Body` 改为 **`Grid ColumnDefinitions="Auto,*"`**——
  `PART_HistoryPane` 占左列**全高**（标题栏到窗口底部）；右列 `DockPanel[Bottom 输入区 + 聊天]`。
  输入区不再横跨整窗，而是居中于右侧主区（`MaxWidth=760`）。
- **为什么**：用户要求历史栏**拉通到底部**、输入区**不拉通**（只在主区）。
- **影响哪些接口**：仅 `MainWindow.axaml` 布局；控件名 / 行为不变；**`Contracts` 零变更**。
- **测试**：`dotnet build` 0/0；无头探针 **26 项 ALL PASS**。

### 9C.3.9 收尾修订 6：输入框宽度 75% + 高度 2 行（2026-09-23）

> 本小节为 Step 9C.3 的**第六次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.8 既有行**。

- **改了什么**：输入区 `Grid MaxWidth` **760 → 570**（≈75%）；`PART_Input` `MinHeight`
  **40 → 64**（约 2 行高度）、`VerticalContentAlignment="Top"`。
- **为什么**：用户要求输入框更短（75%）且高度为 2 行。
- **影响哪些接口**：仅 `MainWindow.axaml` 尺寸；**`Contracts` 零变更**。
- **测试**：`dotnet build` 0/0。

### 9C.3.10 收尾修订 7：输入框容器化 + 状态移入聊天流（2026-09-23）

> 本小节为 Step 9C.3 的**第七次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.9 既有行**。

- **改了什么**：① 输入区改为**单层圆角容器** `Border`（`PART_InputBox`，`#1E1E1E` + `#333` 边框 +
  `CornerRadius=12`），内部上为无边框透明 `PART_Input`、下为工具行（`+` 添加图片 `Button.oc` /
  发送 `Button.ocSend`）；**去掉模型徽标**与底部固定状态。② 状态改为**聊天流末尾的「系统」行**
  （`MainWindow.Status.cs` 的 `BuildStatusRow` / `SetStatus`），随聊天滚动。
  ③ 新增 `IconSparkle` / `IconChevronDown` 与 `Path.modelIcon` / `Path.chevronIcon`（暂未使用）。
- **为什么**：对齐 opencode / DeepSeek 的输入框容器风格；状态归入聊天流。
- **影响哪些接口**：新增 App 层 `MainWindow.Status.cs`（partial）；`Contracts` 零变更。
- **测试**：`dotnet build` 0/0；非 GPU **158 通过 / 0 失败**；无头探针 **ALL PASS**
  （`PART_InputBox` 圆角=12 / 发送 `ocSend` / 侧栏切换 + `User` 角色）；`MainWindow.axaml.cs` 558 行。

### 9C.3.11 收尾修订 8：去输入框内框 / 按钮缩至 60%（2026-09-23）

> 本小节为 Step 9C.3 的**第八次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.10 既有行**。

- **改了什么**：新增 `TextBox.plainInput` 样式（含 `/template/ Border#PART_BorderElement` 的
  `:pointerover` / `:focus` 覆盖）使输入框**透明无边框**，`PART_Input` 改用之；输入区按钮
  `Button.oc` / `Button.ocSend` 尺寸 **40 → 24**（≈60%）、圆角 **8 → 6**，`sendIcon` 16 → 11。
- **为什么**：用户要求去掉 TextBox 的黑底 / 内框，并缩小按钮至 60%。
- **影响哪些接口**：仅 `ChromeStyles.axaml` / `MainWindow.axaml`；**`Contracts` 零变更**。
- **测试**：`dotnet build` 0/0；无头探针 **ALL PASS**。

### 9C.3.12 收尾修订 9：分辨率选择器 + 状态并入生成中 + 对比对齐（2026-09-23）

> 本小节为 Step 9C.3 的**第九次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.11 既有行**。

- **改了什么**：① 新增 App 层 `Controls/ResolutionPicker.axaml(.cs)`（三档 + 自定义，读
  `ModelProfile.TierSides`），放在输入框 `+` 右边；`MainWindow` 注入 `IModelProfileRegistry`
  （`AppContext.ModelProfiles`）。② 删除聊天流末尾「系统」状态行，状态并入「生成中」消息
  （`MainWindow.Status.cs` 改为更新 pending 文本）。③ `CompareOverlay` 父图改为**铺满当前图矩形**
  （对齐「重缩放后尺寸 = 生成尺寸」）。
- **为什么**：用户要求分辨率三档 + 自定义 UI、状态并入生成中、对比按重缩放后尺寸对齐。
- **影响哪些接口**：新增 App 层 `ResolutionPicker`（用已有 `ResolutionTier` / `ModelProfile` /
  `ResolutionResolver` 契约，**未改契约**）；`MainWindow` 构造新增可选参数 `IModelProfileRegistry`；
  `Contracts` 零变更。
- **遗留**：选择器**暂未接入编辑请求**；`自定义` 暂不弹宽高输入。
- **测试**：`dotnet build` 0/0；非 GPU **158 通过**；无头探针 **ALL PASS**。

### 9C.3.13 收尾修订 10：完成耗时 + 尺寸信息（2026-09-23）

> 本小节为 Step 9C.3 的**第十次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.12 既有行**。

- **改了什么**：① `SessionViewModel.SubmitAsync` 完成消息改为 `"{秒:F1}秒 完成"`（`Stopwatch`）；
  ② `ImagePreview` 右下角新增**图片尺寸** badge（`PART_SizeBadge`，与右上角缩放 badge 同款式）；
  ③ 对比模式左下角新增**原图尺寸** badge（`PART_CompareInfo`，父图解码后显示）。
- **为什么**：用户要求展示生成耗时与图片 / 原图尺寸。
- **影响哪些接口**：仅 UI 层（`SessionViewModel` 文本、`ImagePreview.axaml(.cs)` / `.Compare.cs`）；
  **`Contracts` 零变更**。
- **测试**：`dotnet build` 0/0；非 GPU **158 通过**；无头探针 **ALL PASS**。

### 9C.3.14 收尾修订 11：分辨率选择生效 + 去图标（2026-09-23）

> 本小节为 Step 9C.3 的**第十一次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.13 既有行**。

- **改了什么**：① `SessionViewModel` 新增 `ResolutionPolicy? Resolution`（UI 选择），`SubmitAsync`
  在 plan 无自带分辨率时用它重建 `EditPlan.Resolution`；`MainWindow` 接线
  `ResolutionPicker.SelectionChanged → ResolutionResolver.FromTier(...)`（`Custom` → `null`）。
  ② `ResolutionPicker.axaml` 去掉 sparkle 图标，chevron 缩到 10×10。
- **为什么**：用户要求下拉选择真正生效、去掉丑/大/未对齐的图标。
- **影响哪些接口**：`SessionViewModel` 新增公开属性 `Resolution`（UI 层）；**`Contracts` 零变更**；
  `EditPlan` 未改（仅按现有字段重建）。
- **遗留**：`Custom` 档仍无宽高输入。
- **测试**：`dotnet build` 0/0；非 GPU **158 通过**；无头探针 **ALL PASS**。

### 9C.3.15 收尾修订 12：修正下拉下箭头 + 恢复 sparkle（2026-09-23）

> 本小节为 Step 9C.3 的**第十二次收尾修订说明**（用户澄清）。**不改动 9C.3.1–9C.3.14 既有行**。

- **改了什么**：`IconSparkle` 换回 Tabler `sparkles` 几何并恢复 `ResolutionPicker` 的 sparkle；
  `Path.chevronIcon` 调小调细（11px / stroke 1.1 / 圆头 / 居中），去掉 chevron 内联尺寸。
- **为什么**：用户澄清「丑/大/未对齐」指下拉的**下箭头**，sparkle 应保留（对齐参考图）。
- **影响哪些接口**：仅图标资源与样式；**`Contracts` 零变更**。
- **测试**：`dotnet build` 0/0。

### 9C.3.16 收尾修订 13：下箭头缩至 65% + 下移 4px（2026-09-23）

> 本小节为 Step 9C.3 的**第十三次收尾修订说明**（用户裁决）。**不改动 9C.3.1–9C.3.15 既有行**。

- **改了什么**：`Path.chevronIcon` **11 → 7**（≈65%）；`ResolutionPicker` 的 chevron 加
  `TranslateTransform Y="4"` 下移。
- **踩坑**：`RenderTransform="translate(0,4)"` 内联字符串**运行时崩溃**（退出码 `0xE0434352`），
  改用**元素式** `<TranslateTransform Y="4"/>`。
- **影响哪些接口**：仅样式 / `ResolutionPicker.axaml`；**`Contracts` 零变更**。
- **测试**：`dotnet build` 0/0；exe 启动正常。

---

## Step 9C.3-R（日期：2026-09-23）

> **修订说明（Step 9C.3-R · 清理 + 字段统一）**
>
> 本段为 Step 9C.3-R **只增**记录。目标：修复 9C.1–9C.3 期间引入的分层耦合与字段一致性问题
> （纯重构 + 注释 + 少量下沉，不改行为）。**不改动 Step 0–9C.3 已冻结行**；本段为**纯追加**
> （文件尾部）。唯一契约相关变更见 V3（`ICommandParser` **新增重载**，不破坏既有签名）。

### 9C.3-R.V2 会话 DAG 查询下沉（冻结 · 非契约）

- **新增**：`EditSession.GetParentImagePath(string?)` / `EditSession.GetPathToCurrent()` /
  `EditSession.GetDepth(EditNode?)`（`ZivAiEditor.Agent/EditSession.cs`）。语义与原
  `SessionViewModel` 私有实现一致，仅**新增方法**，不改 `EditSession` 既有成员。
- **修改**：`SessionViewModel` 删除本地 DAG 遍历，改为委托 `EditSession`；`GetParentImagePath`
  保留为薄委托（App 调用点不变）。
- **不改**：`SessionViewModel` 持有 `EditSession` 的现状（V1 硬违规延后 9C.5，本步不动）。

### 9C.3-R.V3 `ICommandParser` 新增重载（冻结 · 契约新增）

> **2026-09-23，用户裁决授权：`ICommandParser` 新增 `ParseAsync(input, session, resolution, ct)`
> 重载（新增，不破坏既有签名）。**

- **改了什么**：`ZivAiEditor.Agent/CommandParser.cs` 的 `ICommandParser` 新增
  `Task<ParseResult> ParseAsync(string input, EditSession session, ResolutionPolicy? resolution, CancellationToken ct = default)`；
  `CommandParser` 实现之：解析后在产出的 `EditPlan` 上设 `Resolution = resolution`（当 plan
  无自带分辨率时；parser 自带分辨率——如 `/扩图` 显式宽高——优先）。
- **为什么**：`SessionViewModel.SubmitAsync`（UI）原先为注入 `Resolution` 而**重建整个
  `EditPlan`**；下沉到 Agent 后 UI 只传参。
- **不改 8.1 既有行**：原单参重载 `ParseAsync(input, session, ct)` **保留不动**，内部委托新重载
  （传 `null`）。**`Contracts` 项目零变更**。
- **影响哪些接口**：仅 Agent 层 `ICommandParser`（新增重载）；`EditPlan` 字段未改。
- **测试**：`CommandParserTests` 新增 4 例（注入生效 / parser 分辨率优先 / null 不变 / 旧重载不变）。

### 9C.3-R.V4 App 装配统一（冻结 · 非契约）

- **改了什么**：删除 `MainWindow` 字段默认值 `new ModelProfileRegistry()`；构造参数
  `IModelProfileRegistry? modelProfiles = null` 改为**必填**（`IModelProfileRegistry modelProfiles`，
  置于 `ISessionExporter` 之后、`LaunchOptions?` 之前）；`App.axaml.cs` 调用点相应调整参数顺序。
- **为什么**：消除与 `AppContext` 的重复构造（单一装配来源）。

### 9C.3-R.6 删除死状态写入（冻结 · 非契约）

- **改了什么**：`MainWindow.SetBusy` 删除非忙分支的 `SetStatus("就绪")`（`SetStatus` 只写
  pending 气泡，非忙时无气泡 → 原调用为 no-op）。

### 9C.3-R.7 导入变更事件命名统一（冻结 · 非契约）

- **改了什么**：`ImageImportList.Changed` **重命名**为 `ImageImportList.ImagesChanged`，与
  `ImageImportBar.ImagesChanged` 统一。理由：控件是 App 面向的接缝，`ImagesChanged` 自描述、
  调用点可读；数据层同步同名消除歧义。
- **影响**：`ImageImportBar` 订阅点、`ImageImportListTests`（8 处订阅）同步更新。

### 9C.3-R.4 / #5 / #9 / #10 / #2 注释与文档（冻结 · 非契约）

- **#4**：为 `ResolutionPolicy.Width/Height`（请求目标）、`AspectPreset.Width/Height`（比例预设）、
  `InferenceResultDetail.Width/Height`（后端实际输出）、`ImageViewModel.ImageWidth/ImageHeight`
  （源图像素）、`MaskSpec.Width/Height`（遮罩原始像素）补 XML 注释，**不改名**。
- **#5**：`ResolutionPolicy.MaxPixels` 注明权威来源为 `ModelProfile.MaxPixels`；`ModelProfile`
  的 `NativeSide` / `SafeMaxSide` / `MinSide` / `MultipleOf` / `Presets` 注明「能力元数据，
  当前无生产消费者」。
- **#9**：`InferenceProgressDetail` 类级注释注明「仅 Backend 诊断 / 事件富集用；契约边界传
  `InferenceProgress`；两者是同一帧的两种投影」。
- **#10**：`CompareOverlay` 类注释与当前实现（父图**铺满当前图矩形**）同步；不改
  `ACCEPTANCE.md` / `DEVLOG.md` 既有行。**9C.2C.9 验收描述与实际实现不符，已记入遗留**
  （不实现「精确对齐」，属 9C.3-G）。
- **#2**：`InferenceResultDetail.DurationMs`（后端采样 + 解码）、`ToolResult.Duration`（一次
  IPC 提交，含惰性加载与排队）、`SessionViewModel.SubmitAsync` 的 `Stopwatch`（点击到气泡替换
  的端到端墙钟）三处加注释，说明各自作用域，**不互相校验**。

### 9C.3-R 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`EditSessionTests` / `CommandParserTests` /
  `SessionViewModelTests` / `ImageImportListTests` **48 通过 / 0 失败**；非 GPU 全量
  （排除 `Ipc*` / `PlannerIntegration`）→ **169 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）；本步不加载模型、不启动 Python、不占 GPU。

### 9C.3-R 遗留项（冻结）

- **9C.2C.9 验收描述与实现不符**：原描述「居中 + 背景填充」，实现为「父图铺满当前图矩形」；
  本步已同步代码注释，**不改** `ACCEPTANCE.md` / `DEVLOG.md` 既有行。精确对齐（outpaint 几何
  补齐）属 **9C.3-G**，另立步。
- **`CompareOverlay.BackgroundFill` 现为死状态**（`Render` 不再读取）：保留属性以避免改动
  XAML 绑定，已在注释标明当前未使用；后续如需背景填充设计再恢复。
- **V1（`SessionViewModel` 直接持有 `EditSession`）仍未解**：延后至 9C.5，本步不动。
- **`SessionViewModel` 仍直接读 `_session.CurrentNodeId` / `RootImagePath` / `GetHistory()`**：
  属 V1 同一违规范围（简单属性 / 列表读取，非遍历），随 9C.5 一并处理。

---

## Step 9C.5（日期：2026-09-23）

> **修订说明（Step 9C.5 · 架构底座重构：会话接口抽象到 Contracts）**
>
> 本段为 Step 9C.5 **只增**记录。目标：修复 V1 硬违规（UI 直接持有 Agent 实现类
> `EditSession` / `EditNode`），把会话接口抽象到 `Contracts`，收窄 UI 对 Agent 的依赖。
> **纯重构，行为不变**。**不改动 Step 0–9C.3-R 已冻结行**；本段为**纯追加**（文件尾部）。

### 9C.5.1 新增契约（冻结）

| 类型 | 文件 | 说明 |
|---|---|---|
| `IEditNode`（接口） | `ZivAiEditor.Contracts/Planning/IEditNode.cs` | `NodeId` / `ParentNodeId?` / `ImagePath` / `Command` / `CreatedAt`（`EditNode` 的只读投影，**不补几何**） |
| `IEditSession`（接口） | `ZivAiEditor.Contracts/Planning/IEditSession.cs` | **只读**：`SessionId` / `RootImagePath?` / `CurrentNodeId?` / `GetCurrentImagePath()` / `GetHistory()` / `GetParentImagePath(string?)` / `GetPathToCurrent()` / `GetDepth(IEditNode?)`；**不含** `SetRoot` / `ResetToRoot` / `AppendNode` / `NavigateTo` |
| `IEditSessionWriter`（接口） | `ZivAiEditor.Contracts/Planning/IEditSessionWriter.cs` | **写**：`SetRoot(string)` / `ResetToRoot(string)` / `AppendNode(string?, string, string) → IEditNode` / `NavigateTo(string) → bool` |

- **`GetCurrentImagePath()` 归入只读接口**：`ICommandParser` 解析时以当前工作图为 plan 源，
  故只读视图必须暴露它（任务原始成员清单遗漏，已补入）。
- **`Contracts` 新增接口**（既有成员零修改）；`IEditSession` / `IEditSessionWriter` 分离，
  使只读消费者（对比叠加 / 未来遮罩）不能改会话。

### 9C.5.2 `EditSession` / `EditNode` 实现契约（冻结）

- `EditNode` → `public sealed class EditNode : IEditNode`（字段不变）。
- `EditSession` → `public sealed class EditSession : IEditSession, IEditSessionWriter`。
- **签名变化（因接口实现需精确匹配返回类型）**：
  - `GetHistory()` 返回类型 `IReadOnlyList<EditNode>` → `IReadOnlyList<IEditNode>`
  - `GetPathToCurrent()` 返回类型 `IReadOnlyList<EditNode>` → `IReadOnlyList<IEditNode>`
  - `GetDepth(EditNode?)` 参数类型 → `GetDepth(IEditNode?)`
  - `AppendNode(...)` 返回类型 `EditNode` → `IEditNode`
- 写入方法（`SetRoot` / `ResetToRoot` / `AppendNode` / `NavigateTo`）**保留在 `EditSession` 上**。

### 9C.5.3 `ICommandParser` 签名修订（冻结 · 既有签名变更，已授权）

> **修订说明**：`ICommandParser.ParseAsync` 的 `session` 参数类型由 **`EditSession`（Agent 实现类）
> 改为 `IEditSession`（Contracts 接口）**——两个重载同步。**不改动 8.1 既有行**（原文保留），
> 以本小节为准。
>
> - **为什么**：`SessionViewModel`（UI）现持有 `IEditSession`，无法再传具体 `EditSession`；
>   且 UI 不应引用 Agent 实现类（V1）。参数收窄到接口后，UI 只依赖 Contracts 抽象。
> - **兼容性**：`CommandParserTests` 传入 `EditSession` 实例，隐式转 `IEditSession`，无需改测试；
>   实现内部 `session.GetCurrentImagePath()` 经接口调用。

### 9C.5.4 UI 依赖方向（冻结 · 未收窄）

- **UI 对 Agent 的 `ProjectReference` 保留**：UI 仍使用 **`ICommandParser`**（Agent 层接口，
>   FROZEN 8.1 冻结），删引用会编译失败。故 `ZivAiEditor.UI.csproj` **未改**。
- **V1 已解**：UI 不再引用 Agent **实现类**（`EditSession` / `EditNode` 在 UI 内零引用）；
  `SessionViewModel` 字段 / 构造 / `HistoryItem.Node` 全部改用 Contracts 接口。
- **后续建议**（另立步 / 需授权）：若要把 UI 依赖彻底收窄为「仅 Contracts」，需将
  `ICommandParser` 上提到 Contracts——属契约搬迁，超出本步范围。

### 9C.5.5 清理与遗留（冻结）

- **`CompareOverlay.BackgroundFill` 未删除**：`ImagePreview.axaml:58` 存在 XAML 绑定引用，
  按任务分支「有绑定 → 报告后不删」，保留字段（`Render` 已不读取，属死状态）。
- **既有文档不一致（报告，不改）**：FROZEN 8.1 记 `ISessionExporter` / `SessionExporter` 位于
  `ZivAiEditor.App/SessionExporter.cs`，**实际位于 `ZivAiEditor.Agent/SessionExporter.cs`**；
  本步未改该既有行，仅登记。

### 9C.5.6 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`EditSessionTests` / `SessionViewModelTests` /
  `CommandParserTests` / `SessionExporterTests` **41 通过 / 0 失败**；非 GPU 全量
  （排除 `Ipc*` / `PlannerIntegration`）→ **170 通过 / 0 失败**（9C.3-R 基线 169，本步 +1：
  `EditSessionTests` 接口一致性用例）。
- **未跑 GPU 端到端**（Z29 / Z30）；本步不加载模型、不启动 Python、不占 GPU。

---

## Step 9C.5 收尾（日期：2026-09-23）

> **修订说明（2026-09-23 · Step 9C.5 用户授权）**
>
> 用户明确授权：`ICommandParser` 的 `session` 参数类型由 `EditSession` 收窄为
> `IEditSession`（Contracts 层只读接口）。原因：Step 9C.5 修复 V1（UI 直接持有 Agent
> 实现类）后，UI 仅持 `IEditSession`，`ParseAsync` 必须同步接 `IEditSession`，否则
> 无法编译。性质：改冻结签名（FROZEN 8.1），**已获授权**。

同时确认以下 3 项**不改**：

- **`IEditSessionWriter`：保留**（`NavigateTo` / `ResetToRoot` / `SetRoot` 为用户动作，
  经核查为真 UI 主动变更，非 UI 代 Agent 编排）
- **`GetCurrentImagePath()` 进 `IEditSession`：非新增 API**（Step 8 起即为 `EditSession`
  公开方法），仅暴露到接口
- **UI 保留 Agent `ProjectReference`**：因 `ICommandParser` 仍在 Agent 层；彻底收窄需
  将 `ICommandParser` 上提 Contracts，另立步

**遗留（9C.5-B，不阻塞）**：`SessionViewModel.AppendNode`（执行成功回写节点）属 UI 代
Agent 编排，应下沉至 Agent；涉及 `IExecutor` 设计变更，单独立步。

---

## Step 9C.6（日期：2026-09-23）

> **修订说明（Step 9C.6 · 根节点实体化：导入原图成为 DAG 首个节点）**
>
> 本段为 Step 9C.6 **只增**记录。目标：让「导入的原图」成为会话 DAG 的第一个 `EditNode`
> （`ParentNodeId = null`），使历史列表从原图开始、用户可回到原图。**底座级数据结构改动，
> 编辑流程行为保持兼容**。**不改动 Step 0–9C.5 已冻结行**；本段为**纯追加**（文件尾部）。

### 9C.6.1 `EditSession` 行为语义变更（冻结 · 非契约）

> **接口签名不变**（`IEditSession` / `IEditSessionWriter` 零修改，见 9C.6.2）；以下为**实现类
> `EditSession`** 的**行为语义**变化，属既有成员的新语义，记录备案。

| 成员 | 变更前语义 | 变更后语义（9C.6） |
|---|---|---|
| `SetRoot(string)` | 仅赋值 `RootImagePath`，不建节点、不清 DAG | **等价 `ResetToRoot`**：清空 `Nodes` + `CurrentNodeId`，建 root 节点并设为当前 |
| `ResetToRoot(string)` | 清空 `Nodes` + `CurrentNodeId`，**不建节点** | 清空后**建 root 节点**（`ParentNodeId = null`、`Command = "原图"`），`CurrentNodeId = root.NodeId` |
| `RootImagePath` | `{ get; set; }` 独立字段 | **派生只读** `=> _rootNode?.ImagePath`（无 public set） |
| `GetHistory()` | 只含编辑节点（不含原图） | **含 root 节点，且 root 恒排第一** |
| `GetCurrentImagePath()` | 无 current 时回退 `RootImagePath` | current 节点的 `ImagePath`；无 current → `null`（root 已是节点） |
| `GetParentImagePath(string?)` | 匹配 `ParentNodeId == null` 节点 → 返回 `RootImagePath` | 匹配编辑节点 → 父节点 `ImagePath`；匹配 **root 节点** / 不匹配 → `null` |
| `GetPathToCurrent()` | root 不在路径中 | current=root → `[root]`；否则 `[root,…,current]`；无 current → 空 |
| `GetDepth(IEditNode?)` | 直接子节点深度 0 | root = 0；直接子节点 = 1（实现沿 `ParentNodeId` 数祖先，未改） |
| `AppendNode(string?, …)` | `parentId` 原样写入 | `null` 且存在 root → 挂 `root.NodeId`；无 root → 保持 `null`（选项 A） |

- **`RootCommand`**：root 节点 `Command` 固定字面量 **`"原图"`**（`EditSession` 私有常量）。
- **新增私有状态**：`EditNode? _rootNode`。
- **`SetRoot` 现也清空 DAG**（此前不清）：对二实例转发路径（`ApplyLaunchRequest` →
  `ApplyRequest`）而言，转发的请求本就替换主图，清空合理且与导入对称（用户裁决确认）。

### 9C.6.2 接口签名（冻结 · **零变化**）

- `IEditSession` / `IEditSessionWriter` **未改任何成员或签名**（G3 / G4 合规）。
- `RootImagePath` 在 `IEditSession` 本为 `{ get; }`；`EditSession` 由公开 set 改为派生只读仍满足
  接口约束，故**非契约级变化**。
- `IEditNode` 未改。

### 9C.6.3 `SessionExporter` / UI（冻结 · 非契约）

- **`SessionExporter` 未改**：root 节点进入 `GetHistory()` 后自动被拷为 `{NodeId}.png`；
  `session.json` 的 `nodes` 数组含 root，`root_image_path` 仍写派生值（兼容既有 schema）。
- **`MainWindow.BuildHistoryItem` 未改**：`Command = "原图"` 正常显示，无特殊分支。
- **`ImagePreview`（划像对比）未改**：`SetCompareSource(GetParentImagePath(path))` 逻辑不变；
  root → `null` 自动禁用对比，编辑结果 → 父图 = 原图。
- **`SessionViewModel.RebuildContext` 改**：重放路径时**跳过 root 节点**（`ParentNodeId is null`），
  因 root 已由「起始图像」系统气泡呈现——避免原图重复渲染（复审修复）。

### 9C.6.4 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：受影响类 `EditSessionTests` / `SessionViewModelTests` /
  `SessionExporterTests` / `CommandParserTests` / `ImageImportListTests` **57 通过 / 0 失败**；
  非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **175 通过 / 0 失败**
  （9C.5 基线 170，本步 +5）。
- **未跑 GPU 端到端**（Z29 / Z30）；本步不加载模型、不启动 Python、不占 GPU。

### 9C.6.5 遗留项（冻结）

- **Contracts XML 注释**：`IEditSession` / `IEditSessionWriter` 文档仍述旧语义（「null = root 当前」
  「不建节点」等）；**签名未变**，登记为后续文档整理。
- **`AppendNode` 未知 `parentId`**：静默建悬空节点（遍历有 `MaxTreeDepth` 保护，终止安全）；
  既有行为，本步未改。
- **9C.4 裁切接口**：本步完成后，裁切结果可直接 `AppendNode(当前节点 id, 裁切输出, "裁切")` 进会话，
  **无需再改底座**。

---

## Step 9C.4（日期：2026-09-23）

> **修订说明（Step 9C.4 · 裁切工具（内裁））**
>
> 本段为 Step 9C.4 **只增**记录。目标：在 `ImagePreview` 实现 PS 风格矩形内裁，确认后生成新文件
> （Z24）并进会话。**不改动 Step 0–9C.6 已冻结行**；本段为**纯追加**（文件尾部）。
> **无冻结契约变更**——`IEditSession` / `IEditSessionWriter` 签名零修改；本步新增类型均**非契约**。

### 9C.4.1 新增类型（冻结 · 非契约）

| 类型 | 文件 | 说明 |
|---|---|---|
| `CropState`（类） | `ZivAiEditor.UI/Editing/CropState.cs` | 纯逻辑裁切状态（无 Avalonia）：图像像素矩形、8 手柄 + Move、边界钳制、`MinSize=16`、状态机 |
| `CropHandle`（枚举） | 同上 | `None` / `Move` / `TopLeft` / `Top` / `TopRight` / `Right` / `BottomRight` / `Bottom` / `BottomLeft` / `Left` |
| `ImageCropper`（静态类） | `ZivAiEditor.UI/Imaging/ImageCropper.cs` | `ResolveOutputPath(string?, DateTimeOffset)` + `CropAsync(...)`；复用 ZIV.Imaging `SkiaCodec` + `SKImage.Subset` |
| `CropOverlay`（控件） | `ZivAiEditor.App/Controls/CropOverlay.axaml(.cs)` | 自绘叠加（框外暗化 + 边框 + 8 手柄），`IsHitTestVisible=false` |
| `CropCompletedEventArgs`（类） | `ZivAiEditor.App/Controls/ImagePreview.Crop.cs` | `SourceImagePath` / `OutputPath` |
| `ImagePreview.CropCompleted`（事件） | 同上 | 裁切完成事件（App 订阅后进会话） |

### 9C.4.2 `SessionViewModel` 新增方法（冻结 · 非契约）

- `public void AppendEditNode(string sourceImagePath, string outputPath, string command)` ——
  非 AI 编辑结果（裁切）入会话：父节点按 `ImagePath` 匹配（忽略大小写）→ 回退 `CurrentNodeId` →
  `_writer.AppendNode` → `RefreshHistory`。**不涉及契约接口变更**。

### 9C.4.3 ZIV.Imaging 复用（冻结 · 非契约）

- 裁剪走 **Option A**：`SkiaCodec.LoadThumbnail(path, int.MaxValue)`（全尺寸解码）→
  `SKImage.Subset(SKRectI)`（Skia 原语，ZIV.Imaging 无裁切原语）→ `SkiaCodec.SaveAsync`（编码）。
- **NativeAOT 验证**：`dotnet publish`（App，`PublishAot=true`）成功，输出含
  `Magick.Native-Q16-HDRI-OpenMP-x64.dll`；Magick 路径 AOT 可用。
- **不改** `ZIV.Imaging` / `ZIV.Core` 共享库（Z26）。

### 9C.4.4 落盘规则（冻结 · 非契约）

- `<源图目录>/<stem>_crop_<yyyyMMdd_HHmmss>.png`；同名冲突追加 `_1.._N`；源图路径空 →
  `<程序目录>/output/`。镜像 Python `_resolve_output_path`（仅 `_ai_` → `_crop_`）；
  **绝不覆盖源文件**（Z24 / SPEC §3.9）。

### 9C.4.5 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：受影响类 `CropStateTests` / `ImageCropperTests` /
  `SessionViewModelTests` / `ToolStateMachineTests` / `ImageViewModelTests` / `CompareStateTests`
  **83 通过 / 0 失败**；非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **214 通过 / 0 失败**
  （9C.6 基线 175，本步 +39）。
- `dotnet publish`（App，NativeAOT）成功；**未跑 GPU**（Z29 / Z30）。

---

## Step 9C.6-B（日期：2026-09-23）

> **修订说明（Step 9C.6-B · 裁切语义重构：节点级裁切状态）**
>
> 本段为 Step 9C.6-B **只增**记录。目标：把裁切从「AI 编辑步骤（建节点）」改为「节点的内在
> 属性」——每节点最多一个裁切状态、可反复调整、**不建节点**；送 AI 管线用裁切结果；划像对比
> 与裁切互斥。**契约变更已获用户授权**。**不改动 Step 0–9C.6 已冻结行**；本段为**纯追加**。

### 9C.6-B.1 新增契约（冻结 · 契约新增，已授权）

| 类型 | 文件 | 说明 |
|---|---|---|
| `CropSpec`（类） | `ZivAiEditor.Contracts/Planning/CropSpec.cs` | `X` / `Y` / `Width` / `Height`（图像原始像素）+ `ResultImagePath`（新文件，Z24），均 `init` |

- `IEditNode` **新增** `CropSpec? Crop { get; }`（默认 null）；`EditNode` 新增
  `CropSpec? Crop { get; init; }`。
- `IEditSession` **新增** `string? GetCurrentPipelineImagePath()` 与
  `string? GetParentPipelineImagePath(string? imagePath)`。
- `IEditSessionWriter` **新增** `void SetNodeCrop(string nodeId, CropSpec? crop)`。
- **既有成员零修改**：`GetCurrentImagePath()` / `GetParentImagePath()` 签名与语义**均不变**。

### 9C.6-B.2 语义变更（冻结 · 非契约）

| 行为 | 9C.4 | 9C.6-B |
|---|---|---|
| 裁切 | 新建节点（`AppendNode "裁切"`） | **节点属性** `EditNode.Crop`，不建节点 |
| 送 AI 管线图 | `当前节点.ImagePath` | `Crop?.ResultImagePath ?? ImagePath` |
| 划像对比左（父） | 父节点 `ImagePath` | 父节点 `Crop?.ResultImagePath ?? ImagePath`（新方法） |
| 划像对比右（当前） | 当前显示图 | 当前节点 `ImagePath`（**原图**） |
| 对比 / 裁切 | 独立 | **互斥**（进一者自动退另一者） |
| 初始裁切框 | 全图 | 上次裁切框（若有）否则 **75% 居中** |
| 裁切交互 | 移动整框 + 8 手柄 | **任意点按下拖动重建**（无移动 / 手柄） |

- `SetNodeCrop` 因 `EditNode` 为 `init`-only 而**重建节点**（保留 `NodeId`/`ParentNodeId`/
  `ImagePath`/`Command`/`CreatedAt`），并同步 `_rootNode` 引用；未知 `nodeId` → **no-op**。

### 9C.6-B.3 导出（冻结 · 非契约）

- `session.json` 节点新增 `crop { x, y, width, height, result_image_path }`；
  `result_image_path` 为**相对导出目录**名 `{NodeId}_crop.png`；导出时拷贝裁切结果到该名。
- 既有 `nodes[].image_path` 行为不变。

### 9C.6-B.4 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release --no-incremental` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：受影响类 `EditSessionTests` / `CropStateTests` /
  `SessionViewModelTests` / `SessionExporterTests` / `CommandParserTests` / `ImageCropperTests`
  **75 通过 / 0 失败**；非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **207 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.6-B 修订说明（2026-09-23 · 真机反馈，冻结 · 非契约）

> 本小节为 Step 9C.6-B 的**收尾修订说明**。**不改动 9C.6-B.1–9C.6-B.4 既有行**，仅追加。

- **裁切在原图上进行**：进入裁切加载节点原图（裁切坐标恒为原图坐标），退出恢复显示图（裁切结果/
  原图）；确认恒以原图为源，**不链式裁切**。
- **聊天反映裁切**：`RebuildContext` 用 `Crop?.ResultImagePath ?? ImagePath` 渲染气泡；
  `SetNodeCrop` 重建聊天（生成中跳过）。
- **交互恢复**：整框移动 + 8 手柄缩放（框外按下重建）；保留节点级裁切语义与 75% 默认框。
- **节点解析扩展**：`FindNodeByImagePath` / `GetParentPipelineImagePath` 同时匹配 `ImagePath` 与
  `Crop.ResultImagePath`（聊天气泡可能携带裁切结果路径）。
- **无新增契约**：本次修订仅行为语义与实现，`CropSpec` / `IEditNode.Crop` / 3 个方法签名不变。
- 修订后：`dotnet build` 0 错误 0 警告；非 GPU 全量 **228 通过 / 0 失败**。

---

## Step 9C.6-B2（日期：2026-09-23）

> **修订说明（Step 9C.6-B2 · 裁切图生命周期：临时区 + 覆盖 + 清理）**
>
> 本段为 Step 9C.6-B2 **只增**记录。目标：裁切结果（中间产物）改放程序目录临时区
> `_cache/crops/{sessionId}/{nodeId}.png`，每节点覆盖、会话关闭 / 重置 / 启动兜底清理。
> **无契约签名变化**。**不改动 Step 0–9C.6-B 已冻结行**；本段为**纯追加**。

### 9C.6-B2.1 `CropSpec.ResultImagePath` 位置语义（冻结 · 语义变化，非签名）

- 值仍为**绝对路径**，但位置由「源图目录 `<stem>_crop_<timestamp>.png`」改为
  **程序目录临时区** `{AppContext.BaseDirectory}/_cache/crops/{sessionId}/{nodeId}.png`（Z14）。
- **每节点一个文件，反复裁切覆盖**（不再累积）；旧 9C.4 的 `_crop_<timestamp>` 规则删除。
- `CropSpec` / `IEditNode` / `IEditSession` / `IEditSessionWriter` **签名与成员不变**。

### 9C.6-B2.2 清理策略（冻结 · 非契约）

- **会话关闭**：`App` 的 `desktop.Exit` → `ImageCropper.CleanupSession(sessionId)`。
- **重置根**（导入 / 二实例换图）：`SessionViewModel.SetRootImage` / `ApplyRequest` 在
  `ResetToRoot`/`SetRoot` 前调 `CleanupSession`（App/UI 层触发；**`EditSession` 不碰文件系统**）。
- **启动兜底**：`Program.Main` 首实例 `ImageCropper.CleanupAll()`（单实例 ⇒ 无活跃会话）。
- **容错**：逐文件 `try-catch`、目录不存在 no-op、绝不抛异常。
- **导出**：`SessionExporter` 从新位置拷贝到导出目录 `{NodeId}_crop.png`（相对名不变），
  导出后**不删**临时文件。

### 9C.6-B2.3 覆盖后的强制重载（冻结 · 非契约）

- `ImagePreview.LoadImage(path, force)`：裁切临时文件按节点覆盖（路径不变），确认后以
  `force:true` 重载同路径以显示新内容；`_generation` / `_bitmap` 逻辑经核查无「路径唯一性」假设。

### 9C.6-B2.4 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release --no-incremental` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`ImageCropperTests` / `SessionExporterTests` 等受影响类全过；
  非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **231 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.6-B2.5 遗留项（冻结）

- **`_cache` 总量上限 / 淘汰策略**：属 Z12 缓存有界，单独立步。
- **旧版（9C.4）`_crop_` 文件不自动清理**：用户手动清。

---

## Step 9C.6-C（日期：2026-09-23）

> **修订说明（Step 9C.6-C · 图片流入模型重构：附件条一次性输入）**
>
> 本段为 Step 9C.6-C **只增**记录。目标：附件条改为**一次性输入**——拖图 / 粘贴只进附件条
> （不再即时成 root），点发送才消费、成功后清空；单图 / 多图模式 + 发送校验 + 三选一弹框。
> **不改动 Step 0–9C.6-B2 已冻结行**；本段为**纯追加**（文件尾部）。
> **无冻结契约变更**——`IEditSession` / `IEditSessionWriter` / `ICommandParser` 签名零修改；
> 本步新增类型均**非契约**（UI / App 层）。

### 9C.6-C.1 新增类型（冻结 · 非契约）

| 类型 | 文件 | 说明 |
|---|---|---|
| `ImageEditMode`（枚举） | `ZivAiEditor.UI/Editing/ImageEditMode.cs` | `Single` / `Multi`（UI 编辑模式） |
| `AttachmentPreparation`（枚举） | `ZivAiEditor.UI/Chat/AttachmentPreparation.cs` | `Ready` / `NeedsDecision` / `NoImage`（发送前附件裁决） |
| `MultiImageChoice`（枚举） | `ZivAiEditor.App/MultiImagePromptDialog.axaml.cs` | `Cancel = 0` / `NewSession = 1` / `Reference = 2`（默认取消） |
| `MultiImagePromptDialog`（窗口） | `ZivAiEditor.App/MultiImagePromptDialog.axaml(.cs)` | 三选一弹框（取消 / 参考图-禁用 / 新会话） |

### 9C.6-C.2 `SessionViewModel` 新增成员（冻结 · 非契约）

- `ImageEditMode Mode { get; set; } = Single`。
- `bool HasRootImage { get; }`（`_session.RootImagePath` 非空）。
- `bool CanSend(string? input, int attachmentCount)`：文本空 → false；有附件时
  `Single ? count<=1 : count>=2`；**无附件不设限**（保留自然语言 T2I）。
- `AttachmentPreparation PrepareAttachments(string input, IReadOnlyList<string>? attachments)`：
  附件 >0 且无 root → `SetRootImage(首张)` + `Ready`；附件 >0 且有 root → `NeedsDecision`；
  附件空且有 root → `Ready`；附件空且无 root → `/` 开头 → `NoImage`，否则 `Ready`（T2I）。
- `void StartNewSessionFrom(IReadOnlyList<string> attachments)`：首张成新 root（重置 DAG）。
- `void AddHint(string text)`：插入 `ChatMessage{Role=System, IsError=true}`。

### 9C.6-C.3 `ImageImportList` / `ImageImportBar` 新增成员（冻结 · 非契约）

- `ImageImportList.HasImages`（`_paths.Count > 0`）；`ImageImportBar.HasImages` 改为委托之。
- `Clear()` / `RemoveAt` / `AddFiles` 既有签名不变。

### 9C.6-C.4 UI 行为语义（冻结 · 非契约）

- `MainWindow.Import.OnImagesChanged`：**删除** 0→1 即时 `SetRootImage`；改为附件 ≥2 且 Single
  时自动切 `Multi`，并刷新发送按钮启用态。
- `MainWindow.Send.SubmitAsync`：`PrepareAttachments` **先于** `_vm.SubmitAsync`；仅发送**成功**后
  `_importBar.Clear()`；`NoImage` → 提示且不发送；`NeedsDecision` + 非「新会话」→ 不发送、保留附件。
- `MainWindow.SetBusy`：由 `UpdateSendEnabled()` 统一驱动发送按钮启用态（不再无条件启用）。
- 工具行改为 `Auto,Auto,Auto,*,Auto`，新增 `PART_BtnMode` / `PART_ModeHint`，发送按钮列号 3 → 4。
- 模式 ↔ 附件数量不匹配（用户裁决矩阵）：附件 0 → 不设限；1 → 仅单图；≥2 → 仅多图。工具行内联
  瞬态气泡（约 2.5s），触发点为模式按钮点击 / 附件数量变化；`单图+≥2`→「请选择多图编辑」，
  `多图+1`→「请再添加一张图」，`多图+0` 不提示。

### 9C.6-C.5 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release --no-incremental` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`SessionViewModelTests` / `ImageImportListTests`
  **36 通过 / 0 失败**。
- 独立验证 / 复审（只读子代理）：复跑一致；复审 **Approved**，无 P0 / P1。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.6-C.6 遗留项（冻结）

- **多图管线**（`AdditionalImages` 契约 + `<imageN>` 解析 + Python `_encode` 多图）→ **9C.5-D**。
- **参考图功能本身** → 9C.5-D（弹框「参考图」灰化 + tooltip）。
- **`commands.json` 的 `mode` 字段** → 后置（留接口，不实现）。
- **发送校验矩阵已由用户裁决确认**：附件 0 → 不设限（T2I / 当前节点图）；1 → 仅单图；≥2 → 仅多图。
  `CanSend` 与之一致。

---

## Step 9C.6-D（日期：2026-09-24）

> **修订说明（Step 9C.6-D · 小修 + 显存 OOM 兜底）**
>
> 本段为 Step 9C.6-D **只增**记录。**不改动 Step 0–9C.6-C 已冻结行**；本段为**纯追加**。

### 9C.6-D.1 新增（冻结 · 非契约）

- `PythonBackendOptions.LogFilePath`（`string?`，init）：设置后 `PythonProcessManager` 以
  `--log-file <path>` 启动后端，日志写入程序目录 `_cache/backend.log`（Z14）；`AppContext` 默认开启。
- `SessionViewModel.SubmitAsync` OOM 自动重试（UI 非契约）：失败且 `ErrorMessage` 含
  `out of memory` / `OutOfMemory` 时，延迟 2s **重试一次**（同分辨率），重试期间 pending 气泡提示。

### 9C.6-D.2 调查结论（冻结 · 记录）

- 「新会话起新管线」**不成立**：现场采样全程 1 app + 1 python；`AppContext` 管线单例。
- OOM **间歇**（1024/1536/2048 连续 + 换图均未稳定复现）；异常态单进程私有内存 68 GB
  （正常 ~13 GB），疑似 Python/ComfyUI/DynamicVRAM 侧跨推理累积。
- `pipeline.py:_oom_types()` 不含 `AcceleratorError` → 后端自带降级未触发（**治本另立步**，
  需授权改 `python/server/*`）。

### 9C.6-D.3 测试结果（冻结）

- `dotnet build` → **0 错误 0 警告**；非 GPU 全量 **252 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）。

---

## Step 9C.6-E（日期：2026-09-23）

> **修订说明（Step 9C.6-E · 项目模型：保存 / 加载 / 列表 / 切换）**
>
> 本段为 Step 9C.6-E **只增**记录。目标：会话升级为**项目列表**（`sessions/{sessionId}/`
> 自包含），新建 / 删除 / 切换 / 重命名 / 保存 / 启动加载。**不改动 Step 0–9C.6-C 已冻结行**；
> 本段为**纯追加**。**`IEditSession` / `IEditSessionWriter` / `ICommandParser` 签名零变化**
> （G3 / G4 合规）。

### 9C.6-E.1 既有类型行为变化（冻结 · 非契约）

| 成员 | 变更前 | 变更后 |
|---|---|---|
| `EditSession.SessionId` | `{ get; init; }` | `{ get; set; }`（实现类内部；`IEditSession.SessionId` 仍 `{ get; }`） |
| `EditSession.CreatedAt` | `{ get; init; }` | `{ get; set; }` |
| `EditSession.Restore(...)` | 无 | **新增**：`void Restore(IReadOnlyList<IEditNode> nodes, string? currentId, string sessionId, DateTimeOffset createdAt)`（原地重建 DAG + 同步 `_rootNode`） |
| `SessionViewModel.Reload()` | 无 | **新增**（UI，非契约）：`RefreshHistory` + `RebuildContext` |

### 9C.6-E.2 `SessionExporter` 移除（冻结 · 既有记录变更）

- **删除** `ZivAiEditor.Agent/SessionExporter.cs`（`ISessionExporter` / `SessionExporter`），
  导出逻辑并入 **`SessionStore.ExportToAsync`**。原 FROZEN 8.1 / 8R.1 记录的
  `ISessionExporter.ExportAsync(EditSession, string, CancellationToken)` **不再存在**；以本小节为准。
- `AppContext` / `App.axaml.cs` / `MainWindow` 的 `ISessionExporter` 注入改为 **`SessionStore`**。

### 9C.6-E.3 新增类型（冻结 · 非契约）

| 类型 | 文件 | 说明 |
|---|---|---|
| `SessionStore`（类） | `ZivAiEditor.Agent/SessionStore.cs` | 项目保存 / 加载 / 列表 / 删除 / 改名 / 另存为 / `last_project.txt` |
| `SessionLoader`（静态类） | `ZivAiEditor.Agent/SessionLoader.cs` | JSON → `EditSession` 重建（版本 / 缺图 / root 归一化） |
| `SessionLoadResult`（类） | 同上 | `Session` / `Name` / `Warnings` |
| `ProjectInfo`（record） | `SessionStore.cs` | `SessionId` / `Name` / `CreatedAt` |
| `ProjectFormatException` / `ProjectCorruptException` | `SessionStore.cs` | 版本不匹配 / 文件损坏 |
| `ProjectListItem`（类） | `ZivAiEditor.UI/Projects/ProjectListItem.cs` | 列表项 VM |
| `TextPromptDialog`（窗口） | `ZivAiEditor.App/TextPromptDialog.axaml(.cs)` | 单行文本输入（改名） |

### 9C.6-E.4 `session.json` 格式变化（冻结 · 格式 v1）

- 新增 `version: 1` 与 `name`（项目名）。
- `nodes[].image_path` 由**绝对路径**改为**相对名** `{NodeId}.png`；`crop.result_image_path` 仍为
  相对名 `{NodeId}_crop.png`。加载时按项目目录解析。
- 旧格式（无 `version`）不再被列表 / 加载接受（列表跳过；加载抛 `ProjectFormatException`）。

### 9C.6-E.5 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release --no-incremental` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→
  **251 通过 / 0 失败**；新增 `SessionStoreTests` / `SessionLoaderTests`；`Contracts` diff 为空。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.6-E.6 遗留项（冻结）

- **「另存为」入口**：`SessionStore.ExportToAsync` 保留，UI 后置。
- **项目缩略图 / 项目导入** → 后置。
- **App 层编排无自动化测试**：属 UI 层。

---

## Step 9C.4-B（日期：2026-09-24）

> **修订说明（Step 9C.4-B · 裁切外扩：拖出边界 + 灰底画布）**
>
> 本段为 Step 9C.4-B **只增**记录。目标：裁切框可拖到图像边界之外，确认后生成含
> 「灰 0.5 背景填充」的更大画布（纯几何外扩，无 AI）。**不改动 Step 0–9C.6-E 已冻结行**；
> 本段为**纯追加**。**`CropSpec` 签名零变化**——复用既有字段，仅扩展 X / Y 语义（允许负值）。
> `session.json` 格式 v1 不变。

### 9C.4-B.1 `CropSpec` 语义扩展（冻结 · 非签名）

- `CropSpec.X` / `Y` / `Width` / `Height` 的语义统一为「**输出画布在原图坐标系中的矩形**」：
  - 内裁：`X ≥ 0` 且 `X + Width ≤ 原图宽`（行为不变）。
  - 外扩：`X` / `Y` **允许为负**；原图在输出画布中的位置 = `(-X, -Y)`。
- **字段 / 类型签名零变化**（`X` / `Y` / `Width` / `Height` / `ResultImagePath` 均 `init`）；
  `SessionStore` / `SessionLoader` 的 `crop { x, y, width, height, result_image_path }`
  **格式不变**（负整数正常持久化）。

### 9C.4-B.2 外扩上限（冻结 · 非契约）

- `CropState` 新增常量 `MaxExpandFactor = 2.0`（输出边长 ≤ 2× 原图对应边）与
  `MaxPixelCount = 16_000_000`（输出总像素 ≤ 16 MP）。
- 私有 `ClampToLimits` 统一钳制：边上限 + 面积上限（超限按比例缩）+ **≥1px 正面积重叠**
  （`x ∈ [max(-imgW, -w+1), min(imgW-1, 2·imgW-w)]`，y 对称）。
- **钳制只在 `CropState`**；`ImageCropper` 不二次钳制（仅 `width/height > 0` 防御）。
- 内裁语义保留：`SetRect` 的 `MinSize` 仍是**谓词**（小于即 `HasRect=false`），
  仅在 `Normalize` / 缩放时作为下限。

### 9C.4-B.3 行为变化（冻结 · 非契约）

| 项 | 变化 |
|---|---|
| `CropState` | 放开图像边界钳制（可负 / 超界）；`BeginDrag` 建框起点不再钳到图像内；`SetDefaultRect` 走面积上限并保持居中 |
| `ImageCropper` | `CropAsync` 改 **SkiaSharp 直连**：`new SKBitmap(w,h,Rgba8888,Premul)` → `SKCanvas.Clear((128,128,128))` → `DrawImage(full, -x, -y, paint{Src})` → `Encode(Png)`；不再 `SKImage.Subset` |
| `CropOverlay` | 框内、图像外区域填灰 `#808080`；框外暗化保留（精确交集，见 `CropOverlayGeometry`） |
| `ImageViewModel` | 新增 `FitWithMargin(double factor)` 与 `RestoreView(int, double, double)`（纯逻辑，非契约） |
| `CropOverlayGeometry`（新增） | 纯几何静态类（无 Avalonia），输出灰带 / 暗化带 |
| `ImagePreview.Crop` | 进入裁切保存原 Zoom/Offset → 缩到 `FitZoomPercent×0.65` 居中；退出恢复；进入时提示「拖到图像边界外可外扩」 |

- `DrawImage` 用 `SKBlendMode.Src` + `Premul`：源图自身 alpha 处保留原值（内裁 alpha PNG
  行为不变），仅无源区为不透明灰。

### 9C.4-B.4 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release --no-incremental` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→
  **270 通过 / 0 失败**；新增 `CropOverlayGeometryTests`；`CropStateTests` / `ImageCropperTests` /
  `ImageViewModelTests` / `SessionStoreTests`（负原点持久化）扩展。
- `dotnet publish src\ZivAiEditor.App -c Release -r win-x64` → NativeAOT **成功**。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.4-B.5 附带发现（冻结 · 仅记录，不改既有行）

- **FROZEN 9C.4.1** 记 `CropCompletedEventArgs` 为 `SourceImagePath` / `OutputPath`，
  但 **9C.6-B** 已改为 `NodeId` / `CropSpec`——该行过时（9C.6-B 段已覆盖新语义）。
- **FROZEN 9C.4.4** 记落盘规则 `<stem>_crop_<timestamp>.png`，已被 **9C.6-B2** 的
  `_cache/crops/{sessionId}/{nodeId}.png` 取代。
- 上述两行**按修改铁律不改**，仅在此登记。

### 9C.4-B-P2 修订说明（2026-09-24 · 冻结 · 非契约）

> **只增**记录。**不改动 9C.4-B.1–9C.4-B.5 既有行**，仅追加。

- **问题**：9C.4-B 的 16 MP 像素上限（`MaxPixelCount`）对**内裁也生效**——源图 >16 MP 时，
  纯内裁（含默认 75% 框）输出被意外缩小，与「裁出原始分辨率」预期不符。
- **修复**：`CropState.ClampToLimits` 的面积上限改为**仅外扩时生效**（新增 `IsOutpaint`：
  矩形任一边越出图像即视为外扩，含 1e-6 浮点容差）；`SetDefaultRect` 不再做面积缩放
  （默认框恒为内裁）。
- **不变**：边上限（2×）与位置重叠钳制保持（对内裁为 no-op）；`MaxPixelCount` 仍约束
  **外扩**画布；`CropSpec` 签名 / `session.json` 格式零变化。
- **测试**：`CropStateTests` 新增 `SetRect_Inner_Crop_Not_Capped_By_Pixels` /
  `SetDefaultRect_Not_Capped_For_Large_Source`；`dotnet build` 0/0；非 GPU 全量 **272 通过 / 0 失败**。

---

## Step 9C.5-D（日期：2026-09-24）

> **修订说明（Step 9C.5-D · 多图编辑端到端：主图 + 参考图 + `<imageN>`）**
>
> 本段为 Step 9C.5-D **只增**记录。目标：附件第一张为主图、其余为参考图；用户 prompt 以
> `<imageN>` 逐字引用（分词器自动插入标记，C# / Python **不解析**）。**不改动 Step 0–9C.6-E /
> 9C.4-B 已冻结行**；本段为**纯追加**。契约改动均为**追加式**（既有成员 / 签名零变化）。

### 9C.5-D.1 `AdditionalImages` 契约（冻结 · 追加 → 非破坏）

| 类型 | 文件 | 追加成员 |
|---|---|---|
| `EditRequest` | `ZivAiEditor.Contracts/Inference/EditRequest.cs` | `IReadOnlyList<string> AdditionalImages { get; init; } = Array.Empty<string>()` |
| `PlanRequest` | `ZivAiEditor.Contracts/Planning/PlanRequest.cs` | 同上 |
| `EditPlan` | `ZivAiEditor.Contracts/Planning/EditPlan.cs` | 同上 |
| `ToolInput` | `ZivAiEditor.Contracts/Tools/ToolInput.cs` | 同上 |

- **位置即编号**：`ImagePath` / `MainImagePath` = `<image1>`，`AdditionalImages[0]` = `<image2>`，
  依此类推。`ReferenceImagePath` **保留**（`QW21edit` 视为 image2 前置，见 R3）。
- 既有成员 / 签名**零变化**；`AdditionalImages` 默认空列表（**永不 null**；`[]` 序列化无害）。

### 9C.5-D.2 IPC payload（冻结 · ipc_version 0.8）

- `submit.payload` 新增可选字段 `additional_images`（`string[] | null`）：主图之后的**有序
  参考图路径**；缺省 / `null` / `[]` = 无参考图。`config.PROTOCOL_VERSION` 升 **`0.8`**。
- `IpcSubmitMapper` 按 `EditRequest.AdditionalImages` 顺序写入 `SubmitPayload.AdditionalImages`。
- **向后兼容**：旧端忽略该字段；缺省行为与 0.7 一致。详见 `contracts/ipc-protocol.md` §3.4 / §7。

### 9C.5-D.3 管线（冻结 · Python）

- 新增 `python/server/multi_image.py`（纯 CPU，无 torch / comfy 导入）：
  `normalize_additional_images(value)`（None / 非列表 → `[]`；去空白；保序）与
  `reference_paths(main_path, additional_images)`（`[main] + extras`，main 优先）。
- `pipeline.run` 读取 `request["additional_images"]`，经 `_run_once` → `encode_prompt` → `_encode`
  下传；`_encode` 主图处理**不变**，随后对每张参考图按**各自纵横比 + 同一 `spec` 口径**
  （`_target_size_from_spec(extra_w, extra_h, spec)`，D6 解释）lanczos 缩放，同一张量同时追加到
  `images_vl`（`[:, :, :, :3]`）与 `references`（`vae.encode`）。主图恒为 `references[0]`。

### 9C.5-D.4 UI 行为（冻结 · 非契约）

- `SessionViewModel.SubmitAsync` 追加可选参数 `IReadOnlyList<string>? additionalImages = null`
  （具体类，非破坏）：解析成功后、执行前**截断到 3 张**参考图（D4：管线最多 4 张），超限
  追加 `AddHint`（在用户消息之后），再以 `AdditionalImages` 重建计划（镜像
  `CommandParser.ApplyResolution`；`ICommandParser` 签名零变化）。
- `MainWindow.Send.SubmitAsync` 分支：无 root + 附件 → 首张成 root，其余为参考图；
  有 root + `新会话` → 首张成新 root，其余为参考图 + 提示「图 2/3 作为参考图」（在
  `StartNewSessionFrom` 之后）；有 root + `参考图` → **不重置 DAG**，主图 = 当前节点管线图，
  **全部**附件为参考图；`取消` → 不发送。**删除**旧提示「多图参考暂未实现，本次仅使用第一张」。
- `MultiImagePromptDialog` 启用 `参考图` 按钮（移除 `IsEnabled="False"` 与「多图编辑暂未实现」
  tooltip），点击关闭为 `MultiImageChoice.Reference`。

### 9C.5-D.5 会话持久化（冻结 · D7 = B）

- **参考图不写入 `session.json`**；既有 `session.json`（无该字段）加载**不受影响**（R1）。

### 9C.5-D.6 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release --no-incremental` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**，排除 `Ipc*` / `PlannerIntegration`）→ **285 通过 / 0 失败**；
  `IpcSubmitMapperTests`（FQN 含 `Ipc`，被过滤器排除）单独跑 → **5 通过 / 0 失败**。
- `python -m unittest test_multi_image`（纯 CPU）→ **9 通过 / 0 失败**；`discover` → **20 通过**。
- **Z8**：`pipeline.py` 685 → 拆出纯助手模块 `resolution.py`（无 torch/comfy），`pipeline.py` 499 /
  `resolution.py` 206，均 < 600；行为不变（同名函数经 import 引入）。
- **未跑 GPU 端到端**（Z29 / Z30）；3 场景 GPU 验证由用户执行（D2）。

### 9C.5-D.7 遗留项（冻结）

- **GPU 3 场景验证**（`<image2>` / `<image3>` 引用）由用户执行（D2，本步不跑 GPU）。
- **`commands.json` 的 `mode` 字段**仍后置。

---

## Step 9C.7 — 手绘遮罩（MaskCanvas）

> 追加式冻结：以下均为**新增成员 / 新字段**，既有契约成员 / 签名**零变化**；语义变更仅说明。

### 9C.7.1 契约（冻结 · 追加 → 非破坏）

| 类型 | 文件 | 追加成员 |
|---|---|---|
| `IEditNode` | `ZivAiEditor.Contracts/Planning/IEditNode.cs` | `MaskSpec? Mask { get; }` |
| `IEditSession` | `ZivAiEditor.Contracts/Planning/IEditSession.cs` | `MaskSpec? GetCurrentMaskSpec();` |
| `IEditSessionWriter` | `ZivAiEditor.Contracts/Planning/IEditSessionWriter.cs` | `void SetNodeMask(string nodeId, MaskSpec? mask);` |

- `MaskSpec` 复用既有类型（`MaskImagePath` / `Width` / `Height` / `IsBinary` / `Invert`），**未改**。
- 遮罩为**节点属性**（D1）：至多一个 / 节点，可重编辑，**不新增节点**。

### 9C.7.2 语义（冻结 · 变更说明）

- **遮罩坐标 = 当前 pipeline 图**（裁切结果画布，D2），而非主图原图；与裁切**数据上不互斥**
  （`ToolMode` 单选，同一时刻只一个工具激活）。
- `EditSession.SetNodeCrop` 仅在裁切**实际变化**（X/Y/W/H/ResultImagePath 任一不同）时清空该节点
  遮罩；同值重设**保留**遮罩。提示「裁切已改，遮罩已重置」由 `SessionViewModel` 发出（Agent 不发 UI 提示）。
- 发送后遮罩**保留**（D5）。

### 9C.7.3 持久化（冻结 · 追加字段，向后兼容）

- `session.json` 每节点新增可选 `mask` 对象：`image_path`（项目内 `{nodeId}_mask.png`）/ `width` /
  `height` / `is_binary` / `invert`。`SessionStore.FormatVersion` **仍为 1**；缺字段 → `null`
  （旧项目加载不受影响）。
- 项目保存时把 `_cache/masks/{sessionId}/{nodeId}.png` 拷为 `{nodeId}_mask.png`；加载缺文件 →
  丢遮罩 + 警告，**保留节点**。

### 9C.7.4 临时区（冻结 · Z14）

- 遮罩临时落盘 `_cache/masks/{sessionId}/{nodeId}.png`（覆盖式，一节点一文件），生命周期与
  `_cache/crops` 一致（启动 / 关闭 / 切项目 / 重置 root 清理）。

### 9C.7.5 送管线（冻结）

- `CommandParser.ParseSlashCommand` / `ParseNaturalLanguage` 以 `session.GetCurrentMaskSpec()` 注入
  `EditPlan.Mask`；既有 `EditPlan.Mask` → `ToolInput.Mask` → `EditRequest.MaskPath` 链路不变。
- 二值 PNG 仅含 0 / 255（Z19 / R2）；`MaskSpec.Invert` 保持 false（R6）。

### 9C.7.6 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**，排除 `Ipc*` / `PlannerIntegration`）→ **322 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）；发送时遮罩生效由用户真机确认。

### 9C.7.7 遗留项（冻结）

- **画笔大小 UI 滑块**（D6 固定 40px）。
- **`MaskSpec.Width/Height` 注释**仍为「主图原始像素」，与 D2 实际（pipeline 图坐标）不符，仅登记不改。
- **遮罩脏标记用文件 mtime+长度**代理（非内容哈希）。

---

## Step 9C.7-B — 遮罩增强（笔刷圆 / 浮动条 / 中键平移 / 灰度羽化）

> 追加式冻结 + Z19 授权修订：既有契约成员**只加不改**；Z19 原文保留，本段为**尾部授权说明**。

### 9C.7B.1 Z19 修订（冻结 · 授权说明，不改原文）

> **修订说明（2026-09-24 · Z19 灰度授权）**
>
> 用户授权：C# 可**忠实表达用户显式指定的软边（羽化）**，导出灰度 PNG；后端以 `mask_binary=False` 读取。
> Z19 原文「遮罩 PNG 只含 0 / 255」的**核心精神不变**：C# 禁止做后端算法会做的**预处理**
> （自动膨胀、智能补边、形态学操作）。本修订仅放开「用户显式羽化」——羽化值由 UI 滑块决定，
> 非 C# 推断；屏幕显示 = 导出 PNG = 后端处理，三处一致。
> 依据：`_test_step2/mask_feather_result.md`（GPU 实测确认模型接受灰度）。

- **保持**：`FeatherPx = 0` 时导出 PNG **仍只含 0 / 255**（二值），与 Z19 原文一致。
- **禁止未变**：C# 仍禁止自动膨胀 / 智能补边 / 形态学等预处理。

### 9C.7B.2 契约（冻结 · 追加 → 非破坏）

| 类型 | 文件 | 追加成员 |
|---|---|---|
| `MaskSpec` | `ZivAiEditor.Contracts/Imaging/MaskSpec.cs` | `int FeatherPx { get; init; }`（默认 0，范围 0–25） |

- `MaskSpec` 既有成员（`MaskImagePath` / `Width` / `Height` / `IsBinary` / `Invert`）**零变化**。
- `FeatherPx` **不**进入 IPC payload（后端读的是已羽化的 PNG 文件）；仅 C# 侧节点属性 / 持久化用。

### 9C.7B.3 持久化（冻结 · 追加字段，向后兼容）

- `session.json` 的 `mask` 对象追加可选 `feather_px`（int，缺省 0）。`FormatVersion` **仍为 1**。

### 9C.7B.4 IPC 语义（冻结 · 语义澄清，`ipc_version` 不变 0.8）

- `submit.payload.mask_path` 语义由「二值 PNG」修订为「**灰度 PNG（0–255；0=不编辑，255=完全编辑）**」；
  消息结构未变，故 `ipc_version` 不变。详见 `contracts/ipc-protocol.md` §3.4。

### 9C.7B.5 行为（冻结 · 非契约）

- 笔刷大小 5–200（默认 40，可调）；羽化 0–25（默认 0）；浮动工具条顶部居中，仅 `MaskBrush` / `Eraser` 激活时可见。
- 中键平移（R1）：左键先按（描边 / 左平移）→ 中键忽略至左键释放；中键先按（平移）→ 左键忽略至中键释放；
  每次释放只结束自己的模式。
- 模糊：3 趟可分离 box blur（高斯近似），**显示与导出共用同一纯函数** `MaskFeather.Apply`。
- 遮罩缓冲始终为硬边 0/255；羽化为显示 / 导出时的派生处理；重开时 `TryLoadAsync` 阈值回硬边，
  `FeatherPx` 由持久化恢复后重新应用。

### 9C.7B.6 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- 受影响类（MaskState / MaskFeather / PointerArbiter / MaskExporter / SessionStore / SessionLoader /
  CommandParser / Executor / IpcSubmitMapper）→ **98 通过 / 0 失败**。
- `python -m py_compile python/server/{handlers,pipeline}.py` OK；
  `python -m unittest discover -p test_mask_feather.py`（cwd `python/server`）→ **3 通过**。
- **未跑 GPU 端到端**（Z29 / Z30）；羽化软边融合由用户真机确认。

### 9C.7B.7 遗留项（冻结）

- **遮罩脏标记改内容哈希**（9C.7 遗留，仍用文件 mtime+长度代理）。
- **羽化视觉与后端一致性 GPU 验证**（待用户确认 GPU 空闲 + 明确同意）。
- **App 层指针路由（中键 / 悬停）无自动化测试**：`PointerArbiter` 已单测，`OnPressed/Moved/Released`
  路由为 App 层薄封装，由真机 / 无头探针覆盖。

---

## Step 9C.8-A（日期：2026-09-24）

> **修订说明（Step 9C.8-A · 参数快照 + `RerunAsync`（放弃 SQLite））**
>
> 本段为 Step 9C.8-A **只增**记录。目标：让历史节点可**重跑**——`EditNode` 加最小参数快照，
> `IExecutor.RerunAsync` 从 DAG 重建 `EditPlan` 并执行，历史节点右键「重跑」。
> **不改动 Step 0–9C.7-B 已冻结行**；本段为**纯追加**。契约变更为**新增类型 / 新增成员 /
> 一次授权签名变更**（`RerunAsync` 的键 `taskId` → `nodeId`）。

### 9C.8A.1 Z20 修订（冻结 · 授权说明，不改原文）

> **修订说明（2026-09-24 · Z20 落地方式）**
>
> **Z20 的任务状态来源 = DAG（`session.json`）。** 运行记录的可重跑性由 `EditNode` 的
> 参数快照 / `Command` 保证。**不引入独立 SQLite 存储**——经评估，其成本（AOT 风险 /
> 依赖 / 表结构维护 / 迁移）与 ZIV.AI 单机轻量定位（不做跨会话任务查询 / 统计）不成比例。
> Z20 原文（`SPEC.md` §6.2）不改，以本说明为准；`SqliteTaskStore` / `ITaskStore` 不再落地。

### 9C.8A.2 新增契约（冻结 · 追加 → 非破坏）

| 类型 | 文件 | 说明 |
|---|---|---|
| `RerunSpec`（类） | `ZivAiEditor.Contracts/Planning/RerunSpec.cs` | `ResolutionPolicy? Resolution`；`IReadOnlyList<string> AdditionalImages`（默认空）。**只存 DAG 无法重建的两项**：UI 分辨率 + 参考图；`prompt` / `tool` / `steps` / `denoise` 由 `Command` 重解析，源图 / mask 由父节点推导 |

- `IEditNode` **新增** `RerunSpec? Rerun { get; }`（默认 null）；`EditNode` 新增同名 `init` 属性。
- `IEditSessionWriter` **新增** `void SetNodeRerun(string nodeId, RerunSpec? rerun)`。
- **既有成员零修改**（除下方 9C.8A.3 授权的 `RerunAsync`）。

### 9C.8A.3 `IExecutor.RerunAsync` 签名变更（冻结 · 授权）

> **2026-09-24，用户授权：`RerunAsync` 的重跑键由 `taskId` 改为 `nodeId`。**
> 原因：本仓库**没有 taskId**（`TaskState.TaskId` 是执行时新建的 Guid，从不外传 / 落盘）；
> 可重跑的实体是 DAG 节点。旧签名 `RerunAsync(string taskId, ...)` **不再存在**。

```csharp
// 新签名（旧 taskId 版作废）
Task<TaskState> RerunAsync(string nodeId, IProgress<TaskProgress>? progress = null,
                           CancellationToken ct = default);
```

- **语义**：重跑 = **重新执行**（新随机 seed；**不保证逐像素复现**）。无父节点的节点
  （源图 root / T2I-first 节点）**不可重跑**（`InvalidOperationException`）；未知节点抛
  `ArgumentException`。**生成新节点、不覆盖旧节点（Z24）**——新节点为原节点的**兄弟**
  （同父），由 UI 追加。

### 9C.8A.4 `session.json` 节点结构（冻结 · 追加字段，向后兼容）

- `SessionFileNode` 新增可选 `rerun` 对象；**两者皆空时序列化为 `null`**（与既有 `crop` /
  `mask` 的 null 一致）。`FormatVersion` **仍为 1**；缺字段 → `Rerun = null`（旧项目加载不受影响）。

```json
"rerun": {
  "resolution": { "mode": "Side", "side": 1536, "area": null, "scale": null,
                  "width": null, "height": null, "max_pixels": 4700000 },
  "additional_images": ["refs/{NodeId}_ref1.png", "refs/{NodeId}_ref2.jpg"]
}
```

- `resolution` 可空；`mode` 为可读字符串（`Side` / `Area` / `Scale` / `Explicit`）。
- `additional_images` 为**相对项目目录**的名字，位于 `refs/` 子目录。

### 9C.8A.5 参考图持久化（冻结 · D7 修订授权）

> **2026-09-24，用户授权修订 9C.5-D.5（D7=B）：参考图**允许**作为节点重跑快照写入
> `session.json`。** 原 D7「参考图不写入 `session.json`」的**一般性**仍成立（不写顶层
> 字段、不作为节点图）；本修订仅放开「节点重跑快照」这一受限场景。

- **路径策略 = 拷入项目目录**：保存时把节点快照的参考图拷为
  `sessions/{sessionId}/refs/{NodeId}_ref{n}{ext}`，`session.json` 存**相对名**；加载时拼
  项目目录解析（与节点图 / 遮罩 / 裁切一致，保持项目可移植）。缺文件 → 丢该参考图 + 警告，
  **保留 resolution 与节点**。

### 9C.8A.6 实现清单（冻结 · 非契约）

| 类 / 改动 | 文件 | 职责 |
|---|---|---|
| `RerunSpec`（新增） | `Contracts/Planning/RerunSpec.cs` | 重跑快照值对象 |
| （修改）`IEditNode` / `EditNode` | `Contracts/Planning/IEditNode.cs` / `Agent/EditSession.cs` | `Rerun` 属性 |
| （修改）`IEditSessionWriter` / `EditSession` | `Contracts/Planning/IEditSessionWriter.cs` / `Agent/EditSession.cs` | `SetNodeRerun`；`Restore` / `SetNodeCrop` / `SetNodeMask` 均保留 `Rerun` |
| （修改）`IExecutor` / `Executor` | `Contracts/Execution/IExecutor.cs` / `Agent/Executor.cs` | `RerunAsync(nodeId)`：查节点 → 暂存 current → `NavigateTo(parent)` → `ParseAsync(Command, Resolution)` → 注入 `AdditionalImages` → 恢复 current（try/finally）→ `ExecuteAsync`。**Executor 新增注入 `IEditSession` / `IEditSessionWriter` / `ICommandParser`**（重跑需从 DAG 重建计划） |
| （修改）`SessionStore` / `SessionLoader` | `Agent/SessionStore.cs` / `Agent/SessionLoader.cs` / `Agent/SessionFileRerun.cs`（新 DTO） | `rerun` 序列化 / 反序列化 + `refs/` 拷贝 / 解析 |
| （修改）`SessionViewModel` | `UI/Chat/SessionViewModel.cs` / `SessionViewModel.Rerun.cs`（partial，新） | `SubmitAsync` 写快照；`RerunNodeAsync` 追加兄弟节点；`RunWithOomRetryAsync` 共用提取 |
| （修改）`MainWindow` | `App/MainWindow.axaml.cs` / `MainWindow.Rerun.cs`（partial，新） | 历史节点右键「重跑」→ `RerunNodeAsync` |
| （修改）`AppContext` | `App/AppContext.cs` | 先建 session / parser，再注入 `Executor` |

- **DAG 追加仍留在 UI**：`Executor.RerunAsync` 只重建计划 + 执行，不写 DAG；`SessionViewModel`
  追加新节点（与 `SubmitAsync` 现有分工一致）。
- **Z8**：`SessionStore.cs` 560 行；新增 `SessionFileRerun.cs`（DTO）、`SessionViewModel.Rerun.cs`、
  `MainWindow.Rerun.cs` 均为满足 600 行预算的拆分。

### 9C.8A.7 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：受影响类 `EditSessionTests` / `SessionStoreTests` /
  `SessionLoaderTests` / `ExecutorTests` / `SessionViewModelTests` / `ContractsSmokeTests` /
  `CommandParserTests` → **135 通过 / 0 失败**；非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）
  → **358 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）；本步不加载模型、不启动 Python、不占 GPU。

### 9C.8A.8 遗留项（冻结）

- **生成中取消**（重跑 / 提交的在飞取消 UI）→ 9C.8-B。
- **固定 seed 复现**：重跑为「重新执行」，不保证逐像素一致；如需复现需先引入 seed 链路（后置）。
- **`GetTaskAsync`**：阻塞式 submit 下无调用方，继续 `NotSupportedException`（后置）。

---

## Step 9C.8-A2（日期：2026-09-24）

> **修订说明（Step 9C.8-A2 · 重跑改为「替换节点」）**
>
> 本段为 Step 9C.8-A2 **只增**记录。9C.8-A 的重跑语义有误——它**新建兄弟节点**；用户裁决
> 重跑 = **重新执行同一节点、原地替换、不新建**。**不改动 Step 0–9C.8-A 已冻结行的既有成员**；
> 契约变更为**新增两个方法**（走本段追加）。**A2/B1 授权**：级联删除子节点 + 删除旧图。

### 9C.8A2.1 语义修正（冻结）

- 重跑 = **原地替换**：保留 `NodeId` / `ParentNodeId` / `Command`，仅更新 `ImagePath`；
  **不新建节点**（取代 9C.8-A 的「新建兄弟」）。
- **A2（授权）**：重跑一个有子节点的节点 → **级联删除其全部后代**（该节点自身保留）。
- **B1（授权）**：重跑成功后删除旧图（旧输出图 / 裁切图 / 遮罩图 / 参考图副本）。
- **失败不删**：先执行重跑，**成功才删**；失败则节点与子树原状不动、不删任何文件。
- 清空 `Crop` / `Mask`（新输出图，旧坐标失效）；**`Rerun` 快照保留**（支持连续重跑）。

### 9C.8A2.2 新增契约（冻结 · 追加 → 非破坏）

| 成员 | 文件 | 说明 |
|---|---|---|
| `IEditSessionWriter.ReplaceNodeImage(string nodeId, string newImagePath)` | `Contracts/Planning/IEditSessionWriter.cs` | 原地替换节点输出图；同 identity / parent / command / crop / mask / rerun；未知 nodeId → no-op |
| `IEditSessionWriter.RemoveSubtree(string nodeId) → IReadOnlyList<IEditNode>` | 同上 | 删除 `nodeId` 的**全部后代**（`nodeId` 自身保留），返回被删节点供清文件；未知 nodeId → 空列表（no-op） |

> **语义说明（与草稿注释的差异，以本段为准）**：`RemoveSubtree(nodeId)` 删除的是
> **`nodeId` 的后代子树（不含 `nodeId` 自身）**——与 A2「级联删除所有**子节点**」一致，
> 且使 `RerunNodeAsync` 的「先 `RemoveSubtree(N)` 再 `ReplaceNodeImage(N)`」成立。草稿注释
> 「删除节点及其所有后代」措辞不精确，**以本段为准**。

- `EditSession` 实现两方法（`EditSession.Subtree.cs`，Z8 拆分）；`EditSession` 改为 `partial`。
- **既有成员零修改**。

### 9C.8A2.3 文件清理（冻结 · 非契约）

`SessionViewModel.RerunNodeAsync` 成功后的清理：

| 文件 | 位置 | 处置 |
|---|---|---|
| 被替换节点旧输出图 | `node.ImagePath`（主图目录 `{StepId}.png` / T2I 的 `%TEMP%/zivai/{StepId}.png`） | 删 |
| 被替换节点旧裁切图 / 遮罩图 | `_cache/crops|masks/{sessionId}/{nodeId}.png` | 删 |
| 被删子树各节点 | 输出图 / 裁切图 / 遮罩图 | 删 |
| **项目副本**（保存过才有） | `sessions/{id}/{nodeId}.png` / `_crop.png` / `_mask.png` | 删（被替换节点 + 子树） |
| **项目参考图副本** | `sessions/{id}/refs/{nodeId}_ref*` | **仅删子树**；被替换节点自身的参考副本**保留**（连续重跑仍需） |
| **外部用户参考图** | `node.Rerun.AdditionalImages`（用户原文件） | **不删（Z24）** |

- **安全边界**：绝不再删除 `_session.RootImagePath`；每文件单独 try-catch、绝不抛（同
  `CleanupSession` 精神）。
- **项目副本清理**：`SessionStore.DeleteNodeArtifacts(sessionId, nodeIds, includeReferences)`
  （`SessionStore.Cleanup.cs`，Z8 拆分；`SessionStore` 改为 `partial`）。未保存过 → 项目目录
  不存在 → no-op。
- 清理在 `Task.Run` 后台执行（Z11）。

### 9C.8A2.4 `RerunNodeAsync` 流程（冻结 · 非契约）

```
1. 查节点 N（未知 → 提示并返回 false）
2. N 无父节点 → 提示并返回 false（root / T2I-first 不可重跑）
3. 记下 currentBefore = CurrentNodeId
4. executor.RerunAsync(N) → state；失败 → 报错，不删任何文件
5. 成功后：
   a. removed = writer.RemoveSubtree(N)            // 后代子树
   b. writer.ReplaceNodeImage(N, newOutput)
   c. writer.SetNodeCrop(N, null) / SetNodeMask(N, null)
   d. currentBefore ∈ {N} ∪ removed → writer.NavigateTo(N)
   e. 项目副本清理（Task.Run）：removed 含 refs；N 不含 refs
   f. 删旧文件（Task.Run）：N 旧输出/裁切/遮罩 + 子树各文件；不删外部参考图
   g. RefreshHistory() + RebuildContext()
```

- **DAG 追加/删除** 仍在 UI（`SessionViewModel`）；`Executor.RerunAsync`（Agent）语义**不变**。

### 9C.8A2.5 实现清单（冻结 · 非契约）

| 类 / 改动 | 文件 |
|---|---|
| `IEditSessionWriter` +2 方法 | `Contracts/Planning/IEditSessionWriter.cs` |
| `EditSession.ReplaceNodeImage` | `Agent/EditSession.cs`（`partial`） |
| `EditSession.RemoveSubtree` | `Agent/EditSession.Subtree.cs`（新，Z8） |
| `SessionStore.DeleteNodeArtifacts` | `Agent/SessionStore.Cleanup.cs`（新，Z8；`SessionStore` `partial`） |
| `SessionViewModel` 注入 `nodeArtifactsCleaner` 委托 | `UI/Chat/SessionViewModel.cs` |
| `RerunNodeAsync` 替换语义 + 清理 | `UI/Chat/SessionViewModel.Rerun.cs` |
| `MainWindow` 传 `_store.DeleteNodeArtifacts` | `App/MainWindow.axaml.cs` |

### 9C.8A2.6 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：受影响类 → **147 通过 / 0 失败**；非 GPU 全量（排除
  `Ipc*` / `PlannerIntegration`）→ **370 通过 / 0 失败**。
- **Z8**：`EditSession.cs` 533 / `EditSession.Subtree.cs` 87 / `SessionStore.cs` 560 /
  `SessionStore.Cleanup.cs` 94 / `SessionViewModel.cs` 584 / `SessionViewModel.Rerun.cs` 244，
  均 < 600。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.8A2.7 遗留项（冻结）

- **生成中取消** → 9C.8-B。
- **固定 seed 复现**（后置）。
- **外部用户参考图不删**：与 B1 草稿「删除参考图」的差异——仅删应用自有的项目副本，
  不删用户原始参考图（Z24）。

---

## Step 9C.8-A3（日期：2026-09-24）

> **新增说明（Step 9C.8-A3 · 重跑聊天流原地更新）**
>
> 本段为 Step 9C.8-A3 **只增**记录。9C.8-A2 已实现「重跑原地替换节点」（DAG / 历史列表正确），
> 但聊天流仍**新增消息**（「重跑:」用户气泡 + 新 AI pending 气泡）。用户要求重跑**不新增任何
> 消息**，直接在**原 AI 气泡**上显示进度并原地换图。**不改动 Step 0–9C.8-A2 已冻结行的既有成员**；
> 唯一类型变化为 UI 自有类型 `ChatMessage` **追加字段**（非契约）。**不改** `SubmitAsync` 的气泡数量。

### 9C.8A3.1 `ChatMessage` 追加字段（冻结 · 非契约，UI 类型）

- `ZivAiEditor.UI/Chat/SessionViewModel.cs` 的 `ChatMessage` 追加：
  `public string? NodeId { get; init; }`——标识该气泡归属的会话节点（AI 气泡），使重跑能
  **按 NodeId 定位原气泡并原地更新**。`null` = 用户 / 系统气泡或尚未 append 的 pending 气泡。
- **非契约**（`ChatMessage` 为 UI 自有类型，见 9A.8）；`Contracts` 零变更。

### 9C.8A3.2 归属写入（冻结 · 非契约）

- `SubmitAsync` 完成气泡：填 `NodeId = appended.NodeId`（**气泡数量与流程不变**，仅补字段）。
- `RebuildContext`：System「起始图像」填 root 节点 id；每个节点的 Assistant「完成」气泡填
  `NodeId = node.NodeId`。

### 9C.8A3.3 `RerunNodeAsync` 行为（冻结 · 非契约）

- **不新增任何消息**：按 `NodeId` 找到该节点的 Assistant 气泡，**原地替换**为 pending 副本
  （`Text="重跑中…"`、`IsPending=true`、保留原 `ImagePath`、`NodeId` 不变）。
- **进度**：`SetStatus` / `ShowPreview`（既有 pending 机制）自动指向该气泡（`RenderChat` 在
  替换时重建并捕获 `_pendingTextLabel` / `_pendingPreviewImage`）。
- **完成**：原地替换为 `Text="{耗时}秒 完成"`、`ImagePath=新输出`、`IsPending=false`；仅
  `RefreshHistory()`，**不调用 `RebuildContext()`**（避免清空 / 重建气泡）。
- **失败 / 取消**：原地替换为错误气泡（`IsError=true`、保留原 `ImagePath`）。
- **回退**：若找不到带该 `NodeId` 的气泡（节点从未渲染），追加一个 pending 气泡（防御性）。

### 9C.8A3.4 实现清单（冻结 · 非契约）

| 类 / 改动 | 文件 |
|---|---|
| `ChatMessage.NodeId` | `UI/Chat/SessionViewModel.cs` |
| `SubmitAsync` 完成气泡填 `NodeId` | 同上 |
| `RebuildContext` 填 `NodeId` | 同上 |
| `RerunNodeAsync` 原地更新 + `FindAssistantMessage` | `UI/Chat/SessionViewModel.Rerun.cs` |

### 9C.8A3.5 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：受影响类 → **131 通过 / 0 失败**；非 GPU 全量（排除
  `Ipc*` / `PlannerIntegration`）→ **373 通过 / 0 失败**。
- **Z8**：`SessionViewModel.cs` 599 / `SessionViewModel.Rerun.cs` 308，均 < 600。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.8A3.6 遗留项（冻结）

- **生成中取消** → 9C.8-B。
- **`SessionViewModel.cs` 逼近 Z8（599 行）**：后续若再加成员需拆分。

---

## Step 9C.8-B（日期：2026-09-24）

> **新增说明（Step 9C.8-B · 生成中取消）**
>
> 本段为 Step 9C.8-B **只增**记录。为生成中气泡加「取消」按钮，中断在飞任务。
> **无契约变更**（`Contracts` 零修改）；仅 UI / App 层行为 + Z8 拆分。用户裁决：
> D1=A（VM 持有在飞 CTS + `CancelCurrent()`）/ D2=气泡内「取消」按钮 / D3=原位替换为「已取消。」/
> D4=状态恢复。

### 9C.8B.1 取消链路（冻结 · 非契约）

- **机制 A**：`SessionViewModel` 持有在飞 `_inFlightCts`（`CancellationTokenSource`），
  `SubmitAsync` / `RerunNodeAsync` 在**任何 await 之前**用 `CreateLinkedTokenSource(ct)` 建链、
  传 `cts.Token` 给执行器，`finally` 清理。
- **为什么不用 `IExecutor.CancelAsync(taskId)` / `IInferenceClient.CancelTaskAsync(taskId)`**：
  `TaskState.TaskId` 在执行器内部生成、完成才返回，UI **执行期间拿不到 taskId** → 这两个 API
  在 UI 流程中不可达。取消外部 token 是**已验证可达后端**的路径（`IpcInferenceClient` 的
  `TryForwardCancelAsync`，Step 6E）——`Executor` 吞掉工具抛的 OCE 并返回 `Canceled` 状态。
- **`CancelCurrent()`**（`SessionViewModel.Rerun.cs`）：`_cancelRequested || _inFlightCts is null`
  → `false`；否则置 `_cancelRequested=true`、`cts.Cancel()`、返回 `true`；`ObjectDisposedException`
  → `false`；**不抛**（R1）。`_cancelRequested` 保证「多次点击 → false」，且**不参与** OOM 重试
  （token 是唯一中断源）。

### 9C.8B.2 UI（冻结 · 非契约）

- pending 气泡（`MainWindow.BuildMessage`）在预览图下方加「取消」`Button`：点击 → 立即禁用按钮
  + `SetStatus("取消中…")` + `_vm.CancelCurrent()`（R2）。
- **移除 `MainWindow._cts`**（R4）：确认其唯一用途是「传给 VM 的 token」+ `Closed` 时 `Dispose`；
  窗口关闭走 `_closing` 标志、**不**取消 `_cts` → 改为传 `CancellationToken.None`。

### 9C.8B.3 取消后语义（冻结 · 非契约）

- `BuildFailureMessage`：`TaskStatus.Canceled` **优先**返回「已取消。」（R3）——此前会显示执行器写入的
  `"canceled"`。
- 新提交取消：User 气泡保留；pending **原位替换**为「已取消。」+ `IsError`；**不 append 节点**。
- 重跑取消：原 AI 气泡 **原位替换**为「已取消。」+ **保留旧图** + `IsError`（A3 机制）。
- 状态恢复：`IsBusy=false`（finally）→ 输入框 / 发送按钮恢复；`_inFlightCts` 置 null（R4/D4）。

### 9C.8B.4 Z8 拆分（冻结 · 非契约）

- `ChatRole` / `ChatMessage` / `HistoryItem` → `UI/Chat/ChatMessage.cs`。
- 取消成员（`_inFlightCts` / `_cancelRequested` / `CancelCurrent`）与运行状态助手
  （`IsSuccess` / `IsOutOfMemory` / `BuildFailureMessage`）→ `SessionViewModel.Rerun.cs`。
- 结果：`SessionViewModel.cs` **546** 行（< 550 目标）；`SessionViewModel.Rerun.cs` 383。

### 9C.8B.5 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：受影响类 → **153 通过 / 0 失败**；非 GPU 全量（排除
  `Ipc*` / `PlannerIntegration`）→ **376 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）；取消后 `nvidia-smi` 回落由用户确认（R5）。

### 9C.8B.6 遗留项（冻结）

- **多任务并发取消**（当前单队列，仅当前在飞可取消）。
- **取消后的「重试」按钮**（后置）。
- **GPU 端到端**：取消后显存回落 + 无残留 Python（待用户确认 GPU 空闲 + 明确同意）。

---

## Step 9C.8-B2（日期：2026-09-24）

> **新增说明（Step 9C.8-B2 · 取消按钮样式 + 重新生成 + 编辑取消回退）**
>
> 本段为 9C.8-B 的**用户 UI 反馈**修订。**无契约变更**（`Contracts` 零修改）；仅 UI / App 行为。

### 9C.8B2.1 取消 / 重新生成按钮（冻结 · 非契约）

- 生成中气泡的取消按钮改用**关闭窗体的 × 样式**（`Path.bubbleIcon` + 标题栏同款 × 几何），
  底为 **75% 透明圆角底**（`Button.bubbleAction`：`#40FFFFFF` 圆角 6；`bubbleClose` 变体 hover 红）。
- **生成完成后**：该位置的按钮变为**「重新生成」（重跑）**按钮（`IconRefresh`，Tabler 同源，
  新增于 `TablerIcons.axaml`）。仅对可重跑节点（有父节点）显示（`SessionViewModel.CanRerun`）。
- 重跑入口与历史节点右键「重跑」共用 `MainWindow.RerunAsync`。

### 9C.8B2.2 编辑取消 = 回退（冻结 · 非契约）

- **提交（编辑）取消**：不再显示「已取消。」气泡；`SessionViewModel` **回退聊天**（`RebuildContext`，
  移除本次 User + pending 气泡），并置 `LastRunCanceled=true`。
- `MainWindow` 在提交后若 `LastRunCanceled`：把**提示词打回输入框**、**附件打回附件条**
  （`ImageImportBar.AddFiles`，快照自发送前）。
- **重跑取消**：语义不变（原 AI 气泡「已取消。」+ 保留旧图）。
- 新增 `SessionViewModel.LastRunCanceled` / `CanRerun(nodeId)`。

### 9C.8B2.3 实现清单（冻结 · 非契约）

| 类 / 改动 | 文件 |
|---|---|
| `IconRefresh`（Tabler 同源） | `App/Assets/Icons/TablerIcons.axaml` |
| `Button.bubbleAction`（+`.bubbleClose`）/ `Path.bubbleIcon` | `App/Styles/ChromeStyles.axaml` |
| 气泡取消 × / 重新生成按钮 + `BubbleIcon` | `App/MainWindow.axaml.cs` |
| 取消回退 + `LastRunCanceled` + `CanRerun` | `UI/Chat/SessionViewModel.cs` / `SessionViewModel.Rerun.cs` |
| 取消后恢复提示词 / 附件 | `App/MainWindow.Send.cs` |

### 9C.8B2.4 测试结果（冻结）

- `dotnet build ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`SessionViewModelTests` → **47 通过 / 0 失败**；非 GPU 全量
  （排除 `Ipc*` / `PlannerIntegration`）→ **377 通过 / 0 失败**。
- **Z8**：`SessionViewModel.cs` 515 / `SessionViewModel.Rerun.cs` 444 / `MainWindow.axaml.cs` 562，
  均 < 600。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.8B2.5 遗留项（冻结）

- **无根会话取消**：首个附件被 `PrepareAttachments` 提升为 root 时，取消**不回退该 root**
  （仅恢复输入 / 附件）；属边界场景。
- **多任务并发取消** / **GPU 端到端**（同 9C.8B.6）。

---

## Step 9C.9-A1（日期：2026-09-24）

> **修订说明（Step 9C.9-A1 · 命令自动分支 + `/生成`（LLM 扩写））**
>
> 本段为 Step 9C.9-A1 **只增**记录。目标：① 同一命令名按附件数自动选模板（single / multi）
> ② 新增 `/生成`（T2I + 大模型扩写）③ `/生成` 前 LLM 可达性 + 显存预检。**不改动 Step 0–9C.8-B2
> 已冻结行的既有成员**；契约变更为**新增重载 / 新增字段 / 新类型**（走本段追加，已授权）。

### 9C.9-A1.1 `ICommandParser` 新增重载（冻结 · 契约新增，已授权）

| 成员 | 文件 | 说明 |
|---|---|---|
| `ICommandParser.ParseAsync(string, IEditSession, int imageCount, ResolutionPolicy?, CancellationToken)` | `ZivAiEditor.Agent/CommandParser.cs` | `imageCount` = 管线图数（主图 + 参考图）；`-1` = 未知（回退 `defaultVariant`） |

- 既有两重载**保留不动**，均委托新重载并传 `-1`。变体选择在 `CommandParser` 内。
- `Executor.BuildRerunPlanAsync` 改传 `1 + (node.Rerun?.AdditionalImages.Count ?? 0)`（R7）。
- `SessionViewModel.SubmitAsync` 计算 `imageCount = (当前管线图?1:0) + 参考图数` 并传入。
- **`Contracts` 零变更**（`RerunSpec` 不动，图数可从 `AdditionalImages` 推导）。

### 9C.9-A1.2 `CommandDefinition` 新增字段（冻结 · Agent 层）

| 字段 | JSON 键 | 说明 |
|---|---|---|
| `Variants` | `variants` | `{ "single": "...", "multi": "..." }`；为空时用扁平 `Template` |
| `DefaultVariant` | `defaultVariant` | `imageCount < 0` 时选用 |
| `Variadic` | `variadic` | 末位参数吸收整段剩余文本（多词描述）；空 → 报错 |
| `T2i` | `t2i` | 文生图：计划恒为空 `MainImagePath`（即便有 root） |

- `Variants` 在源生成反序列化下**可能为 `null`**（JSON 缺键时），代码按 `is not { Count: > 0 }` 容错。

### 9C.9-A1.3 `Template/commands.json` 结构（冻结 · version 1.1）

- 命令集：`/换背景`(variants single+multi)、`/换装`(variants single+multi)、`/合照`(仅 multi)、
  `/生成`(t2i)、`/去水印`、`/去物体`、`/扩图`。
- `template` 与 `variants` **共存**：无 `variants` 的命令仍用扁平 `template`（去水印 / 去物体 / 扩图）。
- **行为变更（登记）**：`/换背景` 参数由 `target` 改名 `description` 且 `variadic: true`；
  原模板文本移入 `variants.single`（**逐字不变**），新增 `variants.multi`。
- 变体选择规则：`T2i` → `single`；否则 `imageCount >= 2 ? "multi" : "single"`；已知图数而缺该键 →
  **报错**（不回退）；`imageCount < 0` → `defaultVariant`。仅 multi 变体的命令在 `imageCount < 2`
  时报「requires at least 2 images」。
- `BuiltInCommands()` 同步为 7 条（含新字段），文件缺失时行为一致。

### 9C.9-A1.4 新增类型（冻结 · 非契约）

| 类型 | 文件 | 归属 | 说明 |
|---|---|---|---|
| `IPromptExpander` / `PromptExpander` | `ZivAiEditor.Agent/PromptExpander.cs` | Agent | 注入 `ILlmClient`；内置 `/生成` system prompt；Fake 可测 |
| `LlmPreflightStatus` / `LlmPreflightResult` / `ILlmPreflight` / `LlmPreflight` | `ZivAiEditor.App/LlmPreflight.cs` | App | LLM `/health` 探测 + `IInferenceClient.CheckHealthAsync().VramUsedMb` 预检 |
| `PromptChoice` / `PromptConfirmDialog` | `ZivAiEditor.App/PromptConfirmDialog.axaml(.cs)` | App | 确定 / 重写 / 取消；可滚动只读扩写结果 |
| `CommandRequirements` | `ZivAiEditor.App/CommandRequirements.cs` | App | D3 发送前门控（纯函数，可测） |

### 9C.9-A1.5 预检语义（冻结 · 非契约）

- **LLM 可达性**：裸 HTTP `GET {endpoint 去 /v1/chat/completions}/health`（超时 1.5s）；不可达 →
  **硬阻断**「请先启动 LLM 服务」。
- **显存**：`free = VramTotalMb - VramUsedMb`（`vram_used_mb` 为 NVML **设备级**占用，含 LLM 进程）；
  `free < VramNeedMb` → **警告**（非阻断，D7）。默认 `16376` / `9800` MB。
- LLM 可达但后端显存读取失败 → `Ready`（不阻断；正常提交流程会暴露后端错误）。

### 9C.9-A1.6 配置（冻结 · 非契约）

- `settings.ini` / `settings.ini.template` 新增 `[llm.rewriter]`（endpoint / model / temperature=0.7 /
  max_tokens / enable_thinking / timeout_seconds / vram_total_mb / vram_need_mb）。
  **取代** Step 5 补完登记的预留段注释 `[llm.prompt_rewriter]` / `[llm.multi_image]`（段名以本段为准）。

### 9C.9-A1.7 外部规范声明（冻结）

- `/生成` 的 system prompt 按**用户提供的官方 Qwen-Image-2.1 T2I 8 步观察者散文规范**落地为
  **内置常量**；本仓库内**无该规范原文**（`Comfyui/blueprints/*.json` 与 `DOC/OPTIMIZATION.md`
  均未含），无法在仓库内交叉验证，属**外部规范引用**。

### 9C.9-A1.8 `/生成` 流程（冻结 · 非契约）

- 发送 `/生成 <描述>` → 预检 → LLM 扩写 → `PromptConfirmDialog`（确定 / 重写 / 取消）→
  确定后提交 `/生成 <扩写>`，`displayText` = 原始输入。
- `node.Command` = `/生成 <扩写>`；`SessionViewModel.SubmitAsync` 新增可选 `displayText`
  （气泡显示原始输入，`node.Command` 仍存完整命令）。
- **重跑**：重新解析 `node.Command`（`{description}` = 扩写）→ T2I；**不再次调用 LLM**，确定性。
- `/生成` 不使用附件；有附件时提示「文生图不使用附件，已忽略」并清空附件条。

### 9C.9-A1.9 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：受影响类（`CommandParserTests` / `SessionViewModelTests` /
  `ExecutorTests` / `PromptExpanderTests` / `PreflightTests` / `CommandRequirementsTests`）
  → **98 通过 / 0 失败**；非 GPU 全量（排除 `Ipc*` / `PlannerIntegration`）→ **398 通过 / 0 失败**
  （9C.8-B2 基线 377，本步 +21）。
- **Z8**：改动源文件均 < 600（最大 `CommandParser.cs` 523 / `SessionViewModel.cs` 520）。
- **未跑 GPU 端到端**（Z29 / Z30）；LLM 扩写 / 预检由 Fake 覆盖。

### 9C.9-A1.10 遗留项（冻结）

- **批量（N 图 → N 结果）** → 后续。
- **命令列表 UI 补全（`/` 自动补全）** → 后置。
- **`/生成` 气泡在会话重载后显示 `node.Command`（扩写文本）**，与实时气泡（原始输入）不一致 → 后置。
- **WD14 打标（9C.9-B）/ VLM 反推（9C.9-C）/ `@图引用` UI** → 后置。

---

## Step 9C.10-P1（日期：2026-09-24）

> **修订说明（Step 9C.10-P1 · 节点图包数据模型：`ImagePaths` / `UsedImagePaths` + `session.json` v2）**
>
> 本段为 Step 9C.10 分阶段重构的 **P1**。目标：把「单图节点」升级为「图包节点」的数据底座——
> `EditNode` 增加 `ImagePaths`（图包）/ `UsedImagePaths`（本次管线输入），`session.json` 升 v2
> （`image_paths` / `used_image_paths`）并**兼容读 v1**。**不改动 Step 0–9C.9-A1 已冻结行的既有成员**
> （契约**仅追加**）；本段为**纯追加**（文件尾部）。**P1 行为不变**（root = 单图、编辑 = 单图）；
> 多图行为在 **P2** 启用（需授权修订 9C.6-C.4）。

### 9C.10-P1.1 契约新增（冻结 · 追加 → 非破坏）

| 成员 | 文件 | 说明 |
|---|---|---|
| `IEditNode.ImagePaths`（`IReadOnlyList<string>`） | `Contracts/Planning/IEditNode.cs` | 节点图包（非空）：root = `[导入图]`，编辑节点 = `[输出]`；`ImagePath == ImagePaths[0]` |
| `IEditNode.UsedImagePaths`（`IReadOnlyList<string>`） | 同上 | 本次编辑消费的**有序**管线图（`image1..imageN`，主图在前）；root 恒空 |
| `IEditSessionWriter.SetNodeUsedImages(string nodeId, IReadOnlyList<string> imagePaths)` | `Contracts/Planning/IEditSessionWriter.cs` | 原地设置节点 `UsedImagePaths`；同 crop / mask / rerun，**不新增节点**；空白项丢弃；未知 nodeId → no-op |

- **既有成员零修改**：`IEditNode.ImagePath` 保留为「主图 = `ImagePaths[0]`」的兼容字段。

### 9C.10-P1.2 `EditNode` 新增字段与写入（冻结 · Agent 层）

| 字段 | 默认 | 说明 |
|---|---|---|
| `IReadOnlyList<string> ImagePaths` | 空 | `EditSession` 插入时**归一化为非空** |
| `IReadOnlyList<string> UsedImagePaths` | 空 | 编辑节点提交时写入；root 恒空 |

- `ResetToRoot` / `AppendNode`：`ImagePaths = [imagePath]`；`Restore`：`ImagePaths` 为空则回退 `[ImagePath]`。
- `SetNodeCrop` / `SetNodeMask` / `SetNodeRerun` 与 `ReplaceNodeImage` 均**保留**两字段；`ReplaceNodeImage`
  置 `ImagePaths = [newImagePath]`、**保留** `UsedImagePaths`。
- `SetNodeUsedImages` + 私有 `NormalizeImages` 落在 **`Agent/EditSession.Images.cs`**（Z8 拆分；`EditSession` 已 `partial`）。

### 9C.10-P1.3 `session.json` 格式 v2（冻结）

- `SessionStore.FormatVersion` **1 → 2**。
- `SessionFileNode` 新增：`image_paths`（图包相对名；首图 `{NodeId}.png`，其余 `{NodeId}_{n}.png`，n ≥ 2）、
  `used_image_paths`（有序，主图在前）。
- 旧 `image_path` 改为**只读兼容字段**（`JsonIgnore(WhenWritingNull)`；v2 不再写出）。
- **v1 兼容读**：`image_path` → `ImagePaths = [image_path]`；`SessionLoader` 接受 `version ∈ [1, 2]`；
  `TryReadInfoAsync` 列出 v1..v2（旧项目不从项目列表消失）。
- **缺图语义**：图包中缺失的图逐个忽略 + 警告；**全部缺失才跳过节点**（v1 单图缺失 → 原「跳过」行为不变）。
- **文件布局（追加）**：`refs/`（重跑参考，9C.8-A）之外新增 `used/{nodeId}_{n}{ext}`（管线输入副本）；
  同源文件已被拷贝则**复用**（相对名映射），不重复。
- `SessionStore.Cleanup.DeleteNodeArtifacts` 追加删除 `{nodeId}_*.png` 与 `used/{nodeId}_*`。
- **拆分**：拷贝 / 映射助手（`CopyReferenceImages` / `CopyNodeImage` / `CopyUsedImages` / `Remember` /
  `TryRemembered`）落在 **`Agent/SessionStore.Images.cs`**（Z8 拆分；`SessionStore` 已 `partial`）。

### 9C.10-P1.4 UI 写入（冻结 · 非契约）

- `SessionViewModel.SubmitAsync` 成功后：`_writer.SetNodeUsedImages(appended.NodeId, BuildUsedImages(plan))`；
  `BuildUsedImages = [plan.MainImagePath] + plan.AdditionalImages`（去空白，主图在前），见 `SessionViewModel.Rerun.cs`。
- 对 `SessionLoader` 的 `dto.Nodes` / `rerun.additional_images` 补 **null 兜底**（源生成在缺键时该 `List` 为 `null`）。

### 9C.10-P1.5 测试结果（冻结）

- `dotnet build ZivAiEditor.UI -c Release`（含 Contracts / Agent / Backend / Tools）→ **0 错误 0 警告**。
- `dotnet test`（Z29，**无 GPU**）：`EditSessionTests` / `SessionStoreTests` / `SessionLoaderTests` /
  `SessionViewModelTests` / `ContractsSmokeTests` → **134 通过 / 0 失败**。
- **Z8**：`EditSession.cs` 559 / `EditSession.Images.cs` 73 / `SessionStore.cs` 556 /
  `SessionStore.Images.cs` 140，均 < 600。
- **未跑 GPU 端到端**（Z29 / Z30）；P1 行为不变。

### 9C.10-P1.6 遗留项（冻结）

- **P2**：空会话拖入即 root（含 N 图）+ 多图 UI（**授权修订 9C.6-C.4**）。
- **P3**：`@` 引用机制（UI 层；不改 `ICommandParser` 签名）。
- **P4**：重跑 / 删除适配（`RerunSpec` 瘦身为仅 `Resolution`）。
- **P5**：批量（独立按钮）。
- **已知观察项（P3 复核）**：`session.json` 拷贝复用映射（`copied`）在「`UsedImagePaths` 与
  `Rerun.AdditionalImages` 不一致」时需确认不会跨节点别名。

---

## Step 9C.10-P2（日期：2026-09-24）

> **修订说明（Step 9C.10-P2 · 空会话拖入即 root（含 N 图）+ 多图 UI）**
>
> 本段为 Step 9C.10 的 **P2**。启用「图包节点」行为：空会话拖入 N 张 → **整批立即成为 root 节点**
> （`ImagePaths` 含 N 张）；聊天流「起始图像」气泡与历史列表显示多图；管线消费 root 图包（Q1=A）。
> **授权修订 Step 9C.6-C.4**（仅空会话场景的**部分回退**：0→N 即时提升为 root、附件条清空）。
> **契约仅追加**（`IEditSessionWriter.SetRoot(IReadOnlyList<string>)`）；本段为**纯追加**（文件尾部）。
> 用户裁决：Q1=A / Q2=B / Q3=B / Q4=A / Q5=B / Q6=A（见 `INTERACTION.md` §12）。

### 9C.10-P2.1 契约新增（冻结 · 追加 → 非破坏）

| 成员 | 文件 | 说明 |
|---|---|---|
| `IEditSessionWriter.SetRoot(IReadOnlyList<string> imagePaths)` | `Contracts/Planning/IEditSessionWriter.cs` | 多图 root：清 DAG → 建 root（`ParentNodeId=null`、`ImagePath=imagePaths[0]`、`ImagePaths=imagePaths`、`UsedImagePaths` 空、`Command="原图"`）→ `CurrentNodeId=root`；空列表 no-op。既有 `SetRoot(string)` / `ResetToRoot(string)` 不变 |

- **无新增读契约**：当前图包由 UI 从 `IEditSession.GetPathToCurrent()` 末节点派生（不扩契约面）。

### 9C.10-P2.2 行为变更（冻结 · 授权修订 9C.6-C.4）

- **空会话 0→N 即时提升为 root**（R5）：`MainWindow.Import.OnImagesChanged` 在
  `CountBefore==0 && CountAfter>0 && !HasRootImage` 时把**整批**提升为多图 root，随后清空附件条；
  清空的再入事件（`CountBefore>0, CountAfter==0`）不重复提升（无循环）。纯判定抽为
  `ImageImportPromotion.ShouldPromote`。
- **已有 root 拖入仍只进附件条**（9C.6-C 行为不变）；`MultiImagePromptDialog` **保留**（Q3=B，P3 再议）；
  `ImageEditMode` **保留**（Q6=A，发送门控语义不变）。
- **上限**（Q2=B）：root 图包 ≤ **10**（`SessionViewModel.MaxRootImages`，超出截断 + 提示）；
  送管线 ≤ **4**（主图 + ≤3 参考；超限截断尾部 + 提示）。

### 9C.10-P2.3 管线输入语义（冻结 · 非契约）

- **Q1=A**：无 `@` 时管线消费**当前节点的整个图包**——`<image1>` = pipeline 主图
  （`GetCurrentPipelineImagePath()`，crop 优先），`<image2>..` = `ImagePaths[1..]`，其后为附件参考图。
- **R1**：parser 变体选择 `imageCount = CurrentImageCount + 附件数`。
- **R4**：`CommandRequirements.RequiresMoreImages` 的 `bool hasRootImage` 参数改为 `int currentImageCount`；
  `effective = currentImageCount + attachmentCount`（App 层内部签名，非契约）。
- **R2**：图片消费型提交（`plan.MainImagePath` 非空）在聊天流插入**非错误**系统行「本次使用 N 张图」
  （N = 主图 + 参考，截断后实际进管线数）。**T2I（`/生成`，`MainImagePath` 为空）不挂参考图、不发该行**（修复复审 P1）。

### 9C.10-P2.4 UI（冻结 · 非契约）

- 聊天流「起始图像」气泡：`ChatMessage` 追加 `IReadOnlyList<string> ImagePaths`（**保留** `ImagePath`）；
  >1 张时横排 72px 缩略图（>4 显示 "+N"），缩略图经既有 `_bitmaps` 跟踪并在聊天流重建 / 关闭时释放（Z9）。
- 历史列表：`ImagePaths.Count > 1` 时标签为「{命令}（N 张）」（Q5=B）。
- 新增纯逻辑 `ImageImportPromotion.ShouldPromote(countBefore, countAfter, hasRootImage)`（`UI/Editing`，可测）。

### 9C.10-P2.5 拆分（Z8）

- `SessionViewModel.Images.cs`（新 partial）：`SetRootImage(list)` / `CurrentImageCount` / `AddInfo` /
  图包组装助手（`CurrentPackExtras` / `AssembleReferences` / `BuildDisplayPack` / `NormalizeRootImages`）。
- `EditSession.SetRoot(list)` 落在既有 `EditSession.Images.cs`。

### 9C.10-P2.6 测试结果（冻结）

- `dotnet build src\ZivAiEditor.UI -c Release`（含 Contracts/Agent/Backend/Tools）→ **0 错误 0 警告**；
  `ZivAiEditor.App` Debug 构建 0/0（Release 输出被运行中实例锁，非编译错误，无 `error CS`）。
- `dotnet test`（Z29，**无 GPU**）：受影响类 **165 通过 / 0 失败**；**全量 448 通过 / 0 失败**。
- **Z8**：改动源文件均 < 600（`SessionViewModel.cs` 518 / `SessionViewModel.Images.cs` 176 /
  `EditSession.Images.cs` 101 / `MainWindow.axaml.cs` 491 等）。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 9C.10-P2.7 遗留项（冻结）

- **P3**：`@` 引用机制（UI 层）；`MultiImagePromptDialog` 废弃；`ImageEditMode` 语义调整。
- **P4**：重跑 / 删除适配；root 图包 extras 现随 `plan.AdditionalImages` 存进 `RerunSpec.AdditionalImages`
  （P2 行为一致，P4 迁到 `UsedImagePaths`）。
- **P5**：批量。
- **观察项**：图包上限仅在 VM（`SessionViewModel`），手改 v2 JSON 可载入 >10；`used/` 跨节点别名待 P3/P4 复核。
- **未跑 GPU 端到端**（Z29 / Z30）。

---

## 模块边界迁移 · 第 1 步：ICommandParser 上提 Contracts（日期：2026-09-24）

> **契约搬迁（追加 · 签名零变化）**
>
> 依据模块边界设计定稿（Q9：端口接口放 kernel = 现 `ZivAiEditor.Contracts`，保留程序集名）与
> 渐进搬迁策略。本步把命令解析契约从 `ZivAiEditor.Agent` 移到 `ZivAiEditor.Contracts.Planning`，
> 消除 `ZivAiEditor.UI` → `ZivAiEditor.Agent` 的编译期依赖。**只挪位置 + 改命名空间，方法签名逐字不变**（G3 / G4）。

### 迁移.1 契约（搬迁 · 签名不变）

| 类型 | 原位置 | 新位置 | 说明 |
|---|---|---|---|
| `ICommandParser` | `Agent/CommandParser.cs`（`ZivAiEditor.Agent`） | `Contracts/Planning/ICommandParser.cs`（`ZivAiEditor.Contracts.Planning`） | 三个 `ParseAsync` 重载签名零变化 |
| `ParseResult` | 同上 | `Contracts/Planning/ParseResult.cs` | 成员零变化 |
| `CommandDefinition` | 同上 | `Contracts/Planning/CommandDefinition.cs` | 成员零变化（含 `[JsonPropertyName]`） |

- 实现类 `CommandParser : ICommandParser`、`CommandsFileDto`、`CommandJsonContext`、`BuiltInCommands()` 保留在 `ZivAiEditor.Agent`（实现细节）。

### 迁移.2 依赖解耦（UI）

- `ZivAiEditor.UI/Chat/SessionViewModel.cs`：删除 `using ZivAiEditor.Agent;`（`ICommandParser` 现来自 Contracts）。
- `ZivAiEditor.UI.csproj`：**删除**对 `ZivAiEditor.Agent` 的 `ProjectReference`（保留 Tools / Backend）。
- UI 对 Agent 的引用面归零：`grep "using ZivAiEditor.Agent" src/ZivAiEditor.UI/` → 零命中。

### 迁移.3 调用点同步（namespace 迁移）

- `App/CommandRequirements.cs`：`using ZivAiEditor.Agent;` → `using ZivAiEditor.Contracts.Planning;`。
- `App/MainWindow.axaml.cs`：追加 `using ZivAiEditor.Contracts.Planning;`（保留 Agent，仍需 `EditSession` / `SessionStore`）。
- `Tests/CommandRequirementsTests.cs`：追加 `using ZivAiEditor.Contracts.Planning;`（保留 Agent，仍需 `CommandParser`）。

### 迁移.4 验收（无 GPU，Z29）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `grep "using ZivAiEditor.Agent" src/ZivAiEditor.UI/` → **零命中**；`ZivAiEditor.UI.csproj` 不再引用 Agent。
- `dotnet test`（Release）：受影响类 **106 通过 / 0 失败**；**全量 448 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 迁移.5 本步不做（范围）

- 不动 `IPlanner` / `IExecutor` / 其他接口、不动 session 域、不改行为逻辑、不拆程序集、不改 `commands.json` 结构。

---

## 模块边界迁移 · 第 2 步：App 不再持 session 具体类型（日期：2026-09-24）

> **契约追加 + 参数窄化（追加式 · 不改既有成员语义）**
>
> 依据模块边界设计定稿与第 2 步裁决（Q1 窄化 `SessionStore.SaveAsync`；Q2 空会话方法名 `NewSession()`；
> Q3 `AppContext` ctor 改接口、属性双开；Q4 `SessionLoadResult.Session` 保留具体；Q5 `ExportToAsync` 不动）。
> 目标：`ZivAiEditor.App` 不再 `new` / 持具体 `EditSession`（装配点 `AppContext.Create` 除外），改为持
> `IEditSession`（读）+ `IEditSessionWriter`（写）。

### 迁移2.1 契约追加（`Contracts/Planning`）

| 接口 | 追加成员 | 说明 |
|---|---|---|
| `IEditSession` | `DateTimeOffset CreatedAt { get; }` | 会话创建时间（持久化为项目 `created_at`）。`EditSession` 已有该属性（`get`/`set`），无需改实现 |
| `IEditSessionWriter` | `void NewSession();` | 就地重置为全新空会话（清 DAG / root / current，采纳新 `SessionId` / `CreatedAt`）；等价于新建 `EditSession` 的初始态 |
| `IEditSessionWriter` | `void Restore(IReadOnlyList<IEditNode> nodes, string? currentId, string sessionId, DateTimeOffset createdAt);` | 就地按持久化项目重建会话（原 `EditSession.Restore` 的公共签名，现升为契约） |

- 既有成员语义零改动（追加式）；`EditSession` 新增 `public void NewSession()`，`Restore` 原已实现。

### 迁移2.2 `SessionStore` 参数窄化（Agent · 非契约）

- `SessionStore.SaveAsync` 与私有 `WriteProjectAsync` 的参数 `EditSession` → **`IEditSession`**（行为不变；
  仅经 `GetHistory()` / `SessionId` / `CurrentNodeId` / `CreatedAt`）。
- `SessionStore.ExportToAsync` **不动**（Q5；仍取具体 `EditSession`，UI 未用）。

### 迁移2.3 App 装配（不再持具体类型）

- `AppContext`：ctor 参数 `EditSession session` → `IEditSession session, IEditSessionWriter sessionWriter`；
  属性 `Session` 类型 → `IEditSession`，新增 `SessionWriter: IEditSessionWriter`（同一实例）。
  `Create` 内 `new EditSession()` 为**唯一装配点**。
- `App.axaml.cs`：`new MainWindow(Session, SessionWriter, …)`。
- `MainWindow`：字段 `_session:IEditSession` + `_writer:IEditSessionWriter`；ctor 参数改接口；
  `new SessionViewModel(session, sessionWriter, …)`。
- `MainWindow.Projects.cs`：`_session.Restore(...)` → `_writer.Restore(...)`（打开项目）；
  `ResetToEmptyProjectAsync` 的 `new EditSession()` + `Restore(...)` → **`_writer.NewSession()`**（清空项目）。

### 迁移2.4 验收（无 GPU，Z29）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `grep "\bEditSession\b" src/ZivAiEditor.App/`：具体类型仅 `AppContext.cs` 装配点 `new EditSession()`；
  `MainWindow` / `App` 均只引用 `IEditSession` / `IEditSessionWriter`。
- `dotnet test`（Release）：**全量 448 通过 / 0 失败**。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 迁移2.5 本步不做（范围）

- 不新建 `ISessionReader` / `ISessionWriter` / `SessionService`；不重命名接口；不动 `session.json` 格式；
  不抽 project 域（第 4 步）/ execution 域（第 5 步）；`SessionLoadResult.Session` 保留具体（Q4）。

---

## 模块边界迁移 · 第 3 步：project 域抽取（日期：2026-09-24）

> **编目所有权迁移（追加 · 不改磁盘格式）**
>
> 依据第 3 步裁决（Q1 选项 2：`ProjectService` 拥有编目 `GetDirectory` / `DeleteAsync` / `RenameAsync` /
> `GetLastProjectId` / `SetLastProjectIdAsync` / `ListAsync`；`SessionStore` 保留会话读写 +
> 元数据原语；Q2 原语留 `SessionStore`；Q3 `ProjectSummary` 上提 Contracts、`ProjectInfo` 替换；
> Q4 不实现 `NewDirectory`；Q5 `MainWindow.Projects.cs` 瘦身 <300；Q6 保持异步）。
> 不新建项目/程序集，`ProjectService` 与 session 同处 Agent；不动磁盘格式。

### 迁移3.1 新增类型

| 类型 | 文件 | 说明 |
|---|---|---|
| `ProjectSummary`（record） | `Contracts/Planning/ProjectSummary.cs` | `SessionId` / `Name` / `CreatedAt`；**取代**原 Agent 的 `ProjectInfo`（后者删除） |
| `ProjectService`（类） | `Agent/ProjectService.cs` | 项目编目门面：`ListAsync` / `DeleteAsync` / `RenameAsync` / `GetDirectory` / `GetLastProjectId` / `SetLastProjectIdAsync` / `RootDirectory` |
| `SessionSignature`（静态类） | `Agent/SessionSignature.cs` | 纯脏签名 `Compute(IEditSession)`（含文件戳） |
| `ProjectNaming`（静态类） | `Agent/ProjectNaming.cs` | `Unnamed` 常量 + `EffectiveName(explicitName, rootImagePath)` |

### 迁移3.2 `SessionStore` 收敛（会话读写 + 元数据原语）

- **保留**：`SaveAsync`（返回改 `ProjectSummary`）/ `LoadAsync` / `ExportToAsync` / `DeleteNodeArtifacts`；
  新增公开元数据原语 `ReadMetadataAsync(directory, ct)`（原 `TryReadInfoAsync`，改实例方法、返回 `ProjectSummary?`）
  与 `WriteMetadataNameAsync(directory, name, ct)`（原 `RenameAsync` 的 JSON 改写体）。
- **移出**（至 `ProjectService`）：`ListAsync` / `DeleteAsync` / `RenameAsync` / `GetLastProjectId` /
  `SetLastProjectIdAsync` / 公开 `GetProjectDirectory`（改私有 `GetDirectory`）；`last_project.txt` 逻辑随迁。
- `ProjectInfo` 记录删除；`ProjectFormatException` / `ProjectCorruptException` 不动。

### 迁移3.3 App 装配与瘦身

- `AppContext`：新增 `ProjectService Projects` 属性；`Create` 内 `new ProjectService(sessionStore)`。
- `MainWindow`：新增字段/ctor 参数 `ProjectService _projects`（`_store` 仍用于会话保存/加载/清理）。
- `MainWindow.Projects.cs`：编目调用改走 `_projects`；`ComputeSignature` → `SessionSignature.Compute(_session)`；
  `EffectiveProjectName` → `ProjectNaming.EffectiveName(...)`；`UnnamedProject` → `ProjectNaming.Unnamed`。
- 新增 `MainWindow.ProjectList.cs`：左侧项目列表 UI（`InitProjects` / `BuildProjectRow` / `RefreshProjectListAsync` /
  防抖 open timer / `FlushPendingMaskAsync`）。
- **Z8**：`MainWindow.Projects.cs` **260 行**（<300 达标，原 515）；`MainWindow.ProjectList.cs` 191。


### 迁移3.4 测试

- 编目用例（`List_Returns_Saved_Projects_Newest_First` / `Delete_Removes_Project_Directory` /
  `Last_Project_Id_RoundTrips_And_Clears` / `List_Skips_Unsupported_Version`）从 `SessionStoreTests`
  搬到新 **`ProjectServiceTests`**；`SessionStoreTests` 保留会话读写 + 清理用例并改用 `Path.Combine` 定位。

### 迁移3.5 验收（无 GPU，Z29）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Release）：**447 通过 / 1 失败**（`IpcIdleUnloadTests.Idle_Timeout_Unloads_Then_Reloads_On_Next_Submit`，
  GPU 后端空闲卸载后 VRAM 15s 回落窗口的时序抖动；**孤立重跑通过**，与本次会话/编目改动无关）。
- **未主动跑 GPU 端到端新增项**（Z29 / Z30）。

### 迁移3.6 本步不做（范围）

- 不动 `SaveAsync` / `LoadAsync` / `ExportToAsync`；不动 `session.json` 格式 / `last_project.txt` 格式；
  不抽 execution / ui / shell / imaging 域；不改项目列表 UI 行为；不实现 `NewDirectory`；无新 NuGet。

---

## 模块边界迁移 · 第 4 步：imaging 域抽取 + 三域门面化（日期：2026-09-24）

> **新项目 + 端口 + 全注入（追加 · 不改行为）**
>
> 依据第 4 步裁决（Q1 新建 `ZivAiEditor.Imaging`；Q2 全注入；Q3 `IImagingService` 成员集；
> Q4 `CleanupAll` 移入 `AppContext.Create`；Q5 inference/tools 不新增接口、删 `AppContext.Backend`；
> Q6 测试迁命名空间）。把栅格操作从 UI 层抽到独立 imaging 域，UI/App 只经端口访问。

### 迁移4.1 新项目 `ZivAiEditor.Imaging`

- `src/ZivAiEditor.Imaging/`（net8.0；引用 `Contracts` + `ZIV.Core` + `ZIV.Imaging` + `SkiaSharp` 包，
  CPM 版本复用）；加入 `ZIV.AI.sln`（sln 仍只含 `ZivAiEditor.*`，ZIV 项目不入 sln）。
- 迁入并改命名空间为 `ZivAiEditor.Imaging`：`ImageCropper`、`MaskExporter`、`MaskFeather`；
  新增门面实现 `ImagingService : IImagingService`。
- **`MaskFeather` 改为 `internal`**（`<InternalsVisibleTo Include="ZivAiEditor.Tests" />`）；UI 不再可见。

### 迁移4.2 契约端口（`Contracts/Imaging/IImagingService.cs`）

`CropAsync(sourceImagePath, sessionId, nodeId, x, y, width, height, ct)` /
`ExportMaskAsync(sessionId, nodeId, pixels, width, height, featherPx, ct)` /
`LoadMaskAsync(path, ct)` / `ResolveMaskPath(sessionId, nodeId)` /
`FeatherMask(pixels, width, height, radiusPx)` / `CleanupSession(sessionId?)` / `CleanupAll()`。

### 迁移4.3 全注入（UI / App 只经端口）

- UI：`SessionViewModel` ctor 增可选参数 `IImagingService? imaging = null`（测试免改；生产注入）；
  `SetRootImage` / `ApplyRequest` 的 crop+mask 清理合并为 `_imaging?.CleanupSession(...)`。UI 项目
  **不引用** `ZivAiEditor.Imaging`（仅 Contracts）。
- App：`AppContext` 构造 `ImagingService` 并 `CleanupAll()`，暴露 `Imaging` 端口属性；**删除**
  `AppContext.Backend`（`PythonProcessManager` 改私有字段）。`MainWindow` 增 `_imaging` 并传入
  `SessionViewModel` 与 `ImagePreview`；`MainWindow.Projects` / `App.axaml` 退出清理走 `_imaging` /
  `ctx.Imaging`；`Program.cs` 删除启动 `CleanupAll`。
- 控件：`ImagePreview` 增 ctor 注入 + 无参重载（Avalonia `x:Class` 加载器需要；生产用注入重载）；
  `CropAsync` / `LoadMaskAsync` / `ExportMaskAsync` / `ResolveMaskPath` 走端口；`MaskOverlay` 增
  `Imaging` 属性（由 `ImagePreview` 设置），显示羽化走 `IImagingService.FeatherMask`。

### 迁移4.4 inference / tools

- **不新增**门面接口：`IInferenceClient`（Contracts）与 `IToolRegistry`（Contracts）即门面。
- 删除无消费者的 `AppContext.Backend`（原暴露 `PythonProcessManager` 机制类型）。

### 迁移4.5 验收（无 GPU，Z29）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**（`ImagePreview` 加无参重载消除 AVLN3001）。
- `dotnet test`（Release）：**448 通过 / 0 失败**。
- `grep "ImageCropper\.\|MaskExporter\.\|MaskFeather\." src/ZivAiEditor.App src/ZivAiEditor.UI` → **0 命中**。
- `grep "ZivAiEditor.Imaging" src/ZivAiEditor.UI` → **0 命中**。
- **未跑 GPU 端到端新增项**（Z29 / Z30）。

### 迁移4.6 本步不做（范围）

- 不动 execution 域 / ui 域瘦身 / shell 域 / flows 层；不改 `CropState` / `MaskState` / overlay 渲染行为；
  不改 python / ipc-protocol / chrome / 项目列表 / 分辨率 / 重跑 / 取消 / 模板；无新 NuGet。

---

## 模块边界迁移 · 第 5 步：shell 域收拢（日期：2026-09-24）

> **App 层内 shell 域 + 门面（追加 · 不改行为）**
>
> 依据第 5 步裁决（Q1 方案 1：门面 + 非 XAML 文件入 `Shell/`，命名空间不变；Q2 `ShellService` 持
> `SingleInstance`，`Program.Main` 先建；Q3 省略 URL 协议 / 文件夹选择；Q4 专用对话框留直连；
> Q5 `TemplateDirectory` 抽到 shell；Q6 不拆项目；Q7 `LoadSettings` 返回 internal `BackendSettings`）。
> 平台服务收拢为 `ZivAiEditor.App` 内 `Shell/` 文件夹与 `ShellService` 门面。

### 迁移5.1 shell 域结构与门面

- 新增 `App/Shell/` 文件夹；`SingleInstance.cs` / `SettingsLoader.cs` **迁入**（`namespace ZivAiEditor.App`
  不变，无 `x:Class` 影响）。
- 新增 `App/Shell/ShellService.cs`（`internal sealed`，`IDisposable`）门面成员：
  `IsFirstInstance` / `LaunchRequested`（事件）/ `SendToExistingInstance` / `ProgramDirectory` /
  `TemplateDirectory` / `LoadSettings()` / `PickImagesAsync` / `PickSaveFileAsync` / `ConfirmAsync` /
  `PromptAsync` / `ApplyChrome`。
- **省略**：`RegisterUrlProtocol()`（无实现）、`PickFolderAsync()`（无调用者）——Q3。
- 对话框（`ConfirmDialog` / `TextPromptDialog`）与 chrome 控件物理**留原位**，门面包装；专用选择框
  `MultiImagePromptDialog` / `PromptConfirmDialog` 留直连（Q4）。
- `PickSaveFileAsync` 增加 `suggestedDirectory` 参数以保留「另存为起始目录 = root 图目录」行为。

### 迁移5.2 App 调用点改经门面

- `Program.cs`：`new ShellService()`（含单实例）→ `IsFirstInstance` / `SendToExistingInstance` →
  `app.Shell = shell`。
- `App.axaml.cs`：`SingleInstance` 属性 → `ShellService? Shell`；`AppContext.Create(Shell!)`；
  `Shell.LaunchRequested` 订阅（替代 `PathReceived`）。
- `AppContext`：`Create(ShellService shell)`；`settings = shell.LoadSettings()`；
  `CommandParser(Path.Combine(shell.TemplateDirectory, "commands.json"))`；删除本地 `ResolveCommandsPath`。
- `MainWindow`：注入构造改 `internal`（`ShellService` 为 internal）；持 `_shell`；`ApplyChrome(this)`；
  确认/文本/文件名对话框、picker、图片导入、另存为、项目保存/删除/改名均经门面。
- `ImagePreview`：移除自身 `ChromeBehavior.Init`（改由 `MainWindow` 调 `_shell.ApplyChrome(preview)`）。

### 迁移5.3 验收（无 GPU，Z29）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Release）：**448 通过 / 0 失败**。
- `grep "\bStorageProvider\b\|\bConfirmDialog\.ShowAsync\|\bTextPromptDialog\.ShowAsync"`
  `src/ZivAiEditor.App` → 仅 `Shell/ShellService.cs`（4 命中）。（子串匹配会额外命中
  `MainWindow.Generate.cs` 的 `PromptConfirmDialog.ShowAsync`——Q4 保留的专用框，非本次收拢对象。）
- **未跑 GPU 端到端新增项**（Z29 / Z30）。

### 迁移5.4 本步不做（范围）

- 不抽 flows 层（第 6 步）；不瘦 UI / execution；不拆 `ZivAiEditor.Shell` 项目；不改 chrome 视觉 /
  对话框外观 / `settings.ini` 格式 / 模板机制；无新 NuGet。

---

## 模块边界迁移 · 第 6 步：ui 域瘦身 + flows 层引入（日期：2026-09-24）

> **flows 层（App 层内）+ UI 端口 + 单份实现（追加 · 不改行为）**
>
> 裁决：W1 `MainWindow` 二段式装配；W2 `VM` 保留可观察状态、`FlowRunner` 经 `SetXxx` 写回；
> W3 `FlowRunner.GenerateAsync` 承载域编排、MainWindow 保留视图前后；**W4(ii) 单份实现 + 测试机械改造**
> （不做「旧实现 + 端口」双份，避免分叉）。
> 端口放 UI（方案 A），消除 `VM → FlowRunner` 的循环依赖。

### 迁移6.1 新增类型

| 类型 | 文件 | 说明 |
|---|---|---|
| `IEditFlowRunner`（接口） | `ZivAiEditor.UI/Chat/IEditFlowRunner.cs` | UI 侧端口：`SubmitAsync` / `RerunNodeAsync` / `CancelCurrent`（签名与 VM 原方法逐字一致） |
| `ChatFlowRules`（静态类） | `ZivAiEditor.UI/Chat/ChatFlowRules.cs` | 纯规则/投影（`CanSend` / `CanRerun` / `AssembleReferences` / `BuildDisplayPack` / `NormalizeRootImages` / `IsSuccess` / `IsOutOfMemory` / `BuildFailureMessage` / `BuildUsedImages` / `NormalizeAdditionalImages` / `WithAdditionalImages` / `BuildRerunSpec` / `DeleteArtifact` / `FindAssistantMessage` / `FindNode` / `CurrentPack(Extras)` / `PipelinePath`），VM 与 FlowRunner **共用**（不分叉） |
| `FlowRunner`（类，partial） | `ZivAiEditor.App/Flows/{FlowRunner,FlowRunner.Submit,FlowRunner.Rerun,FlowRunner.Generate,FlowRunner.Cancel}.cs` | `: IEditFlowRunner`；持 VM + 域端口（session/writer/parser/executor/expander/preflight/`nodeArtifactsCleaner`）；CTS / `_cancelRequested` 在此 |
| `GenerateOutcome`（枚举） | `FlowRunner.Generate.cs` | `Canceled` / `Failed` / `Submitted` |
| `FlowRunnerHarness`（测试） | `ZivAiEditor.Tests/Helpers/FlowRunnerHarness.cs` | 二段式装配 VM + FlowRunner |

### 迁移6.2 `SessionViewModel` 瘦身（职责）

- **保留（UI 状态 + 用户意图）**：`Messages` / `History` / `IsBusy` / `Resolution` / `Mode` /
  `LaunchOptions` / `LastRunCanceled`；`Start` / `ApplyRequest` / `NavigateTo` / `SetNodeCrop` /
  `SetNodeMask` / `SetRootImage` / `PrepareAttachments` / `StartNewSessionFrom` / `CanSend` /
  `CanRerun` / `RefreshHistory` / `RebuildContext` / `ReplacePending` / `AddHint` / `AddInfo` /
  `GetParent*`。
- **转发（薄 wrapper → 端口）**：`SubmitAsync` / `RerunNodeAsync` / `CancelCurrent`。
- **写回入口（供 FlowRunner）**：`AttachFlowRunner` / `SetBusy` / `SetLastRunCanceled`；`ReplacePending` /
  `RebuildContext` 提升为 public。
- ctor 收敛为 `(IEditSession, IEditSessionWriter, IImagingService?)`（parser/executor/cleaner 迁 FlowRunner）。
- 三文件合计 **484 行**（原 1168）；`ChatFlowRules` 323 / `FlowRunner.*` 92+174+198+116+53。

### 迁移6.3 装配与调用

- `MainWindow`：`new SessionViewModel(session, sessionWriter, _imaging)` → `new FlowRunner(_vm, session,
  sessionWriter, commandParser, executor, _promptExpander, _llmPreflight, _store.DeleteNodeArtifacts)` →
  `_vm.AttachFlowRunner(_flow)`。`Send`/`Rerun`/`Chat` 仍调 `_vm.*`（薄 wrapper）；`Generate` 改调
  `_flow.GenerateAsync(...)` 并保留视图前后（input/附件条/busy/scroll）。
- `AppContext`：域门面已暴露（session/writer/parser/executor/imaging/expander/preflight + store）；
  VM/FlowRunner 在 `MainWindow` 装配（VM 本就在此创建）。
- 无循环依赖：UI 仅依赖自身 `IEditFlowRunner`；App → UI（实现端口 + 持 VM）。

### 迁移6.4 验收（无 GPU，Z29）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Release）：**448 通过 / 0 失败**（`SessionViewModelTests` 58 处构造机械改为 `FlowRunnerHarness.Create`，断言未动）。
- 目标行数：VM 三文件 **484 < 700** 达成；各文件 < 600。
- `grep "ZivAiEditor\.App|App\.Flows" src/ZivAiEditor.UI` → 仅既有注释 `UiPlaceholder.cs:4`（无代码引用）；
  `UI.csproj` 未引用 App。
- **未跑 GPU 端到端新增项**（Z29 / Z30）。

### 迁移6.5 本步不做（范围）

- 不动 execution 域（Executor 仍写会话）；不动 shell / project / imaging；不瘦 views/Avalonia 控件；
  不改行为逻辑；`crop` / `mask` / `navigate` / `import` 流程留 VM；无新 NuGet。

---

## 模块边界迁移 · 第 7 步：剩余迁移 + 收尾 + 收口（日期：2026-09-24）

> **收口 + 端口化 + 文档（只增不改）**
>
> 裁决：D1 execution 域**不搬文件**（登记遗留）；D2 project 域端口**做**；D3 RELEASE-CHECKLIST
> 3 项**只列不改**；D4 flaky 测试窗口**授权修复**；D5 收窄 Executor 注入**不做**；
> D6 删死代码**做**；7-D views 收尾**先判断再下沉**。全部无 GPU 验证（Z29 / Z30）。

### 7A.1 收口（删除死引用 / 死代码 / 修注释）

- `ZivAiEditor.UI.csproj` 删除 `Tools` / `Backend` ProjectReference（UI 代码零引用）。
- 删除死代码 `Backend/HttpInferenceClient.cs`、`UI/UiPlaceholder.cs`。
- 修正过期注释 `ResolutionPicker.axaml.cs` / `LaunchOptions.cs` / `ImageEditMode.cs`。
- 行为不变。

### 7C.1 契约新增（Contracts/Planning）

| 成员 | 说明 |
|---|---|
| `IProjectService`（新） | `RootDirectory` / `GetDirectory` / `ListAsync` / `DeleteAsync` / `RenameAsync` / `GetLastProjectId` / `SetLastProjectIdAsync`，逐字对应 `ProjectService` 公开面 |
| `ISessionPersistence`（新） | `LoadAsync` / `SaveAsync` / `ExportToAsync(IEditSession,…)` / `DeleteNodeArtifacts` |
| `SessionLoadResult`（上提） | 从 `Agent/SessionLoader.cs` 迁至 `Contracts/Planning`；成员不变，仅 `Session` 属性 `EditSession → IEditSession` |

### 7C.2 实现加接口（签名零变化）

- `ProjectService : IProjectService`；`SessionStore : ISessionPersistence`。
- `SessionStore.ExportToAsync(EditSession,…)` 保留为兼容重载，转发到 `ExportToAsync(IEditSession,…)`；
  原逻辑落在 `IEditSession` 版本。

### 7C.3 Q4 字面修订声明

- 第 2 步 2.3「`SessionLoadResult.Session` 保留具体（Q4）」的**字面**以本段为准：`Session` 现为
  只读端口 `IEditSession`。原行不改；`MainWindow` 既有表达式访问无需改动。

### 7C.4 App 装配

- `AppContext` 暴露 `ISessionPersistence SessionStore` / `IProjectService Projects`；具体类仅在
  `AppContext.Create` 内 `new`。`MainWindow` 字段 / 构造改接口。
- `grep "\bProjectService\b|\bSessionStore\b" src/ZivAiEditor.App` → 具体类仅 `AppContext.Create` 装配点。

### 7D1.1 附件消费规则下沉（ChatFlowRules）

- 新增纯规则 `ChatFlowRules.ResolveAttachmentSend(...)` + `AttachmentSendPlan`（record struct）；
  `MainWindow.Send` 改调规则；三选一弹框 / `_vm.StartNewSessionFrom` / 取消回退留视图。
- 判据：「附件→会话结构 / 管线输入」为真编排；其余为视图交互 / 薄调用。
- 新增 `ChatFlowRulesTests`（6 例，非 GPU）。

### 7D2.1 判断结果：MainWindow.Projects **不下沉**

- 编目 / 保存 / 打开 / dirty 与「对话框 owner + `_projectName`/`_savedSignature`/`_opening`
  （被 `OnClosing` / `ProjectList` 共享）+ 列表刷新 + 预览控件 flush」深度交织；下沉需注入 8+
  回调并迁移 3 状态字段，风险 > 收尾收益 → 留原地。
- **登记遗留 7-H「project 流程下沉」**，前置条件：
  1. `RefreshProjectListAsync` 拆为「取数据（域）」+「渲染（视图）」；
  2. 保存询问 owner 注入方式统一（`ShellService` 现为 `Window owner`）；
  3. 明确 `OnClosing` 保存路径归属；
  4. 迁移 `_projectName` / `_savedSignature` / `_opening` 并同步 `ProjectList` / `OnClosing`；
  5. 先补该流程行为测试基线再下沉。

### 7E.1 flaky 测试窗口修复（方案 A）

- `IpcIdleUnloadTests.Idle_Timeout_Unloads_Then_Reloads_On_Next_Submit`：把「等 state 未加载（60s）」
  与「等 VRAM 回落（独立 15s）」合并为单一轮询条件（同一 `CheckHealthAsync`），窗口 30s。
- 阈值 `VramUsedMb < 3000` 沿用原用例；来源为 Step 3 空闲卸载实测回落（回基线 ~1 GB），
  3000 MB 作为「明显回落」的保守上界。
- 仅测试代码；**未 GPU 验证**，登记留待 GPU 环境复验。

### 7.2 本步不做（登记遗留）

- **D1**：execution 域物理划分（`Executor` / `ExecutionQueue` 搬文件夹 / 改命名空间 / 新建项目）
  **不做**；`FROZEN.md` Z18 行与 Step 6 段冻结其归属与文件位置，需授权另立步。
- **D3**：`RELEASE-CHECKLIST.md` 3 项（路径硬编码 / publish 打包 / `FindTemplate`）**只列不改**，
  发布前单独一步。
- **D5**：收窄 `Executor` 注入（`IEditSession` / `IEditSessionWriter` / `ICommandParser`）不做；
  9C.8-A 已授权冻结。

### 7.3 新增 Nit 登记（只登记不改）

1. `AppContext.SessionStore` 属性名与类型名混淆（类型 = `ISessionPersistence`）→ 后续改名。
2. `MainWindow.ProjectList.cs` / `MainWindow.Projects.cs` 仍以注释提及 `SessionStore` /
   `ProjectService` → 后续清理。
3. `UI → ZIV.Core / ZIV.Imaging` 疑为死引用（7-A 未删，涉及 FROZEN 1.1 项目表）→ 待授权评估。
4. `SessionStore.ExportToAsync` 兼容重载属过渡态（保留时长待定）。

### 7.4 验收（无 GPU，Z29 / Z30）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- 非 GPU 全量：426（7-A / 7-C）→ **432**（7-D1，+6 `ChatFlowRulesTests`）；含 GPU 全量既有基线 448。
- `UI.csproj` 仅引用 `Contracts` / `ZIV.Core` / `ZIV.Imaging`；无循环依赖。
- **未跑 GPU 端到端**；7-E 用例留待 GPU 环境复验。

### 7.5 遮罩不生效 遗留登记（只登记不改）

- 现象：发送时遮罩不生效（实现在、行为缺）。
- 关联 Step：9C.7 / 9C.7-B；对应验收项 **9C.7.16 / 9C.7B.16 当前状态 = 未通过（留后）**。
- 根因：待查（第 7 步不查）。
- 处置：用户裁决留后，另立步；**不改 9C.7 既有行**。

---

## Step 8-1：LoRA 数据驱动链路（日期：2026-09-25）

> **契约追加 + 数据化（追加式，行为新增）**
>
> 目标：加一个 LoRA 模板 = 改数据（`Template/commands.json` + `Template/loras.json`），0 代码。
> 裁决：优先级 8-1 LoRA → 8-2 模型 → 8-3 分辨率 → 8-4 物理划分（可选）；Q1 不拆程序集 /
> Q9 端口放 Contracts / Q10 不允许 UI DTO / Q11 保留 Contracts 名。**Python 真加载待 GPU 复验**（Z29/Z30）。

### 8-1.1 契约追加（复用 LoraOptions）

| 类型 | 追加成员 | 说明 |
|---|---|---|
| `Contracts/Planning/CommandDefinition` | `LoraOptions? Lora`（JSON `lora`） | 命令声明的 LoRA；`Path` 可为 `loras.json` 的 id 或字面路径 |
| `Contracts/Planning/EditStep` | `LoraOptions? Lora` | 由命令填入，随计划流转 |
| `Contracts/Tools/ToolInput` | `LoraOptions? Lora` | Executor 注入，工具透传 |
| `Contracts/Inference/LoraOptions` | `[JsonPropertyName]` path/strength_model/strength_clip | 复用既有类型；仅补序列化名（跨 IPC 与 commands.json 一致） |

### 8-1.2 透传链（C#）

- `commands.json`：命令条目可选 `"lora": { "path": "<id>", "strength_model": 1.0, "strength_clip": 1.0 }`。
- `CommandParser`：解析命令的 LoRA → `EditStep.Lora`（`NormalizeLora`：源生成忽略 `double` 默认值，
  缺 strength → 1.0；空 path → 丢弃）。
- `Executor`：`step.Lora` → `ToolInput.Lora`。
- `QwenImage21EditTool`：`input.Lora` → `EditRequest.Lora`（字段既有）。
- `IpcSubmitMapper` / `IpcDtos` / `IpcJsonContext`：**已通，未改**。

### 8-1.3 数据文件

- 新增 `Template/loras.json`（`{ version, loras: [ {id, path, default_strength_model, default_strength_clip, description} ] }`）；
  `ZivAiEditor.App` 拷贝到输出 `Template/`。

### 8-1.4 Python（代码交付 · 待 GPU 复验）

- `config.py`：`LORA_REGISTRY_PATH`（默认仓库根 `Template/loras.json`，`ZIV_AI_LORA_REGISTRY` 可覆盖）。
- `loras.py`（新）：读注册表 + id / 字面路径解析（纯 CPU，可单测）。
- `pipeline_hooks.py`：`make_lora_hook` 真加载（懒 import `comfy.sd.load_lora_for_models`；失败记 warning 不抛）。
- `handlers.py`：`_configure_pre_sampling_hooks` 经 `loras.resolve_path` 解析后注册 hook。
- `test_loras.py`（新）：6 例纯 CPU 单测。

### 8-1.5 验收（无 GPU，Z29 / Z30）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- 非 GPU 全量 **437 通过 / 0 失败**（432 + 5：`CommandParserLoraTests` 3 + `ExecutorTests` 1 +
  `QwenImage21EditToolTests` 1）。
- Python：`py_compile` 通过；`test_loras` **6/6** 通过。
- **端到端数据驱动**：改 temp `commands.json` 加 `lora` 条目 → 计划 `EditStep.Lora` 出现
  （`CommandParserLoraTests`），不改代码。
- **Python 真加载未 GPU 验证**，登记待 GPU 复验。

### 8-1.6 本步不做

- 不动 model_id（8-2）/ 分辨率档位（8-3）/ 物理划分（8-4）；不做工具 op 数据化（§11）；
  `QwenImage21OutpaintTool` 暂不透传 LoRA（仅 `QwenImage21EditTool` 透传）。

---

## Step 8-2：模型身份契约化（日期：2026-09-25）

> **契约追加 + 数据化 + 授权修订（1 处既有成员类型）**
>
> 目标：加一个新模型 = 写一份 `Template/models.json` 条目 + 契约一处 `model_id`，代码不动。
> 裁决：优先级 8-1 LoRA → 8-2 模型 → 8-3 分辨率 → 8-4 物理划分（可选）；Q1 不拆程序集 /
> Q9 端口放 Contracts / Q10 不允许 UI DTO / Q11 保留 Contracts 名。**Python 真加载待 GPU 复验**（Z29/Z30）。

### 8-2.0 Step 4 修订说明（授权 · 语义反转修复）

- **授权**：本轮裁决允许修改既有 `LoraOptions`：`StrengthModel` / `StrengthClip` 由 `double`
  改为 `double?`（默认 `null`）。
- **原因**：源生成器在缺键时对非可空 `double` 填 `0.0`；8-1 的 `NormalizeLora` 以
  `== 0d ? 1.0` 修正，却把「显式 0」也改成 1.0（语义反转）。
- **新语义**：`null` = 未设置（回退 1.0 / 注册表默认）；`0` = 全抑制该侧。
- **影响面**：`LoraOptions`（kernel）、`CommandParser.NormalizeLora`、Python `loras.resolve_strength` /
  `handlers` / `pipeline_hooks`、相关测试。原 Step 4 行不改。

### 8-2.1 契约追加（ModelId）

| 类型 | 追加成员 | 说明 |
|---|---|---|
| `Contracts/Inference/EditRequest` | `string? ModelId` | null / 未知 = 默认模型 |
| `Contracts/Planning/PlanRequest` | `string? ModelId` | planner 输入 |
| `Contracts/Planning/EditPlan` | `string? ModelId` | plan 级模型 id |
| `Contracts/Tools/ToolInput` | `string? ModelId` | 工具透传 |

### 8-2.2 透传链（C#）

- `FallbackPlanner` / `LlmPlanner`：`request.ModelId` → `EditPlan.ModelId`。
- `Executor`：`plan.ModelId` → `ToolInput.ModelId`；`WithAdditionalImages` 保留 `ModelId`。
- `ChatFlowRules.WithAdditionalImages` 保留 `ModelId`。
- `QwenImage21EditTool` / `QwenImage21OutpaintTool`：`input.ModelId` → `EditRequest.ModelId`。
- IPC：`SubmitPayload.ModelId` + `IpcSubmitMapper`；`SubmitInpaintAsync`（无 ModelId）置 null。

### 8-2.3 数据驱动注册表（Backend 机制 / models.json 数据）

- 新增 `Template/models.json`（`models[]`：id / display_name / 路径 / 分辨率元数据 / tier_sides /
  presets / sampler / default）。
- 新增 Backend 局部 `ModelProfileFileDto` + `ModelProfileJsonContext`（`[JsonPropertyName]` 不进
  kernel）；`ModelProfile` 零改动。
- `ModelProfileRegistry` 改读 `models.json`（构造可传路径）；文件缺失 / 损坏 → 内置 Qwen-Image-2.1；
  `default:true` 选默认；`AppContext` 传 `Template/models.json`；App 拷贝该文件到输出。

### 8-2.4 Python（代码交付 · 待 GPU 复验）

- `config.MODELS_REGISTRY_PATH`（env `ZIV_AI_MODELS_REGISTRY` 可覆盖）。
- `models.py`（新）：读注册表 + `resolve` / `resolve_paths` / `resolve_sampler`；优先级
  **env > models.json > config 默认**。
- `engine.ensure_loaded(model_id, notify)`：按 id 解析路径；切换模型先卸载（单模型进程）。
- `pipeline`：`run` 解析 sampler，`_run_once` / `sample` 使用注册表 sampler（env 覆盖仍最高）。
- `handlers`：`payload.get("model_id")` 传入 `ensure_loaded`。

### 8-2.5 验收（无 GPU，Z29 / Z30）

- 构建 0/0；非 GPU 全量 **444 通过**（既有 `!~Ipc` 过滤不计 `IpcSubmitMapperTests`；C# 新增共 7 例）。
- Python `py_compile` 0；`test_loras` 10/10、`test_models` 5/5。
- 数据驱动：加 `models.json` 一条 → `ModelProfileRegistry` 生效（`ModelProfileRegistryDataTests`），不改代码。
- **Python 真加载未 GPU 验证**，登记待 GPU 复验。

### 8-2.6 本步不做

- 未动分辨率档位数据化（8-3）；未动物理划分（8-4）；未做 UI 模型选择器；未加 `commands.json` 的
  model 字段；未碰遮罩 / outpaint / 7-H。

---

## 妥协/挂账清单（只增 · 自 Step 8-2 起生效）

> 记录**明知不理想但当前接受**的妥协；每条给出回归触发条件与时机。

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-001 | `LoraOptions` 显式 0 被误改 1.0 | Step 4 冻结 + 源生成器缺键填 0.0 | 本次 8-2 修复（可空化） | 8-2（本轮关闭） |
| Z-002 | Python LoRA 真加载未 GPU 复验 | Z29/Z30 不跑 GPU | 用户确认 GPU 空闲 + 明确同意 | 后续 GPU 步 |
| Z-003 | `ModelId` 生产路径恒 null（无 UI） | 本步不做 UI 模型选择器 | 用户需要切换模型 | 后续 UI 步 |
| Z-004 | Python env 覆盖保留为过渡机制 | 测试基础设施依赖 | 测试基础设施改造完成 | 未定 |

---

## Step 8-3：分辨率档位数据化（日期：2026-09-25）

> **契约追加（kernel 值对象自然扩展）+ 数据化（标签 / 档位集合）**
>
> 目标：档位**标签**与**集合**来自数据（`Template/models.json`），代码不动；数值 8-2 起已在
> `tier_sides`。范围仅此两件，不引入任意数量档位（Z-005）。无 GPU（Z29/Z30）。

### 8-3.1 契约追加（授权 · 追加式）

| 类型 | 追加成员 | 说明 |
|---|---|---|
| `Contracts/Models/ModelProfile` | `IReadOnlyDictionary<ResolutionTier, string> TierLabels`（init，默认空） | 档位显示标签；空 → 回退 enum 名（生产）/ 中文（设计期） |

> 授权依据：UI 只能见 `Contracts`，`ModelProfileFileDto` 为 Backend internal（Q10 不允许 UI DTO），
> 故标签**必须**经 kernel 值对象承载。原行不改。

### 8-3.2 数据（`Template/models.json`）
- 每模型追加独立键 `tier_labels`（与 `tier_sides` 并列）：`{ "fast": "快速", "balanced": "均衡", "high_quality": "高质" }`。
- 数值仍在 `tier_sides`；`ModelProfileFileDto` 新增 `tier_labels` → `ModelProfileRegistry.ToProfile` 映射；
  `BuiltInDefault()` 带默认标签。

### 8-3.3 UI（机制）
- 新增纯 helper `ZivAiEditor.UI/Editing/ResolutionTierOptions`（无 Avalonia，可单测）：
  - `Options(profile)`：`TierSides.Keys` 按 `(int)tier` 升序 + `Custom` 末尾（缺 side 不显示）。
  - `DefaultTier(profile)`：`Balanced` 在则用之，否则首个非 Custom，否则 Custom。
  - `Label(profile, tier)`：有 `TierLabels` → 用之；**生产**缺 label → enum 名（**不静默回退中文**）；
    **设计期**（profile == null）→ 内置中文表；`Custom` 恒「自定义」。
  - `Display(profile, tier)`：Label + side（Custom 无 side）。
- `ResolutionPicker`：`BuildMenu` 用 `Options`、`Header` 用 `Display`、`Attach` 时 `Tier = DefaultTier(profile)`；
  删除硬编码 `Tiers` 数组与 `LabelFor` switch。

### 8-3.4 验收（无 GPU，Z29 / Z30）
- 构建 0/0；非 GPU 全量 **450 通过**（8-2 基线 444 + `ResolutionTierOptionsTests` 6）。
- 数据驱动：改 `models.json` 的 `tier_labels` / `tier_sides` → 不改代码，标签 / 数值生效
  （`ResolutionTierOptionsTests` + `ModelProfileRegistryDataTests`）。

### 8-3.5 本步不做
- 不引入任意数量档位（新增档位仍改枚举 + `tier_sides`）→ **Z-005**；
- 未动 8-4 物理划分 / UI 模型选择器 / 遮罩 / outpaint / 7-H。

### 妥协/挂账清单 · 追加（8-3）

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-005 | 新增分辨率档位仍改代码（`ResolutionTier` 枚举 + `tier_sides`） | 保持固定 3 档 + Custom；动态档位集合超 8-3 范围 | 需要任意数量 / 动态档位 | 未定 |

---

## Step 8-4：域物理划分（命名空间拆分）（日期：2026-09-25）

> **纯搬迁（位置 / 命名空间变更，不改任何类型 / 签名 / 成员 / 行为；Q1 不拆程序集）**
>
> 授权依据：本轮裁决（D-2/key）——位置变更需登记，本段为「8-4 位置变更说明」。原冻结行不改。

### 8-4.1 Contracts 命名空间拆分（Planning → 域）

| 旧 | 新 | 文件 |
|---|---|---|
| `ZivAiEditor.Contracts.Planning` | `ZivAiEditor.Contracts.Session` | IEditSession, IEditSessionWriter, IEditNode, CropSpec, RerunSpec, ISessionPersistence, SessionLoadResult |
| 同上 | `ZivAiEditor.Contracts.Project` | IProjectService, ProjectSummary |
| 同上 | `ZivAiEditor.Contracts.Execution` | ICommandParser, CommandDefinition, ParseResult, IPlanner, EditPlan, EditStep, PlanRequest |

- 文件位置：`src/ZivAiEditor.Contracts/{Session,Project,Execution}/`；`Planning/` **已移除**。
- `Contracts.Execution` 既有 IExecutor / TaskState / StepState / TaskProgress 不变（同命名空间合并，无同名冲突）。

### 8-4.2 Agent 命名空间拆分（平铺 3 域）

| 旧 | 新 | 文件 / 文件夹 |
|---|---|---|
| `ZivAiEditor.Agent` | `ZivAiEditor.Agent.Session` | `Agent/Session/`：EditSession*, SessionStore*, SessionLoader, SessionFileRerun, SessionSignature |
| 同上 | `ZivAiEditor.Agent.Project` | `Agent/Project/`：ProjectService, ProjectNaming |
| 同上 | `ZivAiEditor.Agent.Execution` | `Agent/Execution/`：Executor, ExecutionQueue；`Execution/Command/`：CommandParser；`Execution/Planner/`：LlmPlanner, FallbackPlanner, ResilientPlanner, PromptExpander（子文件夹命名空间扁平为 `.Execution`） |

- 已知跨命名空间引用：`ProjectService → SessionStore`（`using ZivAiEditor.Agent.Session`）；`CommandParser → FallbackPlanner`（同 `.Execution`）。
- **位置冻结行不改**：`FROZEN.md` 的 Z18 表行、Step 6（`ExecutionQueue`/`Executor` 路径）、Step 6.5、9C.8-A 原行保留；
  其路径字面（`ZivAiEditor.Agent/Executor.cs` 等）以本段为准（现位于 `Agent/Execution/`）。
- **契约/程序集不变**：仍为 `ZivAiEditor.Contracts` / `ZivAiEditor.Agent` 程序集（Q1 / Q11）。

### 8-4.3 验收（无 GPU，Z29 / Z30）

- 构建 0/0；非 GPU 全量 **450 通过 / 0 失败**；`grep` 旧命名空间 0 命中。
- 纯搬迁：仅 `namespace` / `using` 行变化（`git mv` 100% rename）。

### 8-4.4 本步不做

- 不拆程序集（Q1）；不改任何签名 / 成员 / 行为；不动 Backend / Tools / Imaging；不动 Executor 注入（D5）；
  不碰遮罩 / outpaint / 7-H / python / GPU。`_test_step2/**`（sln 外临时工程）未同步（不在范围）。

### 妥协/挂账清单 · 追加（8-4）

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-006 | `ProjectService` 仍注入具体 `SessionStore`（Project→Session 具体依赖） | 7-C 未实现 `IProjectMetadataStore`（元数据原语端口）；8-4 为纯搬迁不动注入 | 需要彻底解耦 project/session 域 | 后续域解耦步 |

---

## Z-006 收口：IProjectMetadataStore 端口（日期：2026-09-25）

> **契约追加 + 纯端口化（零行为变化）**。关闭 8-4 妥协清单 Z-006（Project→Session 具体依赖残留）。

### Z006.1 契约追加（`Contracts/Project/IProjectMetadataStore`）

| 成员（逐字） | 说明 |
|---|---|
| `string RootDirectory { get; }` | 项目根目录 |
| `Task<ProjectSummary?> ReadMetadataAsync(string directory, CancellationToken ct = default)` | 读项目列表元数据 |
| `Task WriteMetadataNameAsync(string directory, string name, CancellationToken ct = default)` | 改写项目 `name`（仅元数据） |

- 端口由 **project 域声明**（它需要），由 session 域 `SessionStore` 实现。
- 刻意**不含**会话内容读写（load / save / export / 清理）→ 那些仍在 `ISessionPersistence`。

### Z006.2 实现与注入

- `SessionStore : ISessionPersistence, IProjectMetadataStore`（签名零变化）。
- `ProjectService` 注入由具体 `SessionStore` 改为 `IProjectMetadataStore`；内部调用点不变。
- `AppContext` 装配点不变（`SessionStore` 隐式转端口）；具体类仅在装配点保留。

### Z006.3 验收（无 GPU，Z29 / Z30）

- 构建 0/0；非 GPU 全量 **451 通过 / 0 失败**（+1 接口一致性用例）。
- `ProjectService.cs` 无具体 `SessionStore` 代码依赖（仅 XML 注释提及）。

### Z006.4 妥协/挂账清单 · 追加（Z-006 关闭）

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-006 | Project→Session 具体依赖残留 | —（已由本段收口） | — | **本轮关闭**（2026-09-25） |

---

## 审查 B 类遗留登记（2026-09-25 · 独立审查）

> 本段为独立代码审查（架构合理性 / 耦合性）的 B 类问题登记（**只增不改**）。经裁决**接受现状**，
> 后续专项处理；A 类问题（R-2 / R-3 / R-4）已于本轮修复。

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-007 | `ZivAiEditor.UI` 名实不符：无视图 / 无 Avalonia，实为视图模型 + 规则库，视图在 `App` | 正名 + 拆分影响面大，非本轮范围 | 新增视图 / 重命名程序集 | 后续专项 |
| Z-008 | `MainWindow` 上帝类（12 个 partial，约 1970 行）+ 字符串 `FindControl` 与 XAML 隐式耦合 | UI 层重构需专项设计与回归验证 | 修改 MainWindow / 拆 UserControl | 后续专项 |

---

## Step 9C.7-C（日期：2026-09-25 · 遮罩修复：送管线对齐 / 持久化 / 气泡可视化 / 羽化上限）

> **修订说明（Step 9C.7-C）**。本段为遮罩 9C.7 / 9C.7-B 验收遗留（9C.7.16 / 9C.7B.16 未通过）
> 的修复记录，**只增不改**。**无契约签名变化**：改动均在 UI / App 层；`ChatMessage` 属 UI 非契约。
> **不改动 Step 0–9C.7-B 已冻结行**。

### 9C.7C.1 裁决（冻结 · 记录）

| 裁决 | 取值 | 说明 |
|---|---|---|
| E1 | ① | 气泡显示 = 原图 + 遮罩可视化叠加（半透明红，含羽化灰度），UI 层合成，不落盘 |
| E2 | A + 自动切 | 当前节点为唯一真源；进入遮罩模式时 `NavigateTo(预览节点)` + hint |
| E4 | 是 | 羽化参与气泡叠加（与预览 / 导出一致） |
| E8 | 15 | 羽化滑块 / `MaskState.FeatherPx` 上限 25 → 15 |
| E3/E5/E6/E7 | 不变 | 否 / 允许 / 否 / 保持现状 + Z-009 |

### 9C.7C.2 语义 / 行为（冻结 · 非契约）

- **送管线对齐**：`SessionViewModel.AlignForMask(previewNodeId)` —— 预览节点 ≠ 当前节点时
  `NavigateTo` 并把当前节点切到预览节点，随后追加 hint「已切换到节点 X 以绘制遮罩」；已为当前 /
  未知 / 空 → no-op。App 在 `ImagePreview.MaskToolEntered`（遮罩画笔 / 橡皮激活）时调用。
- **持久化 App 层**：遮罩导出任务由预览窗级提升为**窗口级** `MainWindow._pendingMaskExport`
  （`ImagePreview.MaskExportScheduled` 事件上报，且在 `MaskCompleted` **之前**发出）。`FlushPendingMaskAsync`
  / 发送 / `OnClosing` / 预览 `Closed` 均 await 该任务，预览窗关闭后仍可 flush。
- **气泡可视化**：`ChatMessage` 追加 `MaskPath` / `MaskFeatherPx`（UI 非契约）；`RebuildContext` 从
  `node.Mask` 填充；App 解码 PNG + 羽化 + `MaskOverlayBitmap.Build`（BGRA 半透明红）叠加到气泡主图；
  异步加载以 `_chatGeneration` 防串代。
- **羽化上限**：`MaskState.MaxFeatherPx` 25 → 15；`MaskToolbar` 滑块 Maximum 25 → 15。
  `MaskFeather.MaxRadiusPx` 保持 25（纯函数内部钳制，产品路径受 `MaskState` 限制）。

### 9C.7C.3 前置核查（冻结 · 记录）

- **mask_binary 现状 = `False`（已修）**：`handlers._dispatch_op` 两处 `pipeline.run` 均传
  `mask_binary=False`；`pipeline.run` 签名默认 `True` 但被 handlers 覆盖；`_load_mask_tensor` 默认
  `True`、以 `binary=mask_binary` 调用。故 `mask_feather_result.md` 第 110 行「产品现状
  mask_binary=True」已过时；S1 未动 `mask_binary`。
- **羽化算法核查**：`MaskFeather.Apply` = 3 趟可分离 box blur（边界 clamp），**非整片衰减**：宽于
  ~3·box 过渡带的实心涂抹区核心保持 255；只有窄于过渡带的细线才整体被压低。故 B（σ=10）核区偏弱
  属 ComfyUI `noise_mask` 混合语义固有，非 C# 羽化 bug；E8 按 15 收口。

### 9C.7C.4 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，无 GPU，排除 `Ipc*` / `PlannerIntegration`）→ **454 通过 / 0 失败**（451 → 454）。
- **未跑 GPU 端到端**（Z29 / Z30）；9C.7.16 / 9C.7B.16 由用户真机确认。

### 9C.7C.5 妥协/挂账清单 · 追加（9C.7-C）

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-009 | 遮罩气泡叠加为 UI 层实时合成（每次渲染重新解码 + 羽化 + 构位图，不缓存）；预览窗 ↔ 当前节点的对齐仅在进入遮罩模式时单向发生（历史导航不反向同步预览） | 落盘 / 缓存会增加中间产物与失效管理；双向同步超出本轮范围 | 气泡数量大导致渲染开销 / 用户反馈预览与当前节点漂移 | 后续专项 |

---

## Step 9C.7-C 修补（R2–R4）（日期：2026-09-25）

> 本段为 9C.7-C 真机反馈后的收尾修补记录（**只增不改**）。**无契约签名变化**；改动均在 UI / App / Agent 实现层。**不改动 Step 0–9C.7-C 已冻结行**。

### 9C.7D.1 行为（冻结 · 非契约）

- **R2 光标**：遮罩画笔 / 橡皮隐藏系统光标（`StandardCursorType.None`），只保留自绘圆圈；裁切保留 `Cross`。
- **R3 叠加保真**：`MaskOverlayBitmap` 改为真正的预乘半透明红（alpha 上限 50%）；删除死代码 `ImagePreview.FlushMaskAsync`；`MaskFeather.MaxRadiusPx`（25，算法）与 `MaskState.MaxFeatherPx`（15，产品）加注说明，数值不变。
- **R3.1 关窗刷新**：气泡叠加改在**关闭预览窗**时刷新一次（不再松手即刷新）；`SessionViewModel.SetNodeMask` 不再重建聊天流。
- **R3.2 抑制深度**：`_suppressMaskExport` 由 `bool` 改 `int` 深度计数——原 bool 被嵌套的 `RefreshMaskCanvas.finally` 提前解除，导致关窗时泄漏一条 `spec=null` 提交并清空节点遮罩。
- **R3.3 显示解耦**：叠加显示不再依赖工具激活（节点有遮罩即显示）；`RefreshMaskCanvas` 在工具未激活时也载入存量遮罩。
- **R4 持久化硬化**：`CopyIfNeeded` / `CopyNodeImage` 返回 `bool`；源文件缺失 / 拷贝失败时 **crop / mask 不写 DTO 字段**（不留悬空引用）并记 `Debug.WriteLine`（源 + 目标 + 原因）。`SessionLoader` 缺文件丢弃 + 警告逻辑不变。诊断日志 `MaskDiagnostics.Log` 加 env 门控 `ZIV_AI_MASK_DIAG=1`（默认静默）。

### 9C.7D.2 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，无 GPU，排除 `Ipc*` / `PlannerIntegration`）→ **460 通过 / 0 失败**（458 + 2：`Save_Drops_Mask_When_Source_Missing` / `Save_Drops_Crop_When_Source_Missing`）。
- **未跑 GPU 端到端**（Z29 / Z30）；R4 全链回填由用户真机确认。

### 9C.7D.3 妥协/挂账清单 · 追加（R4）

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-015 | 打包图 / 参考图 / 用图的拷贝仍是「静默失败 + 无条件记名」的同一模式（可能产生悬空引用） | R4 按裁决仅修 crop / mask（同一根因的两个用户可见面） | 打包 / 参考 / 用图源缺失导致加载丢图 | 后续专项 |

---

## Step 9C.7-C 修补（R5 · 边界 M-4）（日期：2026-09-25）

> 本段为 M-4 修复记录（**只增不改**）。**无契约签名变化**；改动仅在 App 层。

### 9C.7E.1 行为（冻结 · 非契约）

- **M-4 修复**：`MainWindow.OnHistorySelectionChanged` 的 dispatcher 回调中，`_vm.NavigateTo(nodeId)` **之前**：若预览窗遮罩工具激活（`MaskBrush` / `Eraser`）→ `SetTool(ToolMode.None)`。切历史 = 上下文切换，自动退出遮罩，避免「画在预览旧节点、发送读当前节点」的错配。
- **不动** `SessionViewModel.NavigateTo`（保 `AlignForMask` 进入遮罩时的对齐语义）；**不动** `FlowRunner.Rerun.cs`（重跑路径另议）。
- 不加 chat hint；预览窗不关闭 / 不切换（Z-017）。

### 9C.7E.2 测试结果（冻结）

- `dotnet build src\ZIV.AI.sln -c Release` → **0 错误 0 警告**。
- `dotnet test`（Z29，无 GPU，排除 `Ipc*` / `PlannerIntegration`）→ **460 通过 / 0 失败**（App 层改动，测试数不变）。
- **未跑 GPU 端到端**（Z29 / Z30）；M-4 真机由用户确认。

### 9C.7E.3 妥协/挂账清单 · 追加（R5）

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-011 | 多图 root 次要缩略图遮罩静默丢弃（M-3，保持） | 涉及 `FindNodeByImagePath` 扩展，E3 明确不做 | 用户在多图 root 次要缩略图上绘制遮罩 | 未定 |
| Z-016 | `ReplaceNodeImage` 后 mask 尺寸潜在错配（G3，观察项） | 当前仅重跑路径会同时清 mask，未暴露 | 图像被替换而 mask 尺寸变 | 观察 |
| Z-017 | 导航时遮罩工具自动退出，但预览窗仍显示旧节点（不同步）（R5 观察项） | 最小改动；同步预览窗超出 M-4 范围 | 用户导航后误以为预览窗 = 当前节点 | 观察 |

---

## Step 9C.7-C 修补（R6 · GPU 端到端）（日期：2026-09-25）

> 本段为 R6 GPU 端到端验收记录（**只增不改**）。**无产品代码改动**；**Z30 例外**（用户确认 GPU 空闲 + 同意）。
> 覆盖 **9C.7.16**（发送时遮罩生效）与 **9C.7B.16**（软边融合）。**不改动 Step 0–9C.7-C 已冻结行**。

### 9C.7F.1 环境与步骤（冻结 · 记录）

- 测试图 `_test_step2/user_input.png`（2560×1599）；提示词「把遮罩区域替换为蓝天白云」；后端 side=1536（输出 1536×960）。
- 组 A：羽化 0（硬边遮罩）；组 B：同一节点 / 同一区域，羽化 10（灰度软边）。
- 对比：`_test_step2/r6_compare.py`（CPU-only，方法同 `mask_feather_analyze.py`：遮罩内 / 外均值绝对差 + 接缝不连续度）。

### 9C.7F.2 结果（冻结）

| 指标 | 组 A（feather=0） | 组 B（feather=10） | 判据 |
|---|---|---|---|
| 遮罩外 vs 原图（MAD 0-255） | **0.71** | **0.70** | ≈VAE 往返 → 局部编辑 ✅ |
| 遮罩内 vs 原图 | 33.09 | 33.48 | 明显改动 ✅ |
| 接缝不连续度 | 0.011 | **0.008** | B ≤ A → 软边更平滑 ✅ |
| B vs A（遮罩内） | — | 21.92 | 羽化改变过渡区（预期） |

- **9C.7.16 = 通过**：遮罩外像素基本不变 + 遮罩内被编辑 → 后端按遮罩局部编辑。
- **9C.7B.16 = 通过**：接缝不连续度 B（0.008）≤ A（0.011）。
- 日志：`_cache/mask.log` 记录 `[mask] export ok ... feather=0/10`、`[rebuild] current=801533 path=801533:M`、`[overlay] applied ... feather=0/10`；`mask_path` 到达后端并经 `_load_mask_tensor(binary=False)` 处理（输出体现遮罩语义）。
- 无 OOM / 无崩；GPU 回基线：任务前 **1132 MiB** → 任务后 **1116 MiB**；无残留 `python` / App 进程。

### 9C.7F.3 M-7 关闭（冻结）

- 审计遗留 **M-7**（9C.7.16 / 9C.7B.16 未验）→ **R6 通过，关闭**。
  （注：FROZEN 既有 `Z-007` 为“UI 名实不符”，与本项无关；按“只增不改”**不动其行**。）

---

## 第 8 步收口（日期：2026-09-25）

> 本段为**第 8 步（域物理划分 + 沿途解耦）整体收口**（**只增不改**）。汇总 8-0 ~ 8-4 各子步结论、
> Z-006 收口、审查 A 类 R-1 ~ R-4、遮罩修复 R1–R6，以及妥协/挂账清单 Z-001 ~ Z-018 的统一状态。
> **不改动 Step 0–9C.7-C 已冻结行**；各子步的原始冻结行以对应段落为准，本段只做收口索引与状态判定。
> 全步**无契约破坏**（仅授权追加）；**无 GPU**，除 R6 为 Z30 例外的必需 GPU 验证。

### 8-0 解耦审计（4 场景）· 结论

- **性质**：第 8 步启动前的只读解耦审计（4 场景），非代码步。
- **结论 / 方法修正**：审计把**物理划分从「主菜」降为「配菜」**——真正的价值在沿途**数据化 / 端口化**
  （8-1 LoRA、8-2 模型、8-3 分辨率、Z-006 端口），物理划分（8-4）作为可选收尾；该修正贯穿 8-1 ~ 8-4 排序。
- **裁决（Q1 / Q9 / Q10 / Q11）**：Q1 **不拆程序集**；Q9 端口接口放 `Contracts`；Q10 **不允许 UI 直接持有 Backend DTO**；
  Q11 **保留 `Contracts` 程序集名**。同轮定优先级：8-1 LoRA → 8-2 模型 → 8-3 分辨率 → 8-4 物理划分（可选）。

### 8-1 LoRA 数据驱动链路 · 结论

- **目标**：加一个 LoRA 模板 = 改数据（`Template/commands.json` + `Template/loras.json`），0 代码。
- **结论**：✅ 达成。契约追加 `Lora`（`CommandDefinition` / `EditStep` / `ToolInput`）+ `LoraOptions` 序列化名；
  透传链 `CommandParser → Executor → QwenImage21EditTool → EditRequest.Lora` 打通；Python `loras.py`（CPU）+ 真加载 hook。
  非 GPU **432 → 437**；Python `test_loras` 6/6。**Z-001 关闭**（显式 0 语义反转，8-2 彻底修复）。
  遗留：Python 真加载待 GPU（**Z-002**，R6 关闭）。

### 8-2 模型身份契约化（ModelId + models.json）· 结论

- **目标**：加一个新模型 = 写 `Template/models.json` 条目 + 契约一处 `model_id`，代码不动。
- **结论**：✅ 达成。契约追加 `ModelId`（`EditRequest` / `PlanRequest` / `EditPlan` / `ToolInput`）+ IPC `model_id`；
  `ModelProfileRegistry` 改读 `models.json`（Backend 局部 DTO，kernel 零改动，缺失回退内置 Qwen）；Python `models.py`（env > models.json > 默认）。
  **授权修订**：`LoraOptions.StrengthModel/StrengthClip` `double → double?`（关闭 Z-001）。非 GPU **437 → 444**（+7，含过滤外 1）；
  Python `test_loras` 10/10、`test_models` 5/5。新增挂账 **Z-003**（无 UI 选择器）/ **Z-004**（Python env 过渡）。

### 8-3 分辨率档位数据化 · 结论

- **目标**：档位**标签**与**集合**来自数据（`models.json`），代码不动。
- **结论**：✅ 达成。数据追加 `tier_labels`；**授权追加** kernel `ModelProfile.TierLabels`（Q10：标签必须经 kernel 承载）；
  UI 新增纯 helper `ResolutionTierOptions`，`ResolutionPicker` 删除硬编码。生产缺 label → enum 名（配置错误可见）。
  非 GPU **444 → 450**（+6）。新增挂账 **Z-005**（新增档位仍改代码，不引入任意数量档位）。

### 8-4 域物理划分（命名空间拆分）· 结论

- **目标**：纯搬迁（位置 / 命名空间变更，不改类型 / 签名 / 成员 / 行为；Q1 不拆程序集）。
- **结论**：✅ 达成。`Contracts.Planning` → `Session` / `Project` / `Execution`（`Planning/` 移除）；
  `Agent` → `Session` / `Project` / `Execution`（`Command` / `Planner` 子文件夹，命名空间扁平）；`git mv` 100% rename，
  仅 namespace / using 行变化；旧命名空间 `grep` 全 0；程序集名不变。非 GPU **450 通过 / 0 失败**。
  新增挂账 **Z-006**（Project→Session 具体依赖，本轮后收口关闭）。

### Z-006 收口（IProjectMetadataStore 端口）· 结论

- **结论**：✅ 关闭。新增 `Contracts/Project/IProjectMetadataStore`（3 元数据原语，**不含**会话读写）；
  `SessionStore` 实现；`ProjectService` 由具体 `SessionStore` 改注入端口。非 GPU **450 → 451**（+1 接口一致性）。

### 审查 A 类 R-1 ~ R-4 · 结论

- **R-1**：Z-006 收口文档补登 ✅。
- **R-2**：`ZivAiEditor.UI.csproj` 删除死引用，**只保留 `Contracts`**（比 ARCHITECTURE §4 旧措辞更严）✅。
- **R-3**：`ARCHITECTURE.md` / `INTERFACES.md` / `FROZEN.md` 文档同步（8 项目 / UI 仅 Contracts / `IpcInferenceClient` / 编排分层）✅。
- **R-4**：`AppContext.Create` 拆为 `BuildBackend` / `BuildTools` / `BuildAgent` / `BuildLlm` / `BuildPersistence` / `BuildImaging`，
  构造顺序与 `AppContext` 形状不变 ✅。非 GPU **451 通过 / 0 失败**（未增测试）。
- 新增 B 类挂账 **Z-007**（UI 名实不符）/ **Z-008**（MainWindow 上帝类）。

### 遮罩修复 R1–R6（9C.7-C 收口）· 结论

- **R1–R5（无 GPU）**：送管线对齐 / 持久化 flush / 气泡叠加 / 羽化上限 15 / 光标 / 半透明 / 关窗刷新 / 抑制深度
  （`bool → int` 根因修复）/ 持久化硬化（源缺失不写悬空引用）/ 边界 M-4（导航退出遮罩工具）；非 GPU **451 → 460**。
- **R6（Z30 例外，GPU 端到端）**：**9C.7.16 ✅**（遮罩外 MAD 0.71 / 遮罩内 33.09）、**9C.7B.16 ✅**（接缝 B 0.008 ≤ A 0.011）；
  GPU 回基线（1132 → 1116 MiB）、无残留进程、无产品代码改动；**M-7 关闭**。
- 新增挂账：**Z-009**（气泡叠加无缓存 / 对齐单向）、**Z-011**（多图 root 次缩略图，M-3）、**Z-015**（打包/参考/用图同模式）、
  **Z-016**（`ReplaceNodeImage` 后尺寸错配，观察）、**Z-017**（导航后预览窗不同步，观察）。

### 遗留清单（Z-001 ~ Z-018 汇总 · 统一状态）

> 汇总自各段「妥协/挂账清单」，**不改动既有行**，仅在此统一标注状态。

| 编号 | 内容 | 状态 |
|---|---|---|
| Z-001 | `LoraOptions` 显式 0 被误改 | 已关闭（8-2） |
| Z-002 | Python LoRA 真加载未 GPU 验 | 已关闭（R6） |
| Z-003 | `ModelId` 无 UI 选择器 | 挂账 |
| Z-004 | Python env 覆盖 | 挂账 |
| Z-005 | 新增档位仍改代码 | 挂账 |
| Z-006 | Project→Session 具体依赖 | 已关闭（本轮） |
| Z-007 | UI 名实不符 | 挂账 |
| Z-008 | MainWindow 上帝类 | 挂账 |
| Z-009 | 遮罩仅随裁切变化失效（气泡叠加无缓存 / 对齐单向） | 挂账 |
| Z-010 | 羽化核心区视觉弱化 | 挂账（观察） |
| Z-011 | 多图 root 次缩略图遮罩静默丢弃（M-3） | 挂账 |
| Z-012 | 画笔大小不持久化 | 挂账 |
| Z-013 | `MaskSpec` 注释不符 | 挂账 |
| Z-014 | 脏标记 mtime | 挂账 |
| Z-015 | 打包 / 参考 / 用图同模式（悬空） | 挂账 |
| Z-016 | `ReplaceNodeImage` 后尺寸错配 | 挂账（观察） |
| Z-017 | 导航后预览不同步 | 挂账（观察） |
| Z-018 | 切项目时预览窗未同步 | 挂账（观察） |

- **关闭 3 项**：Z-001 / Z-002 / Z-006；**其余 15 项挂账**（其中 Z-010 / Z-016 / Z-017 / Z-018 为观察）。

### 第 8 步总收口

- 主线（8-0 ~ 8-4 + Z-006 + R-1 ~ R-4 + 遮罩 R1–R6）**全部完成**；构建 0/0；非 GPU 全量 **460 通过 / 0 失败**
  （第 7 步基线 432 → 460）；GPU 仅在 R6（Z30 例外）执行并回基线。
- 未 push（提交链 `1648723` → `dee09ee` → `e6aedab` → `944bc7f` → `4b1aeae`）。
- **Z29 / Z30 合规**：除 R6 外全程无 GPU；无契约破坏；无新 NuGet；改动文件 < 600（Z8）。

---

## TE-Speed 集成（外置加速模块 · 默认关）（日期：2026-09-25）

> **只增不改**。新增**可选**加速器接入点（默认关，env 开启）；**无契约签名变化**——改动仅在 Python
> 后端的 `config.py` / `pipeline.py` 与 `.gitignore`；**不改 C# / Contracts / IPC**；**不跑 GPU**（A/B 已实测）。

### TE.1 语义（冻结 · 记录）

- **外置模块**：TE-Speed **不 vendor、不进发布包**（第三方闭源 `.pyd`、无 LICENSE）。用户自行放置于
  `<ComfyUI>/custom_nodes/TE-Speed-QwenImage21/`，env 开启；**删目录即拔除，换 `nodes.pyd` 即更新**。
- **默认关**：`ZIV_AI_TE_SPEED` 默认 `0`；关闭时**不加载 pyd**（无副作用）。
- **加载路径**：`config.TE_SPEED_NODE_DIR`（默认 `COMFY_ROOT/custom_nodes/TE-Speed-QwenImage21`，env 可覆盖）。
- **非致命**：节点缺失 / 补丁失败 / 模型不支持 → 记 warning 并**原样返回 model**，绝不失败任务。

### TE.2 接入点（冻结）

- `python/server/config.py`：新增 `TE_SPEED_*`（默认关；参数见 `DOC/OPTIMIZATION.md` §1.7）。
- `python/server/pipeline.py`：新增 `_load_te_speed()` / `apply_te_speed(model)`；`run()` 在
  `pipeline_hooks.apply_pre_sampling_hooks` 之后调用 `model = apply_te_speed(model)`。
- `.gitignore`：排除 `Comfyui/ComfyUI/custom_nodes/TE-Speed-QwenImage21/`（本地内容不入库）。

### TE.3 实测（冻结 · 详见 `DOC/OPTIMIZATION.md` §1.7）

- 1K（1024×640）：端到端 **+12.3%**，MAD 2.15，PSNR 27.64 dB，SSIM 0.9865；
- 2K（2048×1280）：端到端 **+15.6%**，MAD 0.80，PSNR 38.53 dB，SSIM 0.9957；
- 均 **< 30% 门槛** → **默认关**；显存 peak 相同、无 OOM、连续 3 次稳定。

### TE.4 验收（无 GPU / C# 无改动）

- `py_compile`（config / pipeline）通过；C# `dotnet build` **0/0**；
- 默认关：`apply_te_speed` no-op 且 **pyd 未加载**；
- env 开 + 未部署 / 删目录：跳过且**不致命**；
- env 开 + 已部署：节点类加载成功（mock 模型 patch 失败被捕获，不致命）；
- 默认关时编辑路径**无回归**（Python CPU 单测 38/38 通过）。

### TE.5 妥协/挂账清单 · 追加（TE-Speed）

| 编号 | 内容 | 为什么妥协 | 回归触发条件 | 触发时机 |
|---|---|---|---|---|
| Z-019 | TE-Speed 外置加速模块（默认关；可控 / 可删 / 可更新） | 无 LICENSE / 闭源 pyd / Windows x64 绑定 / attention 冲突未验 | 上游加 LICENSE / 加速率提升 / 暴露源码 | 发布前 / 升级 ComfyUI |
