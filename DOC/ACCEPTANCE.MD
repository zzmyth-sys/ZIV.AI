# ZIV.AI 验收流程（ACCEPTANCE）

- 状态：随项目推进追加
- 用途：每个 Step 结束时，由**验收人**（不懂编程的产品负责人）按本表逐条核对
- 规则：本表**只增不改**。新增 Step 段落在文件末尾追加，禁止修改已通过的段落
- 关联：[`SPEC.md`](SPEC.md) · [`ARCHITECTURE.md`](ARCHITECTURE.md) · [`FROZEN.md`](FROZEN.md) ·
  [`DEVLOG.md`](DEVLOG.md) · [`../contracts/openapi.yaml`](../contracts/openapi.yaml)

> ## 修改铁律
>
> 本文件**只增不改**。
>
> - 追加新 Step 段落：允许
> - 修改已通过的段落：**必须**先向用户说明原因并获授权
> - 删除任何段落：禁止
>
> 验收未通过的 Step，不得开始下一个 Step。

---

## 通用验收清单（每个 Step 都要过）

每个 Step 结束时，先过通用项，再过该 Step 的专属项。

| # | 检查项 | 怎么查 | 通过标准 |
|---|---|---|---|
| G1 | 代码能编译 | 让 AI 跑 `dotnet build ZIV.AI.sln` | 无错误（warning 可接受） |
| G2 | 依赖方向没破 | 让 AI 列出各项目的 `ProjectReference` | 只有 `App → UI → Agent/Tools/Backend → Contracts → ZIV.Core/Imaging`，无反向 |
| G3 | 接口与 FROZEN 一致 | 对照 `FROZEN.md` 逐条比 | 无新增、无删改、无签名变化（除已授权修订） |
| G4 | 未授权修改检查 | 问 AI：“本 Step 有没有改过已冻结接口？” | 回答“没有”或“有，已获授权并记录” |
| G5 | 文档已更新 | 检查 `DEVLOG.md` / `FROZEN.md` 是否追加本 Step 记录 | 两份都已追加 |
| G6 | 便携性未破 | 问 AI：“配置 / 任务库 / 输出是否都写在程序目录？注册表只用于 URL 协议等系统集成吗？” | 数据全在程序目录；注册表仅系统集成且可撤销 |
| G7 | AI 推理进程隔离未破 | 问 AI：“C# 有没有直接加载 Python 运行时 / P/Invoke Python C API？是否只经 `IInferenceClient`？” | 无 Python 运行时加载；只经本地 HTTP 或命名管道（Z17） |
| G8 | 独立运行不依赖 ZIV | 关掉 / 卸载 ZIV，单独运行 `ZivAiEditor.App.exe` | 能启动并完成一次编辑；无 `ZIV.App` 引用（Z27 / Z28） |

**G3 是核心。** 你不会读代码，但你会对照。`FROZEN.md` 里写的接口，代码里有没有；
`FROZEN.md` 没写的公开接口，代码里有没有——**一致就是过，不一致就是打回。**

**G2 是命门。** 依赖方向错一次，架构就破；**G7 / G8 是 ZIV.AI 区别于 ZIV 的命门。**

---

## 测试执行原则（Z29 · 保护硬件资产）

> 新增于 2026-09-22（见 `FROZEN.md` Step 5 / `SPEC.md` §6.2）。约束测试执行范围，
> 避免不必要的全量 GPU 测试。G1（代码能编译）不隐含"每次改动都跑全量测试"。

- 每次改动后，只跑**受影响的测试类**；全量测试仅在 **Step 收尾**时执行。
- **核心动机：保护硬件资产** —— 端到端测试跑满 16GB 显存峰值（1024 下
  16004/16376 MiB），GPU 长期高负载缩短寿命；RTX 4080 市价约 1 万元，
  硬件成本远高于测试时间成本。
- **原则：出错了再修** —— 测试是为了发现已发生的问题，不是为了预防所有可能性。
- 测试选择依据：改动文件的引用关系 + 测试类的覆盖范围。
- 全量测试触发条件：Step 收尾、发布前验证、跨模块重构。
- **禁止**无差别的「每次改动都跑全量」。
- 示例：`pipeline.py` 采样改动 → 只跑 `IpcCancelTests`；`config.py` 加配置 → 无需跑测试。

**原因**：端到端 GPU 测试每次加载 16GB 模型、峰值显存 16004/16376 MiB；全量约 4 分钟，
针对性命中约 40 秒；频繁全量会造成显卡长时间高负载，缩短硬件寿命。

---

## Step 0 验收：文档与契约冻结

**目标**：六份文件齐备（五份文档 + OpenAPI 草案），定位、架构、铁律、契约定义清楚，
可作为后续实现的基准；**本步不写代码，不改 ZIV / ImageGlass**。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 |
|---|---|---|
| G1 | 代码能编译 | **N/A**（本步无代码） |
| G2 | 依赖方向没破 | **N/A**（本步无代码）；以文档中的依赖方向为准 |
| G3 | 接口与 FROZEN 一致 | 文档内部一致：`ARCHITECTURE.md` §5 与 `FROZEN.md` 0.3 签名一致 |
| G4 | 未授权修改检查 | 本步为初始冻结，无既有冻结项被改 |
| G5 | 文档已更新 | 五份文档 + OpenAPI 草案均已生成 |
| G6 | 便携性未破 | 文档明确数据随程序目录；注册表仅 URL 协议（Z14） |
| G7 | AI 推理进程隔离未破 | 文档明确 C# 只经 `IInferenceClient`（Z17） |
| G8 | 独立运行不依赖 ZIV | 文档明确独立入口与独立运行（Z28） |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 |
|---|---|---|---|
| 0.1 | 六份文件齐备 | 列出 `D:\devlop\ZIV.AI\` | `DOC/` 五份 + `contracts/openapi.yaml` |
| 0.2 | 与 ZIV 风格一致 | 读文档头部与 `FROZEN.md` | 有修改铁律、修订说明位、铁律编号续接、验收清单格式一致 |
| 0.3 | 定位清楚 | 读 `SPEC.md` §1 | 明确「AI 图像编辑独立应用 + 进程外插件」 |
| 0.4 | 铁律完整 | 读 `SPEC.md` §6 / `FROZEN.md` 0.1 | Z17–Z28 全部；显式继承 Z1–Z16 |
| 0.5 | 分层与依赖方向 | 读 `ARCHITECTURE.md` §2 / §4 | 明确单向 `App → UI → Agent/Tools/Backend → Contracts → ZIV.Core/Imaging` |
| 0.6 | 核心契约草案 | 读 `ARCHITECTURE.md` §5 / `FROZEN.md` 0.3 | 5 个接口 + 6 个模型有签名草案 |
| 0.7 | 决策记录可追溯 | 读 `ARCHITECTURE.md` §9 | D-1 起连续编号，每条有理由 |
| 0.8 | 与 ZIV 对照 | 读 `ARCHITECTURE.md` §10 | 列明定位 / 解决方案 / 共享库 / 进程模型等差异 |
| 0.9 | 未改来源项目 | 问 AI + 目录修改数自检 | `D:\devlop\ZIV` 与 `D:\devlop\ImageGlass-develop` 今日修改数 = 0 |
| 0.10 | 未写代码 | 列出 `D:\devlop\ZIV.AI\` | 无 `.cs` / `.csproj` / `.sln` / `.py` 新增 |
| 0.11 | 遗留项诚实 | 读 `ARCHITECTURE.md` 遗留项 / `DEVLOG.md` | 待裁决项明确列出，未自行拍板 |
| 0.12 | 共享类型引用而非复制 | 搜 `ZivAiEditor.Contracts` 文档描述 | `SKImageRef` 等指向 `ZIV.Core`，未重新定义 |

### Step 0 通过标准

- 通用清单 G1 / G2 因无代码记为 **N/A**；G3–G8 以文档为准
- 专属清单 0.1–0.12 全过

### Step 0 验收后动作

1. 追加 `FROZEN.md` 的 Step 0 段（本步已完成）
2. 追加 `DEVLOG.md` 的 Step 0 记录（本步已完成）
3. 在本文件末尾追加“Step 0 通过，日期：____”（待签）
4. 用户确认后，将文档状态由「草案 v0.1」改为「v1.0 定稿」

---

## Step 规划（计划，非已冻结验收）

> 以下为后续 Step 的目标与验收方向，**待每个 Step 启动时**在本文件末尾追加正式段落，届时冻结。

| Step | 目标 | 关键验收方向 |
|---|---|---|
| Step 1 | 骨架抽离：`ZIV.AI.sln` + 六项目骨架 + `Contracts` 接口空实现；能编译 | 项目树与依赖方向正确；接口与 `FROZEN.md` 一致；`ZIV.Core` / `ZIV.Imaging` 引用方式落地 |
| Step 2 | OpenAPI 客户端落地：按已冻结的 8 端点生成 C# DTO / 客户端 | 生成物可编译；与 `contracts/openapi.yaml` 一致 |
| Step 3 | Python 后端骨架：SGLang 服务 + 健康检查 + 空闲卸载（LightX2V 为 Step 7 可选） | `/v1/health` 可用；模型加载 / 卸载可观测；超时可配（Z21） |
| Step 4 | `HttpInferenceClient`：C# 侧接入后端，任务轮询与取消 | 提交 → 轮询 → 取结果闭环；取消生效；仅经 HTTP（Z17） |
| Step 5 | Planner：`LlmPlanner` + `FallbackPlanner` | 正常解析产出计划；异常回退默认计划（Z22） |
| Step 6 | Executor：多步执行 + 进度 + 取消 + 重跑 + 串行队列 | 步骤按序、进度单调；取消无新请求；重跑可复现（Z18 / Z20） |
| Step 7 | Tools：`ToolRegistry` + 首批工具（重绘 / 局部编辑等） | 工具经注册表查找；单步输出新文件（Z24） |
| Step 8 | MaskCanvas：手绘遮罩（画笔 / 橡皮 / 撤销 / 二值导出） | 坐标映射正确；导出 PNG 只含 0 / 255（Z19） |
| Step 9 | AiEditorWindow：三图槽位 + 提示词模板 + 任务卡片流 | 三图 + 一 prompt 提交；卡片状态 / 进度 / 重跑可用 |
| Step 10 | 集成到 ZIV：CLI / URL 协议 / 命名管道，被 ZIV 可选调用 | ZIV 能唤起并取回结果；进程隔离不破（Z27） |
| Step 11 | 对外接口完善：本地 HTTP API + 单实例 + 文档 | 三入口闭环；只绑回环；契约与 OpenAPI 一致 |

### Step 4–9 规划（Step 3 修订后，追加）

> 上表 Step 3 起按 SGLang 路线规划；随 **D-7 修订**与 **Step 3 重定义**（空闲卸载 + heartbeat），
> 后续目标调整如下（**追加，不修改上表**）。

| Step | 目标 |
|---|---|
| Step 4 | App 层集成 + `HeartbeatLost` 自动重启 |
| Step 5 | Planner（`LlmPlanner` + `FallbackPlanner`） |
| Step 6 | Executor（多步执行 + 重跑） |
| Step 7 | Tools（`ToolRegistry` + 首批工具） |
| Step 8 | MaskCanvas（手绘遮罩） |
| Step 9 | AiEditorWindow（三图 + 卡片流） |
| 优化候选 | 见 `DOC/OPTIMIZATION.md` |

---

## 验收记录

> 每个 Step 验收通过后在此追加一行；未通过不追加。

- Step 0 通过，日期：____（用户填写）
- （后续 Step 在此续写）

---

## 性能预算（待实测校准）

> 目标值为**初始预算**，Step 3 起按实测校准；每次校准走修订说明。

| 指标 | 目标 | 怎么测 |
|---|---|---|
| 单次重绘推理耗时（512×512 级，20 步） | 待实测校准 | 后端日志时间戳 |
| 单次重绘推理耗时（1024×1024 级） | 待实测校准 | 后端日志时间戳 |
| 首帧可见（提交到第一张结果） | 待实测校准 | 秒表 / 日志 |
| 模型加载耗时（冷启动到可推理） | 待实测校准 | 后端日志 |
| 空闲卸载后显存占用 | 接近基线（仅保留最近一个模型，Z21） | `nvidia-smi` |
| 空闲卸载超时默认值 | 可配置（建议默认 5–10 分钟，待裁决） | 配置项 |
| 常驻内存（C# 前端，空载） | 待实测校准 | 任务管理器 |
| 冷启动到主窗口可见 | 待实测校准 | 秒表 / 启动日志 |

> 说明：AI 推理耗时与硬件（RTX 4080 16GB）、模型、步数强相关；本表在 Step 3 / Step 4
> 实测后填写具体数值，**不预先编造**。

---

## Step 1 验收：解决方案骨架与契约落地

**目标**：建立独立解决方案 `ZIV.AI.sln` 与 **7 个 `ZivAiEditor.*` 项目骨架**；
把 `FROZEN.md` 0.3 的 5 接口 + 6 模型落地到 `ZivAiEditor.Contracts`（无业务逻辑）；
落地共享库引用方式；把 `contracts/openapi.yaml` 冻结提前到本步（**8 端点**）；
产出最小可运行 Avalonia 空窗口（标题 `ZIV.AI Editor`）与 `publish.ps1`。
**不改 ZIV / ImageGlass，不复制共享库源码。**

### 通用清单（G1–G8）

| # | 检查项 | 怎么查 | 通过标准 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build D:\devlop\ZIV.AI\src\ZIV.AI.sln -c Release` | 0 错误 |
| G2 | 依赖方向没破 | 列出各项目 `ProjectReference` | `App → UI → Agent/Tools/Backend → Contracts → ZIV.Core/Imaging`；无反向 / 循环；Agent/Tools/Backend 互不引用 |
| G3 | 接口与 FROZEN 一致 | 对照 `FROZEN.md` 0.3 / 1.2 逐条比 | 5 接口 + 6 模型 + 辅助类型签名一致，无新增 / 删改 |
| G4 | 未授权修改检查 | 问 AI：“本 Step 有没有改过已冻结接口？” | 没有（仅按授权更新 D-3/D-7/D-8/D-9 与 §4） |
| G5 | 文档已更新 | 检查 `DEVLOG.md` / `FROZEN.md` | 均已追加 Step 1 记录 |
| G6 | 便携性未破 | 问 AI：“数据是否都写在程序目录？” | `publish.ps1` 发布到 `D:\Program Files\ZIV.AI`；未引入注册表配置 |
| G7 | AI 推理进程隔离未破 | 问 AI：“C# 有没有加载 Python 运行时？” | 无；`HttpInferenceClient` 仅占位（throw-only），不启动 Python |
| G8 | 独立运行不依赖 ZIV | 单独运行 `ZivAiEditor.App.exe` | 能启动空窗口；无 `ZIV.App` / `ZIV.Viewer` 引用 |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 |
|---|---|---|---|
| 1.1 | 解决方案独立 | 查 `src\ZIV.AI.sln` | 只含 7 个 `ZivAiEditor.*`，无 ZIV 项目 |
| 1.2 | 项目矩阵正确 | 查 7 个 `.csproj` | TFM / 输出类型 / 引用与 `FROZEN.md` 1.1 一致 |
| 1.3 | 中央包管理 | 查 `src\Directory.Packages.props` | `ManagePackageVersionsCentrally=true`；ZivAiEditor 的 `PackageReference` 无版本号 |
| 1.4 | SDK 固定 | 查 `global.json`（仓库根） | `10.0.401` + `rollForward latestFeature`（Avalonia 12.1.1 的 XAML 生成器需 Roslyn 4.14 / SDK 10） |
| 1.5 | 契约纯净 | 查 `ZivAiEditor.Contracts` | 无 Avalonia / 平台依赖；接口无方法体；模型为纯属性 |
| 1.6 | 共享库引用正确 | 查 `Contracts` / `UI` / `App` 的引用路径 | `..\..\..\ZIV\src\ZIV.Core\...` / `ZIV.Imaging\...`（三级） |
| 1.7 | 空窗口可运行 | 启动生成的 exe | 进程存活，窗口标题为 `ZIV.AI Editor` |
| 1.8 | AOT 可发布 | `dotnet publish ... -r win-x64` | 生成原生 exe，无错误 |
| 1.9 | OpenAPI 冻结 | 解析 `contracts/openapi.yaml` | 合法 YAML；OpenAPI 3.1；8 路径；字段 snake_case |
| 1.10 | 发布脚本 | 解析 / 运行 `publish.ps1` | 语法正确；发布到 `D:\Program Files\ZIV.AI` 或指定目录 |
| 1.11 | 无业务逻辑 | 查 Backend/Agent/Tools/UI | 仅占位骨架（throw-only / 空类），无真实实现 |
| 1.12 | 未改来源项目 | 目录修改数自检 | 仅 ZIV 的 `bin` / `obj` 因项目引用被构建（已知现象），无源码改动 |

### Step 1 通过标准

- 通用清单 G1–G8 全过（G1 需 0 错误）
- 专属清单 1.1–1.12 全过
- `dotnet test` 冒烟用例通过（`MaskSpec().IsBinary == true`）
- OpenAPI 冻结提前于 Step 0 规划的 Step 2；Step 2 顺延为「DTO / 客户端生成」等

### Step 1 验收后动作

1. 追加 `FROZEN.md` 的 Step 1 段（本步已完成）
2. 追加 `DEVLOG.md` 的 Step 1 记录（本步已完成）
3. 在本文件「验收记录」追加“Step 1 通过，日期：____”（待签）
4. 用户确认后进入 Step 2

---

## Step 2 验收：后端转向 ComfyUI + IPC 契约冻结

**目标**：Python 后端由 SGLang 转向 **ComfyUI v0.37.0 in-process 管线**；以实测为依据冻结
**IPC 传输契约**（Named Pipe），并修订 D-7 / D-8 / Z23。本步以**实测报告 + 文档**为准，
不含 C# 业务实现。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 |
|---|---|---|
| G1 | 代码能编译 | **N/A**（本步不改 C# 代码；既有骨架保持 0 错误） |
| G2 | 依赖方向没破 | **N/A**（本步不改项目引用） |
| G3 | 接口与 FROZEN 一致 | 文档内部一致；`ipc-protocol.md` 与 FROZEN Step 2 一致 |
| G4 | 未授权修改检查 | Step 0/1 冻结行未被改；修订均走 Step 2 修订说明 |
| G5 | 文档已更新 | `FROZEN` / `ARCHITECTURE` / `SPEC` / `ACCEPTANCE` / `DEVLOG` 均追加 Step 2 |
| G6 | 便携性未破 | 模型绝对路径直传；数据仍随程序目录（Z14） |
| G7 | AI 推理进程隔离未破 | C# 只经 Named Pipe（IPC）；不加载 Python 运行时（Z17） |
| G8 | 独立运行不依赖 ZIV | 后端为 ComfyUI 便携版，不依赖 ZIV（Z28） |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 |
|---|---|---|---|
| 2.1 | 后端选型冻结 | 读 `FROZEN.md` Step 2 / `ARCHITECTURE.md` D-7 | ComfyUI v0.37.0 in-process；SGLang 废弃 |
| 2.2 | IPC 契约冻结 | 读 `contracts/ipc-protocol.md` | Named Pipe + 长度前缀；消息与时序完整 |
| 2.3 | OpenAPI 降级 | 读 `FROZEN.md` Step 2 / D-8 | `openapi.yaml` 保留定义，状态为 Schema 参考 |
| 2.4 | Z23 补充 | 读 `SPEC.md` Z23 | 增列「或 IPC 协议」 |
| 2.5 | 实测报告 | 查 `_test_step2/REPORT.md` | 11 项任务均有命令 / 输出 / 结论 |
| 2.6 | 实测结论 | 读 `REPORT.md` 摘要 | 模型可加载、编辑闭环、进度/打断、管道性能、无泄漏 |
| 2.7 | 未改来源 | 目录自检 | `C:\AI\ComfyUI_PIC` 与 ZIV 无本次写入 |
| 2.8 | 未改 Step 0/1 冻结行 | 对照 `FROZEN.md` | Step 0/1 行原样保留，修订在 Step 2 段 |

### Step 2 通过标准

- 通用 G1 / G2 记为 **N/A**（本步不改代码）；G3–G8 以文档 / 实测为准
- 专属 2.1–2.8 全过

### Step 2 验收后动作

1. 追加 `FROZEN.md` 的 Step 2 段（本步已完成）
2. 追加 `DEVLOG.md` 的 Step 2 记录（本步已完成）
3. 在「验收记录」追加“Step 2 通过，日期：____”（待签）
4. 进入 Step 2 实现：`python\` 推理进程 + `IpcInferenceClient` + `IInferenceClient` 扩展

---

## Step 2.2 验收：模型加载与惰性加载

**目标**：首次 `submit` 触发惰性加载；加载期间 `progress` 推送 `loading_model`
（dit / te / vae）；加载完成后 `ping` 报 `loaded`。不含真实推理（2.3）、预览 `0x02`
帧（2.3）、取消 / 打断（2.4）、空闲卸载（Z21，Step 3+）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln` 0 错误 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 改动只在 Backend（依赖 Contracts）+ Tests + Python | ✅ 未新增项目引用 |
| G3 | 接口与 FROZEN 一致 | `IInferenceClient` / `InferenceProgress` 签名未改 | ✅ 扩展走 Backend 层 `InferenceProgressDetail` |
| G4 | 未授权修改检查 | Step 0/1 冻结行未改；消息类型未改 | ✅ 仅追加 |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` 追加 Step 2.2 | ✅ |
| G6 | 便携性未破 | 模型绝对路径直传；未写 `C:\AI\ComfyUI_PIC` | ✅ |
| G7 | AI 推理进程隔离未破 | C# 只经 Named Pipe；不加载 Python 运行时（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 后端为 ComfyUI 便携版（Z28） | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 2.2.1 | 冷启动 `ping` | `IpcModelLoadTests` / 直连 | `model_status = not_loaded` | ✅ |
| 2.2.2 | 首次 `submit` 触发加载 | 订阅 `ProgressReceived` | ≥ 3 条 `loading_model` 帧（dit / te / vae） | ✅ 6 条 |
| 2.2.3 | 加载完成 | `ping` | `model_status = loaded` | ✅ |
| 2.2.4 | 二次 `submit` 不重载 | 清空帧后再提交 | 无 `loading_model` 帧；仍 `loaded` | ✅ |
| 2.2.5 | 显存可观测 | `nvidia-smi` / NVML | 基线 997 → 峰值 1156 → 退出 914 MiB | ✅ |
| 2.2.6 | 加载耗时 | 直连实测 | 冷加载 8.44 s（dit 0.048 / te 0.596 / vae 0.282） | ✅ |
| 2.2.7 | 测试 | `dotnet test ZIV.AI.sln` | 新增 `IpcModelLoadTests` 2 例全过 | ✅ 5 通过 / 0 失败 |
| 2.2.8 | 关闭即退出 | 测试 + 手工 | 无残留 Python 进程；显存回基线 | ✅ 900 MiB |

### Step 2.2 通过标准

- 通用 G1–G8 全过
- 专属 2.2.1–2.2.8 全过

### Step 2.2 验收后动作

1. 追加 `DEVLOG.md` 的 Step 2.2 记录（本步已完成）
2. 在「验收记录」追加“Step 2.2 通过，日期：____”（待签）
3. 进入 Step 2.3：真实推理提交 + `result` / preview `0x02` 帧

---

## Step 2.3 验收：真实推理提交 + preview `0x02` + result

**目标**：首次 `submit` 在惰性加载完成后真正跑通
「encode（Qwen TE）→ CFGGuider(cfg=1.0) → euler + get_schedule → sample（逐步 progress）
→ preview `0x02`（JPEG）→ VAE decode → 输出 PNG → result」。支持 mask（noise_mask，Z19）
与 reference_latents 注入；输出新文件（Z24）。不含取消 / 打断（2.4）、img2img / upscale /
segment / outpaint、空闲卸载（Z21）、heartbeat（Step 3）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln` 0 错误 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 改动只在 Backend / Tests / Python | ✅ 未新增项目引用 |
| G3 | 接口与 FROZEN 一致 | `IInferenceClient` / `InferenceProgress` 签名未改 | ✅ 扩展走 Backend 层新类型 |
| G4 | 未授权修改检查 | Step 0/1 冻结行未改；IPC 消息类型未改 | ✅ 仅按 §3.5 实现 `0x02` |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` 追加 Step 2.3 | ✅ |
| G6 | 便携性未破 | 模型绝对路径直传；未写 `C:\AI\ComfyUI_PIC` | ✅ |
| G7 | AI 推理进程隔离未破 | C# 只经 Named Pipe；不加载 Python 运行时（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 后端为 ComfyUI 便携版（Z28） | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 2.3.1 | 512² 编辑闭环 | `IpcInferenceTests` submit | sampling progress ≥ 3 → preview ≥ 1 → result；输出 PNG 尺寸正确 | ✅ |
| 2.3.2 | preview `0x02` 端到端 | C# `PreviewReceived` | 收到 JPEG 字节且可解码为图片 | ✅ SkiaSharp 解码通过 |
| 2.3.3 | 进度单调 | 采样帧 fraction | 非递减，0 → 1 | ✅ |
| 2.3.4 | 首次推理含 `load_models_gpu` | 直连实测 | 首帧与首个采样步之间约 5.8 s（其余为 encode / decode） | ✅ 5.76 s |
| 2.3.5 | 二次推理不重载 | 直连实测 | 无 `loading_model` 帧；耗时下降 | ✅ 13.74 s → 9.93 s |
| 2.3.6 | 原图不变（Z24） | 前后 SHA-256 | 字节级一致；输出为**新文件** | ✅ 源图 / 遮罩均未变 |
| 2.3.7 | 构建 / 测试 | `dotnet build` / `dotnet test ZIV.AI.sln` | 0 错误；全通过 | ✅ 7 通过 / 0 失败 |
| 2.3.8 | 关闭即退出 | 测试 + 手工 | 无残留 Python 进程；显存回基线 | ✅ 628 MiB |
| 2.3.9 | mask 二值（Z19） | 二值 PNG 传入 | 输出正常；遮罩文件不被写 | ✅ |

### Step 2.3 通过标准

- 通用 G1–G8 全过
- 专属 2.3.1–2.3.9 全过

### Step 2.3 验收后动作

1. 追加 `DEVLOG.md` 的 Step 2.3 记录（本步已完成）
2. 在「验收记录」追加“Step 2.3 通过，日期：____”（待签）
3. 进入 Step 2.4：取消 / 打断（采样循环的 interrupt 检查点已预埋）

---

## Step 2.4 验收：取消 / 打断

**目标**：跑通「submit → 采样中 → cancel → canceled → 进程存活、模型不卸载、显存回落」。
`cancel` / `canceled` 为契约 0.2 既有消息；不含 `RerunAsync`（Step 6）、`GetTaskAsync`、
空闲卸载（Z21，Step 3+）、heartbeat（Step 3）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln` 0 错误 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 改动只在 Backend / Tests / Python | ✅ 未新增项目引用 |
| G3 | 接口与 FROZEN 一致 | `CancelTaskAsync` 为 Step 1 冻结成员，签名未改 | ✅ 仅实现方法体 + 新增事件/属性 |
| G4 | 未授权修改检查 | Step 0/1 冻结行未改；IPC 消息类型未改 | ✅ `cancel` / `canceled` 为 0.2 既有 |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` 追加 Step 2.4 | ✅ |
| G6 | 便携性未破 | 模型绝对路径直传；未写 `C:\AI\ComfyUI_PIC` | ✅ |
| G7 | AI 推理进程隔离未破 | C# 只经 Named Pipe；不加载 Python 运行时（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 后端为 ComfyUI 便携版（Z28） | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 2.4.1 | 采样中取消 | `IpcCancelTests` | 收到 `canceled`；`CancelTaskAsync` 返回 `true` | ✅ |
| 2.4.2 | 进程存活 | 取消后再 `ping` / 二次提交 | 进程仍在，管道未断 | ✅ |
| 2.4.3 | 模型不卸载 | 取消后二次 `submit` | 成功产出输出，无 `loading_model` | ✅ 11.5 s |
| 2.4.4 | 显存回落 | `nvidia-smi` | 取消后由采样峰值回落（激活释放、权重保留） | ✅ 13381 → 13019 MiB（alloc 12657 → 10830） |
| 2.4.5 | 无在飞任务取消 | `IpcCancelTests` | 返回 `false` | ✅ |
| 2.4.6 | 取消延迟 | 直接实测 | 亚秒级 | ✅ 0.056 s |
| 2.4.7 | 构建 / 测试 | `dotnet build` / `dotnet test ZIV.AI.sln` | 0 错误；全通过 | ✅ 9 通过 / 0 失败 |
| 2.4.8 | 关闭即退出 | 测试 + 手工 | 无残留 Python 进程；显存回基线 | ✅ 541 MiB |

### Step 2.4 通过标准

- 通用 G1–G8 全过
- 专属 2.4.1–2.4.8 全过

### Step 2.4 遗留说明（非阻断）

- `loading_model` 阶段不响应 `cancel`（无采样回调；进入采样首个回调后生效，Z18 串行）。
- VAE decode 本身不可打断（decode 前检查一次；约 2 s）。
- 任务进行中到达的 `shutdown` 延后到任务结束处理。

### Step 2.4 验收后动作

1. 追加 `DEVLOG.md` 的 Step 2.4 记录（本步已完成）
2. 在「验收记录」追加“Step 2.4 通过，日期：____”（待签）
3. 进入 Step 3：空闲卸载（Z21）+ heartbeat

---

## Step 3 验收：空闲卸载（Z21）+ heartbeat

> **重新定义**：原 Step 3 规划的「SGLang 服务」随 D-7 修订废弃；本步改为
> **空闲卸载（Z21）+ heartbeat**（见 `FROZEN.md` Step 3 段）。

**目标**：跑通「空闲超时 → 卸载模型 → 显存回落 → 下次推理自动重载」+「Python 心跳 →
C# 检测异常」。不含自动重启（Step 4）、`RerunAsync` / `GetTaskAsync`、其他编辑 op。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln` 0 错误 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 改动只在 Backend / Tests / Python | ✅ 未新增项目引用 |
| G3 | 接口与 FROZEN 一致 | `IInferenceClient` / `InferenceProgress` 签名未改 | ✅ 新增走 Backend 事件 |
| G4 | 未授权修改检查 | Step 0/1 冻结行未改；契约变更走 Step 3 冻结节 | ✅ heartbeat 已授权新增 |
| G5 | 文档已更新 | `FROZEN` / `DEVLOG` / `ACCEPTANCE` / `ipc-protocol` 追加 Step 3 | ✅ |
| G6 | 便携性未破 | 模型绝对路径直传；未写 `C:\AI\ComfyUI_PIC` | ✅ |
| G7 | AI 推理进程隔离未破 | C# 只经 Named Pipe；不加载 Python 运行时（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 后端为 ComfyUI 便携版（Z28） | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 3.1 | 空闲卸载触发 | `IpcIdleUnloadTests` | 空闲超时后 `ping` 报 `not_loaded` | ✅ 3.5 s |
| 3.2 | 显存回落 | `nvidia-smi` / NVML | 卸载后回落（WDDM 滞后 ~1.5 s） | ✅ → ~1038 MiB |
| 3.3 | 卸载后自动重载 | 再次 `submit` | 成功产出输出，模型未重启进程 | ✅ 输出正确 |
| 3.4 | 进程存活 | 卸载后 `ping` | 进程与管道保持 | ✅ |
| 3.5 | heartbeat 周期 | `HeartbeatReceived` | 每间隔收到一帧 | ✅ |
| 3.6 | 失联检测 | `HeartbeatLost` | 停止心跳后按阈值触发 | ✅ |
| 3.7 | 智能显存优化 | 直连实测 | 关闭后采样峰值显著下降 | ✅ 16020 → 9144 MiB；alloc 11.4 GB → 0.7 GB |
| 3.8 | 构建 / 测试 | `dotnet build` / `dotnet test ZIV.AI.sln` | 0 错误；全通过 | ✅ 12 通过 / 0 失败 |
| 3.9 | 关闭即退出 | 测试 + 手工 | 无残留 Python 进程；显存回基线 | ✅ 753 MiB |

### Step 3 通过标准

- 通用 G1–G8 全过
- 专属 3.1–3.9 全过

### Step 3 遗留说明（非阻断）

- 自动重启（订阅 `HeartbeatLost`）留待 Step 4。
- `loading_model` 阶段不响应 `cancel`（Step 2.4 遗留）。
- 未启用 `cudaMallocAsync`（异步池可能影响 `nvidia-smi` 回收口径，待评估）。

### Step 3 验收后动作

1. 追加 `DEVLOG.md` 的 Step 3 记录（本步已完成）
2. 在「验收记录」追加“Step 3 通过，日期：____”（待签）
3. 进入 Step 4：`HeartbeatLost` 自动重启 + `IInferenceClient` 提交入口扩展

---

## Step 4 验收：自动重启 + App 装配 + 优化预留 + 基线

**目标**：① 后端自愈（`HeartbeatLost` 自动重启）② App 层装配通路 ③ pipeline 优化钩子预留
④ 基线性能实测。不含具体 LoRA / MagCache 加载、PE-I2I、`RerunAsync` / `GetTaskAsync`、
`loading_model` 取消、UI 展示。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln` 0 错误 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 改动在 Contracts / Backend / App / Tests / Python | ✅ 未新增项目引用 |
| G3 | 接口与 FROZEN 一致 | `IInferenceClient` 签名未改；`InpaintRequest` 扩展走修订说明 | ✅ 见 `FROZEN.md` Step 4.1 |
| G4 | 未授权修改检查 | Step 0/1/2/3 冻结行未改；契约变更走 Step 4 修订说明 | ✅ |
| G5 | 文档已更新 | `FROZEN` / `DEVLOG` / `ACCEPTANCE` / `ipc-protocol` / `OPTIMIZATION` 追加 Step 4 | ✅ |
| G6 | 便携性未破 | 模型绝对路径直传；未写 `C:\AI\ComfyUI_PIC` | ✅ |
| G7 | AI 推理进程隔离未破 | C# 只经 Named Pipe；不加载 Python 运行时（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 后端为 ComfyUI 便携版（Z28） | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 4.1 | 崩溃恢复 | `IpcAutoRestartTests` 用例 1 | Kill Python → `HeartbeatLost` → 自动重启 → 新任务成功 | ✅ |
| 4.2 | 重启上限 | `IpcAutoRestartTests` 用例 2 | 连续 Kill 超限 → `RestartFailed` → 不再重试 | ✅ 4 次 → `Failed` |
| 4.3 | 重启期间在飞任务 | 代码 + 用例 | 管道断开 → `BACKEND_RESTARTED`，不悬挂 | ✅ |
| 4.4 | 新管道名 | 重启后 `PipePath` | `zivai.infer.{PID}.{seq}` | ✅ |
| 4.5 | 无孤儿进程 / 显存 | `nvidia-smi` / 进程表 | 重启后无孤儿；显存回基线 | ✅ 871 MiB |
| 4.6 | settings.ini 读取 | `SettingsLoader` | `[backend]` 三键生效，缺失用默认 | ✅ |
| 4.7 | pipeline 钩子 | 直连 Python | 注册空 hook 被调用；不实现具体 LoRA | ✅ `applied==1` |
| 4.8 | payload 可选字段 | C# 传 `null` | Python 正常执行 | ✅ 0 hook，行为同 0.4 |
| 4.9 | 基线实测 | `baseline_bench.py` | 数据写入 `OPTIMIZATION.md` §6 | ✅ |
| 4.10 | 构建 / 测试 | `dotnet build` / `dotnet test ZIV.AI.sln` | 0 错误；全通过 | ✅ 14 通过 / 0 失败 |
| 4.11 | 关闭即退出 | 测试 + 手工 | 无残留 Python 进程；显存回基线 | ✅ |

### Step 4 通过标准

- 通用 G1–G8 全过
- 专属 4.1–4.11 全过

### Step 4 遗留说明（非阻断）

- LoRA / MagCache 仅预留钩子，具体加载留待优化步骤。
- 钩子在 `encode` 之后应用，`strength_clip` 本次不作用于 encode。
- `ZivAiEditor.UI` 无 `MainWindow`，B3 以 App 层 `MainWindow` 构造注入落地。
- `RerunAsync` / `GetTaskAsync` / `loading_model` 取消 / PE-I2I 未实现。

### Step 4 验收后动作

1. 追加 `DEVLOG.md` 的 Step 4 记录（本步已完成）
2. 在「验收记录」追加“Step 4 通过，日期：____”（待签）
3. 进入后续 Step（优化项按 `OPTIMIZATION.md` 逐一实测）

---

## Step 5 验收：Planner（`LlmPlanner` + `FallbackPlanner`）

**目标**：实现 Planner——`LlmPlanner`（LLM 生成计划）、`FallbackPlanner`（确定性兜底）、
`ResilientPlanner`（Z22 降级链），并新增 `ILlmClient` 契约。不含 `ILlmClient` 具体实现、
`Executor`（Step 6）、具体工具（Step 7）、UI（Step 9）、PE-I2I；**不跑 GPU 测试**（Z29）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln -c Release` 0 错误 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 改动在 Contracts（新增 `ILlmClient`）+ Agent + Tests；无新增项目引用 | ✅ Agent 仍只依赖 Contracts |
| G3 | 接口与 FROZEN 一致 | `IPlanner` / `EditPlan` / `EditStep` / `IInferenceClient` 签名未改；新增 `ILlmClient` 走 Step 5.3.1 | ✅ |
| G4 | 未授权修改检查 | Step 0–4 冻结行未改；新增契约走 Step 5 冻结节 | ✅ |
| G5 | 文档已更新 | `FROZEN` / `DEVLOG` / `ACCEPTANCE` 追加 Step 5 | ✅ |
| G6 | 便携性未破 | 未引入注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | Planner 不加载 Python / LLM 运行时；`ILlmClient` 由 App 注入（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 5.1 | Fallback 单步计划 | `PlannerTests` | 有主图 → `Steps.Count == 1`；`ToolName` 为 `inpaint`（有 mask）/ `img2img`（无 mask） | ✅ |
| 5.2 | Fallback 缺主图 | `PlannerTests` | `MainImagePath` 为空 → 抛 `ArgumentException` | ✅ |
| 5.3 | LlmPlanner 解析有效 JSON | `PlannerTests` | mock `ILlmClient` 返回预置 JSON → 解析为正确 `EditPlan` | ✅ |
| 5.4 | LlmPlanner 无效输出 | `PlannerTests` | 无效 / 空 JSON / 空 steps / 缺 tool → 抛 `PlannerException` | ✅ |
| 5.5 | 超时控制 | `PlannerTests` | 超时 → 抛 `PlannerException`（默认 30 s，可注入） | ✅ |
| 5.6 | 降级链 | `PlannerTests` | `LlmPlanner` 失败 → `ResilientPlanner` 返回 `FallbackPlanner` 计划 | ✅ |
| 5.7 | 契约冻结 | 对照 `FROZEN.md` 0.3 / 5.3 | 冻结签名一致；`ILlmClient` 仅新增 | ✅ |
| 5.8 | 构建 / 测试 | `dotnet build` / `dotnet test`（仅 Planner 相关） | 0 错误；全部通过 | ✅ 21 通过 / 0 失败 / 1 跳过 |
| 5.9 | 无 GPU 负载 | 测试过程 | 未启动 Python 进程、未加载模型（Z29 / Z30） | ✅ |
| 5.10 | 占位清理 | 查 `ZivAiEditor.Agent` | `PlaceholderPlanner` 已删除 | ✅ |

### Step 5 补完验收：`ILlmClient` 落地（LocalLlmClient + 装配）

> 新增于 Step 5 补完（2026-09-22）。**不改** `ILlmClient` / `IPlanner` / `EditPlan` /
> `EditStep` 签名；新增实现类与装配，不改 Step 0–4 冻结行。

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 5.11 | `ILlmClient` 有实现 | 查 `ZivAiEditor.Backend` | `LocalLlmClient : ILlmClient` + `LlmClientOptions` 存在 | ✅ |
| 5.12 | 客户端单元测试 | `LocalLlmClientTests`（mock HTTP） | 6/6 通过：载荷 / 解析 / HTTP 错误 / 超时 / 非法 JSON / options 温度 | ✅ 6 通过 |
| 5.13 | 分层正确 | 查引用 | `ILlmClient` 在 Contracts；实现 + options 在 Backend；Agent 只经接口 | ✅ |
| 5.14 | App 装配 | 查 `AppContext` | 构造 `HttpClient` → `LocalLlmClient` → `LlmPlanner` → `ResilientPlanner`，暴露 `IPlanner` | ✅ |
| 5.15 | 配置段 | 查 `settings.ini` / `SettingsLoader` | `[llm.planner]` 六键解析；模板含预留段注释 | ✅ |
| 5.16 | 后期接入点 | 查 `OPTIMIZATION.md` §2.1 | 说明复用 `LocalLlmClient` + 不同 `LlmClientOptions` | ✅ |
| 5.17 | 无新抽象 | 查代码 | 无 `ILlmClientFactory` 等额外抽象（§11） | ✅ |
| 5.18 | 真实调用 | `PlannerIntegrationTests` + `llmctl` | llama-server（`qwythos-9b`）就绪 → `LlmPlanner` 真实生成计划；未就绪 → 自跳过 | ✅ 通过（`path=llm`，2 用例；见下） |

### Step 5 通过标准

- 通用 G1–G8 全过
- 专属 5.1–5.10 全过
- 补完项 5.11–5.18 全过

### Step 5 真实调用验证记录（2026-09-22）

> 用 `C:\AI\llm\启动器\llmctl.exe` 拉起 `qwythos-9b`（llama-server :8080），
> 跑 `PlannerIntegrationTests`，验证后 `llmctl stop`。

- **环境（Z30）**：启动前 GPU **882 MiB**（近基线，无 ComfyUI）；`health=down`。
- **启动**：`llmctl start ... qwythos-9b --wait 180` → 健康 **11.3s**，pid=9320；
  `/v1/models` 200（`id=qwythos-9b`，`n_ctx=65536`）；启动后 GPU **10720 MiB**。
- **结果**：`PlannerIntegrationTests` **2 通过 / 0 失败**（不再 Skip）：
  - `ResilientPlanner_Plans_Against_Local_Llm`：**`path=llm`**（未降级），`Steps.Count=1`（`img2img`）。
  - `LlmPlanner_Handles_Complex_Prompt`：`Steps.Count=1`（`img2img`）。
  - 多步**解析**能力由 `PlannerTests.LlmPlanner_Parses_MultiStep_Json_With_Order_And_DependsOn` 覆盖。
  - live LLM 当前只出单步：系统提示词「最少步骤」+ 工具注册表为空（Step 7 提供可组合工具）。
- **停服务**：`llmctl stop` → `stopped pid 9320`；`health=down`；8080 无 LISTENING；
  GPU 回落 **847 MiB**。

### Step 5 遗留说明（非阻断）

- live LLM 目前只生成**单步**计划（工具注册表为空 + 提示词要求最少步骤）；多步场景待
  Step 7 提供可组合工具后复验。
- `LlmPlanner` 不校验 `steps` / `denoise` 值域，越界由工具 / 后端兜底。
- `EmptyToolRegistry` 为临时占位，Step 7 落地真实 `ToolRegistry` 后替换。
- PE-I2I 重写器 / 多图任务未实现（仅预留接缝）。

### Step 5 验收后动作

1. 追加 `DEVLOG.md` 的 Step 5 记录（本步已完成）
2. 在「验收记录」追加“Step 5 通过，日期：____”（待签）
3. 进入 Step 6：`Executor`（多步执行 + 重跑）

---

## Step 6（Executor）验收：多步执行 + 进度 + 取消 + 重跑占位 + 串行队列

> **命名说明**：`FROZEN.md` 另有「Step 6（推理管线优化）」段；本段是 `ACCEPTANCE.MD`
> 规划表所指的 **Step 6 = Executor**。本段与 `DEVLOG.md` 的「Step 6（Executor）」对应。

**目标**：实现 `Executor`（按 `EditPlan` 编排多步、串联中间结果、三级进度、取消、`RerunAsync`
占位、`ExecutionQueue` 串行），并附带最小真实工具 `InpaintTool` 与真实 `ToolRegistry`，
完成 App 层装配。不含 upscale / segment / outpaint 工具（Step 7）、`ITaskStore` 持久化、
队列优先级、UI、PromptOptimizer、GPU 端到端测试（Z29 / Z30）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 改动在 Tools / Agent / Backend / App / Tests；无新增项目引用；Agent/Tools 互不引用 | ✅ |
| G3 | 接口与 FROZEN 一致 | `IExecutor` / `IEditTool` / `IToolRegistry` / `TaskState` / `StepState` 签名未改 | ✅ |
| G4 | 未授权修改检查 | Step 0–5 冻结行未改；无契约变更 | ✅ |
| G5 | 文档已更新 | `FROZEN` / `DEVLOG` / `ACCEPTANCE` / `OPTIMIZATION` 追加 Step 6（Executor） | ✅ |
| G6 | 便携性未破 | 未引入注册表 / 外部路径配置；输出仍新文件（Z24） | ✅ |
| G7 | AI 推理进程隔离未破 | `Executor` 不调 `IInferenceClient`；推理只经 Tool → `IInferenceClient`（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 6.1 | 单步成功 | `ExecutorTests.Single_Step_Plan_Executes_Successfully` | `TaskState.Succeeded`；`OutputImagePath` 正确 | ✅ |
| 6.2 | 多步按序 + 中间传递 | `ExecutorTests.MultiStep_Plan_Executes_In_Order` | 步骤 2 输入 = 步骤 1 输出；执行顺序正确 | ✅ |
| 6.3 | 三步链路 | `ExecutorTests.MultiStep_Plan_With_Three_Steps_Chains_Outputs` | 链路完整、最终输出正确 | ✅ |
| 6.4 | 失败保留中间结果 | `ExecutorTests.Step_Failure_Marks_Task_Failed_And_Keeps_Intermediate` | `Failed`；步骤 1 中间结果保留；后续 `Skipped` | ✅ |
| 6.5 | 取消生效 | `ExecutorTests.Cancel_Stops_Execution` / `External_Token_Cancels_Task` | `Canceled`；后续步骤未执行 | ✅ |
| 6.6 | 依赖违规 | `ExecutorTests.DependsOn_Violation_Skips_Dependent_Step` | 依赖不存在 → 该步 `Skipped`，任务 `Failed` | ✅ |
| 6.7 | 重跑占位 | `ExecutorTests.Rerun_Is_Not_Supported` | 抛 `NotSupportedException`（留接口） | ✅ |
| 6.8 | 工具注册表 | `ToolRegistryTests`（4 例） | 注册 / 查找 / All / 注销 / 同名替换线程安全 | ✅ |
| 6.9 | InpaintTool 参数转发 | `InpaintToolTests.Execute_Forwards_Parameters_To_InferenceClient` | `ToolInput` → `InpaintRequest` 字段正确 | ✅ |
| 6.10 | InpaintTool 输出路径（Z24） | `InpaintToolTests.Execute_Returns_OutputPath` / `..._Derives_From_WorkingDirectory` | 新文件路径；缺省按 `WorkingDirectory/{StepId}.png` | ✅ |
| 6.11 | InpaintTool 进度 | `InpaintToolTests.Execute_Reports_Progress` | `InferenceProgress` 转发为 `StepProgress` | ✅ |
| 6.12 | 串行队列（Z18） | `ExecutionQueueTests.RunAsync_Never_Runs_Two_Items_Concurrently` | 最大并发 = 1 | ✅ |
| 6.13 | 取消透传后端 | 代码审查 + `IpcInferenceClient.SubmitInpaintAsync` | `ct` 取消 → 发 `cancel` 帧（干净 token）→ 抛 OCE；签名不变 | ✅ |
| 6.14 | App 装配 | 查 `AppContext` | 真实 `ToolRegistry` + `InpaintTool` + `ExecutionQueue` + `Executor`；`EmptyToolRegistry` 已删 | ✅ |
| 6.15 | 构建 / 测试 | `dotnet build` / `dotnet test`（仅受影响类） | 0 错误；Step 6 测试 **21 通过 / 0 失败** | ✅ |
| 6.16 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |

### Step 6（Executor）通过标准

- 通用 G1–G8 全过
- 专属 6.1–6.16 全过

### Step 6（Executor）遗留说明（非阻断）

- `RerunAsync` 需 `ITaskStore` 持久化，后续 Step。
- `ExecutionQueue` 无优先级（交互优先留待后续）。
- 注册表当前仅 `inpaint`；`FallbackPlanner` 无 mask 产出的 `img2img` 计划在 Step 7 补工具前
  会因「no tool registered」失败。
- `InpaintTool` 不校验 `steps` / `denoise` 值域；越界由后端兜底。
- `GetTaskAsync` 仍 `NotSupported`。
- GPU 端到端未跑（`nvidia-smi` = 1587 MiB，未满足 Z30；且未获用户明确同意）。
- PromptOptimizer 仅登记于 `OPTIMIZATION.md` §2.1，未实现。

### Step 6（Executor）验收后动作

1. 追加 `DEVLOG.md` 的 Step 6（Executor）记录（本步已完成）
2. 在「验收记录」追加“Step 6（Executor）通过，日期：____”（待签）
3. 进入 Step 7：Tools（`img2img` 等首批工具）

### Step 6（Executor）命名收尾修正（2026-09-22，追加）

> 依据 Qwen-Image-2.1 编辑机制调研与用户裁决：编辑工具命名**统一为 `QW21edit`**
> （QW21 = Qwen-Image-2.1）。本小节**更新**上文专属清单 6.9–6.10 与遗留说明中的工具命名
> （旧行不改，以本小节为准）。无契约变更；不改 `python/server/*` / `ipc-protocol.md`。

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 6.17 | 工具命名统一 | 查 `QwenImage21EditTool.cs` | `Name`/`ToolName = "QW21edit"`；Description / Capabilities 符合 6E.6 | ✅ |
| 6.18 | 注册表命中 | `ToolRegistryTests` / `QwenImage21EditToolTests.Tool_Registers_Under_QW21edit_Only` | `Get("QW21edit")` 命中；`Get("inpaint")` / `Get("img2img")` 返回 null | ✅ |
| 6.19 | FallbackPlanner 统一 | `PlannerTests.FallbackPlanner_No_Mask_Produces_Inpaint_Tool` / `..._With_Mask_...` | 有/无 mask 均产出 `"QW21edit"` | ✅ |
| 6.20 | output_path 守卫 | `QwenImage21EditToolTests.QwenImage21EditTool_OutputPath_Equals_MainImage_Falls_Back` | 与主图相同（含大小写/`..`）→ 回退 `WorkingDirectory/{StepId}.png` | ✅ |
| 6.21 | IPC `op` 不变 | 查 `IpcInferenceClient.cs` | `Op` 仍为 `"inpaint"`（传输层） | ✅ |
| 6.22 | 构建 / 测试 | `dotnet build` / `dotnet test`（仅受影响类） | 0 错误；**36 通过 / 0 失败** | ✅ |
| 6.23 | 经典 img2img 未实现 | 查 `OPTIMIZATION.md` §2.1.2 | 仅登记候选（Issue #9702 / #10063），不实现 | ✅ |

**命名收尾后更新说明**：上文「遗留说明」中「注册表当前仅 `inpaint`；无 mask 产出的 `img2img`
计划会失败」一条**已解决**（有/无 mask 统一 `QW21edit`）；「验收后动作」第 3 条「进入 Step 7：
Tools（`img2img` 等首批工具）」措辞以本小节为准——Step 7 首批工具为 **`upscale` 等**，
不再需要单独的 `img2img`（经典 img2img 为候选特性）。

---

## Step 6.5 验收：分辨率策略（ResolutionPolicy + ModelProfile + 三层透传）

**目标**：让用户/UI 能指定输出分辨率（此前写死 `config.RESOLUTION_SIDE=1536`），
打通 `EditPlan.Resolution → ToolInput.Resolution → InpaintRequest.Resolution →
IPC submit.payload.resolution → Python`；引入 `ModelProfile` 支持未来模型扩展。
不含 upscale / outpaint 工具（Step 7）、UI（Step 9）、`IModelProfileProvider`；**不跑 GPU**（Z29/Z30）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ |
| G2 | 依赖方向没破 | 改动在 Contracts / Backend / Agent / Tools / App / Tests / Python；无新增项目引用 | ✅ |
| G3 | 接口与 FROZEN 一致 | `IInferenceClient`/`IExecutor`/`IEditTool`/`IToolRegistry` 签名未改；仅新增类型与可选字段 | ✅ |
| G4 | 未授权修改检查 | Step 0–6 冻结行未改；契约变更走 Step 6.5 冻结节 | ✅ |
| G5 | 文档已更新 | `FROZEN` / `DEVLOG` / `ACCEPTANCE` / `ARCHITECTURE`§8 / `ipc-protocol` 追加 Step 6.5 | ✅ |
| G6 | 便携性未破 | 未引入注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 分辨率仅经 IPC 传数值；C# 不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 6.5.1 | 新增契约 | 查 `Contracts/Imaging` / `Contracts/Models` | `ResolutionPolicy` / `ResolutionMode` / `ModelProfile` / `AspectPreset` / `ResolutionTier` / `IModelProfileRegistry` 存在且与 6.5.1 一致 | ✅ |
| 6.5.2 | 三层可选字段 | 查 `InpaintRequest` / `EditPlan` / `ToolInput` | 各新增 `ResolutionPolicy? Resolution`，默认 `null`；既有成员未改 | ✅ |
| 6.5.3 | Registry 正确 | `ModelProfileRegistryTests` | Qwen-2.1 字段正确（2048/512/16、三档 1024/1536/2048、7 预设）；未知模型返回 null | ✅ |
| 6.5.4 | 三档翻译 | `ResolutionPolicyTests` | Fast=1024 / Balanced=1536 / HighQuality=2048；`Custom` 抛 `ArgumentException`；`MaxPixels=SafeMaxSide²` | ✅ |
| 6.5.5 | Executor 透传 | `ExecutorTests` | `Plan.Resolution` → `ToolInput.Resolution`；无则 `null` | ✅ |
| 6.5.6 | Tool 透传 | `QwenImage21EditToolTests` | `ToolInput.Resolution` → `InpaintRequest.Resolution`；无则 `null` | ✅ |
| 6.5.7 | IPC 序列化 | 查 `IpcInferenceClient` | `SubmitPayload.Resolution` + `ResolutionPayload`（snake_case）+ `IpcJsonContext` 注册 | ✅ |
| 6.5.8 | Python 读取 | 查 `pipeline.py` | 读 `payload.resolution`（side/area/scale/explicit），覆盖 config；`max_pixels` 超限降级记日志；无 payload 用 config 默认 | ✅（只读确认） |
| 6.5.9 | 契约升版 | 查 `contracts/ipc-protocol.md` / `config.py` | `ipc_version 0.6`；§3.4 有 `resolution`；§7 有 0.5→0.6 变更点；`PROTOCOL_VERSION="0.6"` | ✅ |
| 6.5.10 | 向后兼容 | 代码审查 | 字段缺省 → 行为与 0.5 完全一致（config 默认 1536）；`encode_prompt` 旧签名保留 | ✅ |
| 6.5.11 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类） | 0 错误；**39 通过 / 0 失败** | ✅ |
| 6.5.12 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |

### Step 6.5 通过标准

- 通用 G1–G8 全过
- 专属 6.5.1–6.5.12 全过

### Step 6.5 遗留说明（非阻断）

- upscale / outpaint 未实现（Step 7）；`Scale` / `Explicit` 模式契约与 Python 已就绪，
  C# 侧 resolver / 工具待 Step 7。
- `IModelProfileProvider` 未实现（未来扩展点，仅注释）。
- `wh_ratio` / `ratio_follow`（提示词重写产物）与本步 `ResolutionPolicy` 未打通。
- UI tier 选择未实现（Step 9）；Python resolution 行为未做 GPU 实测（Z29/Z30）。

### Step 6.5 验收后动作

1. 追加 `DEVLOG.md` 的 Step 6.5 记录（本步已完成）
2. 在「验收记录」追加“Step 6.5 通过，日期：____”（待签）
3. 进入 Step 7：Tools（`upscale` 等首批工具，使用 `Scale` / `Explicit` 模式）

> **修订（Step 7）**：上文第 3 条「Step 7 首批工具为 `upscale` 等」以 Step 7 验收为准——
> 经可行性调研，**本步首批工具为 `QW21outpaint` + T2I**；`upscale` 需独立超分模型，
> `segment` 走 Qwen-Image-2.1 **原生 RGBA**（无需独立模型）；二者记为 **Step 7.5 候选**，
> 本步不注册。
>
> **更正（2026-09-22）**：本行初稿曾写「`upscale`（及 `segment`）需独立模型」——其中
> `segment` 部分**有误**，以本更正为准。

---

## Step 7 验收：工具集扩展 + 泛化提交入口 + T2I + MaxPixels 修正

**目标**：① 泛化提交入口（`EditRequest` + `SubmitEditAsync`）② 新增 `QW21outpaint`
③ 支持 T2I（文生图）④ 修正 `MaxPixels`（16:9 预设不再被 clamp）⑤ Segment / Upscale 记为
Step 7.5 候选。范围外：UI（Step 9）、独立超分模型 / 后处理抠图模型、GPU 端到端（Z29/Z30）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ |
| G2 | 依赖方向没破 | 改动在 Contracts / Backend / Agent / Tools / App / Tests / Python；无新增项目引用（仅 Backend 加 `InternalsVisibleTo`） | ✅ |
| G3 | 接口与 FROZEN 一致 | `SubmitInpaintAsync` / `InpaintRequest` / `IEditTool` / `IToolRegistry` / `IExecutor` 签名未改；`IInferenceClient` **仅新增** `SubmitEditAsync` | ✅ |
| G4 | 未授权修改检查 | Step 0–6.5 冻结行未改；契约变更走 Step 7 冻结节 | ✅ |
| G5 | 文档已更新 | `FROZEN` 7.1–7.6 / `DEVLOG` Step 7 / `ACCEPTANCE` Step 7 / `SPEC` §3.1 / `OPTIMIZATION` §7 / `ipc-protocol` 0.7 | ✅ |
| G6 | 便携性未破 | 未引入注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 仅经 IPC 传数值 / 路径；C# 不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 7.1 | 新增契约 | 查 `Contracts/Inference` | `EditRequest`（`Op`/`ImagePath?`/`MaskPath`/`Prompt`/`Steps`/`Seed`/`Denoise`/`OutputPath`/`Resolution`/`Lora`/`Optimizations`/`Anchor`）+ `EditOps` 常量存在 | ✅ |
| 7.2 | 泛化提交 | 查 `IInferenceClient` / `IpcInferenceClient` | `SubmitEditAsync` 就绪；`SubmitInpaintAsync` 构造 `EditRequest{Op=Inpaint}` 委托、签名/语义不变 | ✅ |
| 7.3 | IPC 序列化 | `IpcSubmitMapperTests` | op / anchor / null `image_path` 正确映射；`Resolution` 透传 | ✅ |
| 7.4 | Outpaint 工具 | `QwenImage21OutpaintToolTests` | `CanHandle` 仅 `QW21outpaint`；转发 `Op=outpaint`/`Anchor`/`Resolution`；非 Explicit 分辨率失败且不提交；返回输出路径；进度转发 | ✅ |
| 7.5 | T2I 分支 | `QwenImage21EditToolTests` | 主图空白 + prompt 非空 → `Op=t2i`、`ImagePath=null`、`MaskPath=null`；有主图 → `Op=inpaint` | ✅ |
| 7.6 | Planner T2I | `PlannerTests` | `FallbackPlanner` / `LlmPlanner`：无主图 + prompt → T2I 计划；皆空 → `ArgumentException` | ✅ |
| 7.7 | Python op 分发 | 查 `handlers.py` | `outpaint`→`run_outpaint`；`t2i`/`inpaint`/缺省→`run`；未知→warn+回退；`config.PROTOCOL_VERSION="0.7"` | ✅（只读确认） |
| 7.8 | Outpaint 几何 | `python/server/test_outpaint.py` | `snap16` / `normalize_anchor`（非法→center）/ `anchor_position` / `build_outpaint`（画布+掩膜）；纯 CPU 无 comfy 依赖；**9 通过 / 0 失败** | ✅ |
| 7.9 | MaxPixels 修正 | `ResolutionPolicyTests` | `FromTier` 读 `profile.MaxPixels`；`MaxPixels` 独立于 `SafeMaxSide²`；16:9 预设（4,227,072）不超限 | ✅ |
| 7.10 | 契约升版 | 查 `ipc-protocol.md` / `config.py` | `ipc_version 0.7`；§3.4 op 集合 `t2i`/`inpaint`/`outpaint`、`image_path` 可选、`anchor`；§7 有 0.6→0.7 变更点 | ✅ |
| 7.11 | SPEC 修订 | 查 `SPEC.md` §3.1 | 主图缺失 + prompt 非空 → T2I；皆空 → 拒绝 | ✅ |
| 7.12 | App 注册 | 查 `AppContext.cs` | 注册 `QwenImage21EditTool` + `QwenImage21OutpaintTool`；Segment / Upscale 不注册 | ✅ |
| 7.13 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类） | 0 错误 0 警告；**76 通过 / 0 失败** | ✅ |
| 7.14 | Python 编译 | `python -m py_compile` | 4 个改动文件通过 | ✅ |
| 7.15 | Python 单测 | `python -m unittest test_outpaint` | outpaint 几何 **9 通过 / 0 失败**（纯 CPU） | ✅ |
| 7.16 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |

### Step 7 通过标准

- 通用 G1–G8 全过
- 专属 7.1–7.15 全过

### Step 7 遗留说明（非阻断）

- **Segment（原生 RGBA）未实现**：Qwen-Image-2.1 **原生支持 RGBA 抠图**（VAE 64 通道含 alpha，
  去噪直接生成透明度），走 `QW21segment` 复用 `QW21edit` 同管线，**无需独立模型**；登记于
  `OPTIMIZATION.md` §7.1，作为 **Step 7.5 候选**，可 GPU 实测。
- **Upscale 未实现**：需独立超分模型（Real-ESRGAN / 4x-UltraSharp），登记于 `OPTIMIZATION.md`
  §7.3，作为 **Step 7.5 候选**。
- **对已有 RGB 图抠图（后处理）**：独立抠图模型 BiRefNet / RMBG-2.0，属**独立候选 / 替代路径**
  （**非 Segment 依赖**，与原生路径不叠加），待真实需求引入（`OPTIMIZATION.md` §7.2）。
- **（更正）** 原「Segment / Upscale 未实现：需独立模型（BiRefNet / RMBG-2.0；Real-ESRGAN /
  4x-UltraSharp）」中，**Segment 的「需独立模型」表述有误**，以本节为准。
- **Python 侧测试覆盖不均**：outpaint 几何已由 `test_outpaint.py`（9 例）覆盖；op 分发与
  `run_outpaint` 临时目录清理仍仅 `py_compile` + 只读确认。
- **Outpaint 未 GPU 实测**：画布 / 掩膜对齐、大尺寸 OOM 行为待实测。
- **`LlmPlanner` 尚不能产出 `QW21outpaint`**：系统提示词仅要求 T2I。
- UI 未实现（Step 9）。

### Step 7 验收后动作

1. 追加 `DEVLOG.md` 的 Step 7 记录（本步已完成）
2. 在「验收记录」追加“Step 7 通过，日期：____”（待签）
3. 进入 Step 7.5（原生 Segment / 独立超分）或 Step 8，由用户决定

---

## Step 8 验收：对话式交互逻辑层（CommandParser + EditSession + SessionExporter）

**目标**：实现 `DOC/INTERACTION.md` 的**逻辑层**——`CommandParser`（斜杠命令 + 自然语言 →
单步 `EditPlan`，不走 LLM）、`EditSession`（内存 DAG，不持久化）、`SessionExporter`
（关闭时导出 `session.json` + 图片）。范围外：UI（Step 9）、LLM 意图理解、`@图片N` 多图引用、
会话持久化 / 自动恢复、GPU 端到端（Z29/Z30）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ |
| G2 | 依赖方向没破 | 新增 Agent（`CommandParser` / `EditSession`）+ App（`SessionExporter`）；无反向依赖 | ✅ |
| G3 | 接口与 FROZEN 一致 | `Contracts` 未新增类型；`IInferenceClient` / `IEditTool` / `IToolRegistry` / `IExecutor` / `IPlanner` 签名未改 | ✅ |
| G4 | 未授权修改检查 | Step 0–7 冻结行未改；新增接口走 Step 8 冻结节 | ✅ |
| G5 | 文档已更新 | `FROZEN` Step 8 / `DEVLOG` Step 8 / `ACCEPTANCE` Step 8 / `INTERACTION` 状态 | ✅ |
| G6 | 便携性未破 | `commands.json` 随程序目录（App `Link` 拷贝）；缺失回退内置集 | ✅ |
| G7 | AI 推理进程隔离未破 | `CommandParser` 只产出 `EditPlan`，不调 `Executor` / 后端（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 8.1 | 命令解析 | `CommandParserTests` | `/命令` 匹配并解析；参数替换模板；未知命令 / 参数不符 → 错误 | ✅ |
| 8.2 | 自然语言 | `CommandParserTests` | 非 `/` 输入 → 单步 `QW21edit` 计划，prompt = 原文；无图 → T2I | ✅ |
| 8.3 | 无图无 prompt | `CommandParserTests` | 空输入 → `Success=false` + `ErrorMessage` | ✅ |
| 8.4 | commands.json | `CommandParserTests` | 文件可加载；缺失 → 内置默认集（含 `/换背景` `/去水印`） | ✅ |
| 8.5 | `/扩图` 分辨率 | `CommandParserTests` | `width`/`height` → `ResolutionPolicy{Explicit}` | ✅ |
| 8.6 | 会话 DAG | `EditSessionTests` | `AppendNode` 建父子链；历史节点分支产生兄弟节点 | ✅ |
| 8.7 | 回溯 | `EditSessionTests` | `NavigateTo` 切换当前节点；未知 id 返回 false | ✅ |
| 8.8 | 当前图 | `EditSessionTests` | 无节点 → 根图；皆无 → `null` | ✅ |
| 8.9 | 导出 | `SessionExporterTests` | 写 `session.json` + 拷贝 `{NodeId}.png`；空会话可导出 | ✅ |
| 8.10 | 导出容错 | `SessionExporterTests` | 失败返回 `null`，不抛异常 | ✅ |
| 8.11 | App 装配 | 查 `AppContext.cs` | 构造并暴露 `CommandParser` / `Session` / `SessionExporter` | ✅ |
| 8.12 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类） | 0 错误 0 警告；新增 **19 通过 / 0 失败**；非 GPU 全量 **98 通过 / 0 失败** | ✅ |
| 8.13 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |

### Step 8 通过标准

- 通用 G1–G8 全过
- 专属 8.1–8.13 全过

### Step 8 遗留说明（非阻断）

- **UI 未实现**（Step 9）：聊天流 + 历史节点列表 + 关闭时询问导出。
- **LLM 意图理解 / `@图片N` 多图引用未实现**：后置。
- **会话不持久化 / 不自动恢复**：关闭导出后需后续 Step 手动导入。
- **`SessionExporter` 未在真实关闭流程接线**：本步只装配，询问用户留 Step 9。
- **无图斜杠命令按 T2I 处理**：UI 侧应保证编辑命令有当前图。

### Step 8 验收后动作

1. 追加 `DEVLOG.md` 的 Step 8 记录（本步已完成）
2. 在「验收记录」追加“Step 8 通过，日期：____”（待签）
3. 进入 Step 9（UI 层：聊天流 + 历史节点列表），由用户决定

> **修订（2026-09-22）**：8.x 中原描述「SessionExporter 在 App 层」与
> 「Tests 引用 App / net8.0-windows」已修正为 Agent 层 / net8.0。
> 详见 FROZEN 8R。

---

## Step 9A 验收：CLI 入口 + 单实例 + UI 层（聊天流 + 历史节点列表）

**目标**：落地 `DOC/INTERACTION.md` 的 UI 层——聊天流 + 历史节点列表、关闭询问导出；
对外 CLI 入口（`--image` / `--prompt` / `--mask`，AOT 友好）；单实例（Mutex + Named Pipe，
JSON payload）；`SPEC.md` §3.4 / §7 授权修订。范围外：URL 协议注册、LLM 意图理解、
`@图片N` 多图、与 ZIV 实际联调（Step 9B）、GPU 端到端（Z29 / Z30）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 新增 UI（`LaunchOptions` / `SessionViewModel`）+ App（`SingleInstance` / UI）；UI 只经契约访问 Agent | ✅ |
| G3 | 接口与 FROZEN 一致 | `Contracts` 零新增；`IInferenceClient` / `IEditTool` / `IToolRegistry` / `IExecutor` / `IPlanner` 签名未改 | ✅ |
| G4 | 未授权修改检查 | Step 0–8 冻结行未改；`SPEC` §3.4 / §7 修订走 Step 9A 冻结节 | ✅ |
| G5 | 文档已更新 | `FROZEN` Step 9A / `DEVLOG` Step 9A / `ACCEPTANCE` Step 9A / `INTERACTION` 状态 / `SPEC` 修订 | ✅ |
| G6 | 便携性未破 | 无新增注册表 / 外部路径配置；会话导出目录由用户选 | ✅ |
| G7 | AI 推理进程隔离未破 | UI 不直接调 `IpcInferenceClient`；推理经 `IExecutor` → Tool（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9A.1 | CLI 解析 | `LaunchOptionsTests` | `--image` / `--prompt` / `--mask` 正确解析；无参数 / 缺值 / 未知参数 → 空 / 忽略，不退出 | ✅ 4 通过 |
| 9A.2 | 单实例 | `SingleInstanceTests` | 首实例持有 Mutex；重复唤起经 Pipe 转发 payload；空 payload 不转发 | ✅ 2 通过 |
| 9A.3 | 聊天流 | `SessionViewModelTests` | 输入 → 执行 → 节点新增 → 历史刷新；失败显示错误且不建节点 | ✅ 4 通过 |
| 9A.4 | 切换上下文 | `SessionViewModelTests.Navigate_Switches_Current_And_Rebuilds_Chat` | `NavigateTo` 切换当前节点并重建聊天 | ✅ |
| 9A.5 | 配色一致 | 查 `Themes/ZivColors.axaml` | 8 个常量与 ZIV `MainWindow.axaml` 一致 | ✅ |
| 9A.6 | UI 只经契约 | 查 `SessionViewModel.cs` | 只用 `ICommandParser` / `IExecutor` / `EditSession`；无 `IpcInferenceClient` | ✅ |
| 9A.7 | 关闭询问 | 查 `MainWindow.OnClosing` / `ConfirmDialog` | 会话非空弹窗；「是」→ 选目录 → `ExportAsync`；失败不阻塞 | ✅（代码审查） |
| 9A.8 | SPEC 修订 | 查 `SPEC.md` §3.4 / §7 | §3.4 改为「聊天流 + 历史节点列表」；§7 评审项改为 App 层措辞 | ✅ |
| 9A.9 | AOT 友好 | 查 `Program.cs` / `LaunchOptions` | 无 `System.CommandLine`、无反射；JSON 源生成 | ✅ |
| 9A.10 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类） | 0 错误 0 警告；新增 **10 通过 / 0 失败**；非 GPU 全量 **108 通过 / 0 失败** | ✅ |
| 9A.11 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |

### Step 9A 通过标准

- 通用 G1–G8 全过
- 专属 9A.1–9A.11 全过

### Step 9A 遗留说明（非阻断）

- **URL 协议注册未实现**（后置）。
- **LLM 意图理解 / `@图片N` 多图未实现**（后置）。
- **与 ZIV 实际联调未做**（Step 9B）。
- **遮罩 UI 未实现**：`--mask` 已解析，UI 无绘制入口。
- **会话导入未实现**：导出后可手动保存，导入留后续 Step。
- **Tests TFM 回到 `net8.0-windows`**：为测 App 平台层（`SingleInstance`），见 `FROZEN` 9A.7；
  本步修订 8R.2 的 TFM 部分（8R 针对可下移的 `SessionExporter`）。

### Step 9A 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9A 记录（本步已完成）
2. 在「验收记录」追加“Step 9A 通过，日期：____”（待签）
3. 进入 Step 9B（与 ZIV 侧联调：按钮 + `settings.ini` 配置 exe 路径），由用户决定

### Step 9A 收尾修正验收（2026-09-22）

> 修复 Step 9A UI 的两个缺陷；**无契约变更**，不改 Python / Tool / Executor / Backend 逻辑。

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9A.12 | 状态栏复位 | 查 `MainWindow.SetBusy` | `SetBusy(false)` → 状态文本「就绪」 | ✅ |
| 9A.13 | 预览接线 | 查 `AppContext` / `App.axaml.cs` / `MainWindow` | `PreviewReceived` → `byte[]` → pending `Image`；UI 不引用 Backend 类型 | ✅ |
| 9A.14 | 契约不变 | 查 `Contracts` | 零新增 / 零修改；`IInferenceClient` / `IProgress<T>` 签名不变 | ✅ |
| 9A.15 | 构建 / 测试 | `dotnet build` / `dotnet test`（非 GPU 全量） | 0 错误 0 警告；**109 通过 / 0 失败** | ✅ |
| 9A.16 | 真机预览 | 用户执行一次编辑 | 生成中气泡显示渐进预览图 | ✅ 用户确认 |

---

## Step 9C.1 验收：图像预览窗口（适配 / 缩放 / 拖拽 / 双击 / 点击打开）

**目标**：为当前会话图像提供**独立大图预览窗口**——默认适配窗口、滚轮锚点缩放、
左键拖拽平移、双击切换适配 / 100%、Esc 退出；点击聊天流图片打开。缩放 / 平移状态与
坐标映射下沉 `ZivAiEditor.UI`（net8.0，可单测），渲染控件在 `ZivAiEditor.App`。
范围外：遮罩绘制、ZIV 联调（Step 9B）、GPU 端到端（Z29 / Z30）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 仅 App 新增 NuGet 包（非项目引用）；UI 新增纯逻辑类型；无反向 / 循环依赖 | ✅ |
| G3 | 接口与 FROZEN 一致 | `Contracts` 零新增、零修改；5 接口 + 6 模型签名未改 | ✅ |
| G4 | 未授权修改检查 | Step 0–9A 冻结行未改；新增走 Step 9C.1 冻结节 | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.1 | ✅ |
| G6 | 便携性未破 | 无注册表 / 外部路径配置；图片按传入路径读取 | ✅ |
| G7 | AI 推理进程隔离未破 | 预览仅本地解码文件，不加载 Python / 不 P/Invoke（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.1.1 | 依赖解析 | `dotnet restore` + `project.assets.json` | `UVtools.AvaloniaControls/5.0.1` + `Avalonia/12.1.1`，无 `NU1107` / `NU1605` | ✅ |
| 9C.1.2 | CPM 规范 | 查 `Directory.Packages.props` / `App.csproj` | 版本在 CPM；`PackageReference` 无版本号 | ✅ |
| 9C.1.3 | 纯逻辑下沉 | 查 `ZivAiEditor.UI/Imaging/ImageViewModel.cs` | net8.0；无 Avalonia 依赖 | ✅ |
| 9C.1.4 | 适配比例 | `ImageViewModelTests` | 取两轴较小比例、保持宽高比；含横 / 竖图 | ✅ |
| 9C.1.5 | 缩放锚点 | `ImageViewModelTests` | 缩放前后鼠标下的图像点不变 | ✅ |
| 9C.1.6 | 平移边界 | `ImageViewModelTests` | 偏移钳制到 `[0, 缩放尺寸-视口]`，不露白 | ✅ |
| 9C.1.7 | 控件渲染 | 无头实测 | `AdvancedImageBox` 主题生效（模板已应用）；真实位图 1020×543 | ✅ |
| 9C.1.8 | 滚轮缩放 | 无头实测（模拟输入） | 78% → 94%，锚点保持 | ✅ |
| 9C.1.9 | 拖拽平移 | 无头实测 | 偏移随拖动更新并钳制 | ✅ |
| 9C.1.10 | 双击切换 | 无头实测 | 78% ↔ 100% 双向切换；单击不变 | ✅ |
| 9C.1.11 | 点击打开大图 | 查 `MainWindow.AddPreview` / `OpenImagePreview` | 聊天流图片可点击 → 独立预览窗口 | ✅（代码审查 + 窗口实例化验证） |
| 9C.1.12 | Esc 退出 | 无头实测 | Esc 关闭窗口 | ✅ |
| 9C.1.13 | Z9 资源释放 | 查 `ImagePreview` | `Bitmap` 在替换 / 关闭时 `Dispose`；过期异步结果丢弃 | ✅ |
| 9C.1.14 | Z11 不阻塞 UI | 查 `ImagePreview.LoadAsync` | 解码在后台线程，UI 线程只赋值 | ✅ |
| 9C.1.15 | Z8 文件行数 | 查新增 / 修改文件 | 均 < 600 行 | ✅ |
| 9C.1.16 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类） | 0 错误 0 警告；`ImageViewModelTests` 11 通过；非 GPU 全量 120 通过 | ✅ |
| 9C.1.17 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.1.18 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留 | ✅ |
| 9C.1.19 | 真机交互 | 用户执行一次真机验收 | 打开预览窗口后滚轮 / 拖拽 / 双击 / Esc 均正常 | ⏳ 待用户确认 |
| 9C.1.20 | 窗体样式复用 | 查 `ImagePreview.axaml` + 无头实测 | `WindowDecorations=None` + 自绘标题栏（同主窗体），非默认 Win32 | ✅ |
| 9C.1.21 | 窗口复用 | 无头实测（连续两次打开） | 返回同一 `ImagePreview` 实例，标题随新图更新 | ✅ |

### Step 9C.1 通过标准

- 通用 G1–G8 全过
- 专属 9C.1.1–9C.1.18 全过；9C.1.19 由用户真机确认

### Step 9C.1 遗留说明（非阻断）

- **AOT publish 未复验**：`AdvancedImageBox` 主题含 `ElementName` 反射绑定，
  `dotnet publish`（ILC）下需复验（登记于 `RELEASE-CHECKLIST`）。
- **遮罩坐标映射未接入**：`ImageViewModel.ViewportToImage` 已就绪，遮罩 UI（SPEC §3.9）后续使用。
- **与 ZIV 实际联调未做**（Step 9B）。

### Step 9C.1 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.1 记录（本步已完成）
2. 在「验收记录」追加“Step 9C.1 通过，日期：____”（待签）
3. 进入 Step 9B / 后续 Step，由用户决定

---

## Step 9C.2 验收：抽公共 chrome（子任务 A）+ 编辑器工具栏（子任务 B）

**目标**：**A**：把 `MainWindow` 与 `ImagePreview` 重复的自绘 chrome 抽成公共控件 / 样式，
行为与视觉**完全不变**；**B**：`ImagePreview` 顶部工具栏 + 纯工具状态机（**框架 / 状态，
不接绘制**）。范围外：遮罩 / 裁切绘制（9C.3 / 9C.4）、ZIV 联调、GPU 端到端（Z29 / Z30）。
流程使用 light-rip（Large，A / B 各一轮）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 新增 UI 纯逻辑 + App 控件；无反向 / 循环依赖；无新增项目引用 | ✅ |
| G3 | 接口与 FROZEN 一致 | `Contracts` 零新增、零修改；5 接口 + 6 模型签名未改 | ✅ |
| G4 | 未授权修改检查 | Step 0–9C.1 冻结行未改；新增走 Step 9C.2 冻结节 | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.2 | ✅ |
| G6 | 便携性未破 | 无注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 工具栏 / 状态机不加载 Python，纯本地 UI（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.2.1 | 样式抽离 | 查 `Styles/ChromeStyles.axaml` / `App.axaml` | `Button.tb` / `Path.winIcon` 已抽出并合并 | ✅ |
| 9C.2.2 | TitleBar 抽离 | 查 `Controls/ChromeTitleBar.axaml(.cs)` | `TitleText` + `Body` StyledProperty | ✅ |
| 9C.2.3 | resize 抽离 | 查 `Controls/ChromeResizeBorders.axaml` | 8 条 Border + `ElementRole` | ✅ |
| 9C.2.4 | 行为抽离 | 查 `Controls/ChromeBehavior.cs` | `Init` 统一接线两窗口 | ✅ |
| 9C.2.5 | 重复行数 | 对比两窗口 | 148 → **0** | ✅ |
| 9C.2.6 | 颜色统一 | 查 `ZivColors.axaml` / `ImagePreview.axaml` | 新增 2 Key 且被使用；既有 8 Key 未改 | ✅ |
| 9C.2.7 | 视觉 / 行为回归 | 无头探针 + 真机 | 角色 / 布局 / 标题 / 最大化图标 / 缩放 / Esc 不变 | ✅（探针 ALL PASS；真机用户确认） |
| 9C.2.8 | Z8 行数 | 查新增 / 修改文件 | 均 < 600 行（`MainWindow.axaml.cs` 526、`ImagePreview.axaml.cs` 413） | ✅ |
| 9C.2.9 | 纯逻辑下沉 | 查 `ZivAiEditor.UI/Editing/` | `ToolMode` / `ToolStateMachine` 无 Avalonia 依赖 | ✅ |
| 9C.2.10 | SetTool 切换 | `ToolStateMachineTests` | 切换正确（含 `None`） | ✅ |
| 9C.2.11 | 画笔 / 橡皮互斥 | `ToolStateMachineTests` | 同时只有一个激活 | ✅ |
| 9C.2.12 | Undo 禁用 | `ToolStateMachineTests` | `NotifyUndoStackChanged(false)` → `CanUndo=false` | ✅ |
| 9C.2.13 | 清空遮罩禁用 | `ToolStateMachineTests` | `NotifyMaskChanged(false)` → `CanClearMask=false` | ✅ |
| 9C.2.14 | 初始状态 | `ToolStateMachineTests` | 无工具 / 无遮罩 / 不可撤销 | ✅ |
| 9C.2.15 | 工具栏布局 | 无头探针 + 真机 | 标题栏下、图片区上；6 按钮 | ✅（探针 + 真机） |
| 9C.2.16 | 工具切换 + 光标 | 无头探针 | 点击→`CurrentTool` + `IsChecked` + 光标（Cross / Hand） | ✅ |
| 9C.2.17 | 重置视图 | 无头探针 | 一次性事件 → `Fit()`（94% → 78%） | ✅ |
| 9C.2.18 | 不接绘制 | 查代码 | 清空 / 撤销仅发事件，无绘制逻辑 | ✅ |
| 9C.2.19 | 未改 chrome 控件 | `git diff` | 子任务 A 三控件未被 B 修改 | ✅ |
| 9C.2.20 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类） | 0 错误 0 警告；`ToolStateMachineTests` 9 通过；非 GPU 129 通过 | ✅ |
| 9C.2.21 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.2.22 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留 | ✅ |
| 9C.2.23 | 真机交互 | 用户执行一次真机验收 | 工具栏显示 / 6 按钮切换 / 光标 / Undo 禁用 / 缩放拖拽双击 Esc resize 无回归 | ⏳ 待用户确认 |
| 9C.2.24 | 图标化 | 查 `Assets/Icons/TablerIcons.axaml` + `EditorToolbar.axaml` + 无头探针 | 6 按钮为 Tabler 图标（同 ZIV），几何已解析；无图标库 / 新 NuGet | ✅ |
| 9C.2.25 | 预览顶栏 ZIV 单行 | 查 `ChromeTitleBar` / `EditorToolbar` / `ImagePreview` + 无头探针 | 左工具图标 + 中文件名 + 右窗口按钮；主窗体不变；图标 12.6 / stroke 1.19 / Spacing 2 / Margin 6,0 | ✅ |
| 9C.2.26 | 顶栏按钮可点击 | 无头探针 | 7 个工具按钮 `role=User`；点击可达、双击不最大化 | ✅（探针 role 断言） |
| 9C.2.27 | 右侧槽 + 对比按钮 | 查 `ImagePreview.axaml` + 无头探针 | `重置视图` 在右；`划像对比` 按钮在最右且禁用（功能下一步） | ✅ |
| 9C.2.28 | 去掉缩放滚动条 | 无头探针 | 放大后 `AdvancedImageBox` 的 2 条滚动条 `ScrollBarVisibility.Hidden` | ✅ |
| 9C.2.29 | 历史节点点击不闪退 | 无头复现探针 | 生成后选中历史节点不再崩溃（导航推迟到 Dispatcher，消除选择模型重入） | ✅ |

### Step 9C.2 通过标准

- 通用 G1–G8 全过
- 专属 9C.2.1–9C.2.22 全过；9C.2.23 由用户真机确认

### Step 9C.2 遗留说明（非阻断）

- **UI 接线仅由外部（未入库）无头探针覆盖**；仓库内自动化只覆盖纯 `ToolStateMachine`。
- 清空遮罩 / 撤销**无真实数据源**（9C.3 / 9C.4 接入）；工具栏目前仅框架。
- 光标映射为近似（画笔 / 裁切同用 `Cross`）。

### Step 9C.2 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.2 记录（本步已完成）
2. 在「验收记录」追加“Step 9C.2 通过，日期：____”（待签）
3. 进入 Step 9C.3（遮罩绘制）或后续 Step，由用户决定

---

## Step 9C.2-C 验收：划像对比（父图 vs 当前图）

**目标**：预览窗口把当前节点图像与**父节点**（这一步编辑的输入）图像叠加，用**垂直分割线**
左右拖动对比（左父图 / 右当前图）；再点对比按钮或 Esc 退出，恢复单图；根节点无父图 →
按钮**禁用**。范围外：outpaint 精确对齐（登记遗留）、遮罩 / 裁切绘制、GPU 端到端（Z29 / Z30）。

> **前置调查结论：outpaint 几何不可得**（`EditNode` / `ToolResult` / IPC `result` 帧均无几何），
> 故采用**简化对齐**（父图居中 + 背景填充）；精确对齐登记 `DOC/OPTIMIZATION.md` §7.5。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 新增 UI 纯逻辑 + App 控件；无反向 / 循环依赖；无新增项目引用 | ✅ |
| G3 | 接口与 FROZEN 一致 | `Contracts` 零新增、零修改；5 接口 + 6 模型签名未改 | ✅ |
| G4 | 未授权修改检查 | Step 0–9C.2 冻结行未改；无契约变更；新增走 Step 9C.2-C 只增段 | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.2-C | ✅ |
| G6 | 便携性未破 | 无注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 对比为纯本地 UI，不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.2C.1 | 纯逻辑下沉 | 查 `ZivAiEditor.UI/Editing/CompareState.cs` | 无 Avalonia 依赖，可单测 | ✅ |
| 9C.2C.2 | 无父图禁用 | `CompareStateTests` + 无头探针 | `CanCompare=false` → 按钮禁用 | ✅ |
| 9C.2C.3 | 有父图可切换 | `CompareStateTests` + 无头探针 | `CanCompare=true` → 按钮启用、可切换 | ✅ |
| 9C.2C.4 | 分割线钳制 | `CompareStateTests` | `SetDivider` 钳制到 `[0,1]` | ✅ |
| 9C.2C.5 | 进入 / 退出状态 | `CompareStateTests` + 无头探针 | `IsCompareMode` 正确；退出复位分割线 | ✅ |
| 9C.2C.6 | 方案 A（独立开关） | `CompareStateTests` + 无头探针 | 不占 `ToolMode`；切工具不退出对比 | ✅ |
| 9C.2C.7 | 父图路径来源 | 查 `SessionViewModel.GetParentImagePath` + 测试 | 直接子→根图；孙→父节点输出；根→`null` | ✅ |
| 9C.2C.8 | 渲染（自绘） | 无头探针（渲染像素） | 左父图 / 右当前图；外扩区为背景填充 | ✅（探针渲染核对） |
| 9C.2C.9 | 不静默变形 | 查 `CompareOverlay` 渲染 | 同 zoom 绘制、保持宽高比（非拉伸） | ✅ |
| 9C.2C.10 | Esc 先退出对比 | 无头探针 | 首次 Esc 退出对比不关窗；再次 Esc 关窗 | ✅ |
| 9C.2C.11 | Z9 释放 | 查代码 | 当前图 / 父图位图在替换 / 清空 / 关窗 `Dispose` | ✅ |
| 9C.2C.12 | Z11 后台解码 | 查代码 | 父图解码在 `Task.Run`，代次丢弃过期结果 | ✅ |
| 9C.2C.13 | Z8 行数 | 查新增 / 修改文件 | 均 < 600 行（`ImagePreview.axaml.cs` 523、`ImagePreview.Compare.cs` 164） | ✅ |
| 9C.2C.14 | 未改 chrome / 工具栏 | `git diff` | 9C.2-A 三控件与 `EditorToolbar` 未被改 | ✅ |
| 9C.2C.15 | 未改冻结结构 | 查 diff | `Contracts` / `python/server/*` / `ipc-protocol.md` 未改 | ✅ |
| 9C.2C.16 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类） | 0 错误 0 警告；`CompareStateTests` 13 通过；非 GPU 145 通过 | ✅ |
| 9C.2C.17 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.2C.18 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留 | ✅ |
| 9C.2C.19 | 真机交互 | 用户执行一次真机验收 | 根节点禁用 / 非根点击对比 / 拖分割线 / 再点或 Esc 退出 / 其他工具无回归 | ⏳ 待用户确认 |

### Step 9C.2-C 通过标准

- 通用 G1–G8 全过
- 专属 9C.2C.1–9C.2C.18 全过；9C.2C.19 由用户真机确认

### Step 9C.2-C 遗留说明（非阻断）

- **outpaint 精确对齐未做**：几何未回传 C#，当前为简化对齐（居中 + 背景填充）；
  登记 `DOC/OPTIMIZATION.md` §7.5（含所需冻结结构改动）。
- **UI 接线仅由外部（未入库）无头探针覆盖**；仓库内自动化覆盖纯状态机与父图查找。
- 分割线拖拽仅验证状态钳制与渲染，未做输入模拟。

### Step 9C.2-C 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.2-C 记录（本步已完成）
2. 在「验收记录」追加“Step 9C.2-C 通过，日期：____”（待签）
3. 进入后续 Step（由用户决定）

---

## Step 9C.3 验收：图片导入行（点击 / 拖入，单张为主图）

**目标**：主窗口底部输入区上方新增一行「图片导入 / 拖入」条——初始一个**虚线 `+` 框**；
点击 `+` 打开文件选择器（多选）；可**拖入**多张 / 单张；每导入一张追加**虚线缩略图框**，
末尾**始终保留 `+` 框**；悬停缩略图右上角 **`×`** 移除；**单张设为主图**，多张仅收集
（`@` 引用为后续）。范围外：`@` 引用、GPU 端到端（Z29 / Z30）。流程使用 light-rip（Medium）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 纯逻辑在 UI、控件 / picker 在 App；无反向 / 循环依赖；无新增项目引用 | ✅ |
| G3 | 接口与 FROZEN 一致 | `Contracts` 零新增、零修改；5 接口 + 6 模型签名未改 | ✅ |
| G4 | 未授权修改检查 | 无契约变更；新增走 Step 9C.3 只增段；`EditSession` / `SessionViewModel` 为内部实现 | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.3 | ✅ |
| G6 | 便携性未破 | 无注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 导入为纯本地 UI / 文件选择，不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.3.1 | 初始单框 | 无头探针 + 查 `ImageImportBar.axaml` | 初始恰好 1 个虚线 `+` 框 | ✅ |
| 9C.3.2 | 追加缩略图 + 末尾 `+` | 无头探针 | 每导入一张追加虚线缩略图；`+` 框始终在末尾 | ✅ |
| 9C.3.3 | 点击 `+` 开选择器 | 查 `MainWindow.Import.cs` | `AddRequested` → `OpenFilePickerAsync`（多选，图片过滤） | ✅（代码） |
| 9C.3.4 | 拖入多张 / 单张 | 查代码 + 手动 | Window 级 `DragDrop`，`TryGetFiles` → `AddFiles` | ⏳ 手动确认 |
| 9C.3.5 | 悬停 `×` 移除 | 查 `ImageImportBar.axaml(.cs)` + 无头探针 | `Panel.thumb:pointerover` 显示 `×`；点击移除 | ✅ |
| 9C.3.6 | 单张设为主图 | 无头探针 + `SessionViewModelTests` | `CountBefore==0 && CountAfter==1` → `SetRootImage` | ✅ |
| 9C.3.7 | 多张不设主图 | 无头探针 | 一次拖入多张不改主图 | ✅ |
| 9C.3.8 | 移除不重置会话 | 无头探针 + `ImageImportListTests` | 2→1 移除**不**提升、不重置；0 时 root 保留 | ✅ |
| 9C.3.9 | 去重 | `ImageImportListTests` | 同路径（大小写不敏感）不重复添加 | ✅ |
| 9C.3.10 | 纯逻辑下沉 | 查 `ZivAiEditor.UI/Editing/ImageImportList.cs` | 无 Avalonia 依赖，可单测 | ✅ |
| 9C.3.11 | Z9 / Z11 | 查 `ImageImportBar.axaml.cs` | 缩略图后台解码；剪枝 / 关窗 `Dispose` | ✅ |
| 9C.3.12 | Z8 行数 | 查新增 / 修改文件 | 均 < 600 行（`MainWindow.axaml.cs` 551、`MainWindow.Import.cs` 127） | ✅ |
| 9C.3.13 | 未改冻结结构 | 查 diff | Contracts / `python/server/*` / `ipc-protocol.md` 未改；无新 NuGet | ✅ |
| 9C.3.14 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类） | 0 错误 0 警告；`ImageImportListTests` 11 通过；非 GPU 158 通过 | ✅ |
| 9C.3.15 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.3.16 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留 | ⏳ 手动确认 |
| 9C.3.17 | 真机交互 | 用户执行一次真机验收 | 点选 / 拖入 / 移除 / 单张设主图 / 多张收集无回归 | ⏳ 待用户确认 |

### Step 9C.3 通过标准

- 通用 G1–G8 全过
- 专属 9C.3.1–9C.3.15 全过；9C.3.4 / 9C.3.16 / 9C.3.17 由用户真机确认

### Step 9C.3 遗留说明（非阻断）

- **`@` 引用未实现**：多张仅收集，`@` 规则属后续 Step。
- **OS 真实拖拽 / 原生 picker 仅手动覆盖**。
- **Window 级 `AllowDrop` 可能抑制 `TextBox` 文本拖放**（如需要可收窄到导入行）。

### Step 9C.3 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.3 记录（本步已完成）
2. 在「验收记录」追加“Step 9C.3 通过，日期：____”（待签）
3. 进入后续 Step（`@` 引用 / 遮罩绘制等，由用户决定）

### Step 9C.3 收尾：网页聊天式附件条（2026-09-23）

**用户反馈**：常驻虚线 `+` 框「不够易用」，改为**直接拖入对话框、上方弹出图片**的网页聊天模式。

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| 9C.3.18 | 默认无附件条 | 无头探针 | 初始附件条 `IsVisible=false`、无缩略图 | ✅ |
| 9C.3.19 | 拖入 / 点选后弹出 | 无头探针 + 手动 | 有附件时附件条在输入框上方显示 | ✅（探针；拖拽手动） |
| 9C.3.20 | 移除到空自动收起 | 无头探针 | `Count==0` → 附件条隐藏 | ✅ |
| 9C.3.21 | 图片按钮入口 | 查 `MainWindow.axaml(.Import.cs)` | `PART_BtnAddImage`（Tabler `photo`）→ 文件选择器 | ✅（代码） |
| 9C.3.22 | 单张设主图 / 多张收集 | 无头探针 | 规则不变（`CountBefore==0 && CountAfter==1`） | ✅ |
| 9C.3.23 | 构建 / 测试 | `dotnet build` / `dotnet test` | 0/0；非 GPU 158 通过 | ✅ |

> 说明：本节为 9C.3 的收尾调整，`9C.3.1`（初始单框）/ `9C.3.2`（末尾 `+` 框）/ `9C.3.3`
> （点击 `+`）已由本次改造取代，以本节为准；原行保留不改（记录演进）。

### Step 9C.3 收尾 2：标题栏侧栏按钮 + 输入区圆角化（2026-09-23）

**用户裁决**：标题栏加「侧栏」图标按钮切换历史节点栏；底部输入区**圆角 / 居中 / 不拉通**，
圆角与窗体 `CornerRadius=8` 统一；按钮走 **opencode** 风格。

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| 9C.3.24 | 侧栏切换按钮 | 无头探针 | 标题栏 `PART_BtnToggleSidebar`（IconSidebar）切换 `PART_HistoryPane` 显隐 | ✅ |
| 9C.3.25 | 输入框圆角 | 无头探针 | `PART_Input.CornerRadius = 8`（与窗体一致） | ✅ |
| 9C.3.26 | 输入区居中不拉通 | 查 `MainWindow.axaml` | 输入区 `Grid MaxWidth="760"`（居中，不占满整窗） | ✅ |
| 9C.3.27 | opencode 按钮风格 | 无头探针 + 查 `ChromeStyles.axaml` | 发送 `Button.ocSend`（灰底圆角 + ↑ 箭头）、附件 `Button.oc` | ✅ |
| 9C.3.28 | 构建 / 测试 | `dotnet build` / `dotnet test` | 0/0；非 GPU 158 通过 | ✅ |

---

## Step 9C.3-R 验收：清理 + 字段统一（纯重构 + 注释 + 少量下沉）

**目标**：修复 Step 9C.1–9C.3 期间引入的分层耦合与字段一致性问题（**不改行为**）。九项：
V2（会话 DAG 查询下沉 Agent）、V3（分辨率注入下沉 Agent，新增 `ICommandParser` 重载）、
V4（App 装配统一）、#6（删死状态写入）、#7（导入事件命名统一）、#4（Width/Height 注释）、
#5（MaxPixels / 无消费者字段注释）、#9（InferenceProgressDetail 注释）、#10（CompareOverlay
注释与实现同步）、#2（三个 Duration 口径注释）。范围外：精确对齐（9C.3-G）、GPU 端到端（Z29 / Z30）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | DAG 遍历下沉 Agent；UI 只经接口；无反向 / 循环依赖；无新增项目引用 | ✅ |
| G3 | 接口与 FROZEN 一致 | V3 为 `ICommandParser` **新增重载**（非改签名），已在 `FROZEN.md` 尾部追加修订说明；旧重载保留；`Contracts` 零变更 | ✅ |
| G4 | 未授权修改检查 | 无既有冻结签名被改；V3 为新增（已记录）；V2 / V4 / #6 / #7 为内部实现 / UI 层 | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.3-R | ✅ |
| G6 | 便携性未破 | 无注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 纯本地重构 / 注释，不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.3-R.1 | V2 下沉 | 查 `EditSession.cs` / `SessionViewModel.cs` | `GetParentImagePath` / `GetPathToCurrent` / `GetDepth` 在 Agent；UI 删除本地遍历 | ✅ |
| 9C.3-R.2 | V2 行为不变 | `EditSessionTests` + `SessionViewModelTests` | 根 / 直接子 / 孙 / 未知 / 大小写不敏感；既有用例全过 | ✅ |
| 9C.3-R.3 | V3 新增重载 | 查 `CommandParser.cs` | `ParseAsync(input, session, resolution, ct)` 存在；旧重载委托新重载 | ✅ |
| 9C.3-R.4 | V3 注入语义 | `CommandParserTests` | 注入写入 `EditPlan.Resolution`；parser 自带分辨率优先；`null` 不变 | ✅ |
| 9C.3-R.5 | V3 UI 不再重建 | 查 `SessionViewModel.SubmitAsync` | 无 `new EditPlan` 重建块 | ✅ |
| 9C.3-R.6 | V4 装配统一 | 查 `MainWindow.axaml.cs` / `App.axaml.cs` | 字段默认值删除；`modelProfiles` 必填；调用点已调整 | ✅ |
| 9C.3-R.7 | #6 删死状态 | 查 `MainWindow.SetBusy` | 无非忙 `SetStatus("就绪")` | ✅ |
| 9C.3-R.8 | #7 命名统一 | 查 `ImageImportList` / `ImageImportBar` / 订阅点 | 两层均 `ImagesChanged`；订阅点同步 | ✅ |
| 9C.3-R.9 | #4 注释 | 查五处 `Width/Height` | 均有语义边界 XML 注释；未改名 | ✅ |
| 9C.3-R.10 | #5 注释 | 查 `ResolutionPolicy.MaxPixels` / `ModelProfile` | 注明权威来源 / 无消费者 | ✅ |
| 9C.3-R.11 | #9 注释 | 查 `InferenceProgressDetail` | 注明 Backend 诊断 / 两种投影 | ✅ |
| 9C.3-R.12 | #10 注释同步 | 查 `CompareOverlay.axaml.cs` | 类注释 = 实现（父图铺满当前图矩形） | ✅ |
| 9C.3-R.13 | #2 注释 | 查三处 Duration | 各自作用域已注明；不改行为 / 契约 | ✅ |
| 9C.3-R.14 | Z8 行数 | 查改动文件 | 均 < 600 行（`MainWindow.axaml.cs` 589） | ✅ |
| 9C.3-R.15 | Z9 / Z11 | 查 diff | 无新增 `Bitmap`、无耗时操作 | ✅ |
| 9C.3-R.16 | 构建 / 测试 | `dotnet build` / `dotnet test`（受影响类 + 非 GPU 全量） | 0 错误 0 警告；受影响 48 通过；非 GPU 169 通过 | ✅ |
| 9C.3-R.17 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.3-R.18 | 真机交互 | 用户执行一次真机验收 | 导入 / 分辨率 / 发送 / 历史点击 / 划像对比 / Esc 无回归 | ⏳ 待用户确认 |
| 9C.3-R.19 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留、未占 GPU | ⏳ 待用户确认 |

### Step 9C.3-R 通过标准

- 通用 G1–G8 全过
- 专属 9C.3-R.1–9C.3-R.17 全过；9C.3-R.18 / 9C.3-R.19 由用户真机确认

### Step 9C.3-R 遗留说明（非阻断）

- **9C.2C.9 验收描述与实际实现不符**：原描述「居中 + 背景填充」，实现为「父图铺满当前图矩形」；
  本步已同步代码注释，**未改**既有验收行。精确对齐属 **9C.3-G**。
- **`CompareOverlay.BackgroundFill` 现为死状态**（`Render` 不再读取），保留并注释标明。
- **`ResultReceived` 无生产订阅者**；三个 Duration 口径不互相校验，本步仅注释。
- **V1（`SessionViewModel` 直接持有 `EditSession`）延后 9C.5**。

### Step 9C.3-R 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.3-R 记录（本步已完成）
2. 在「验收记录」追加"Step 9C.3-R 通过，日期：____"（待签）
3. 进入后续 Step（精确对齐 9C.3-G / `@` 引用 / 遮罩绘制等，由用户决定）

---

## Step 9C.5 验收：架构底座重构（会话接口抽象到 Contracts）

**目标**：修复 V1 硬违规（UI 直接持有 Agent 实现类 `EditSession` / `EditNode`），把会话接口抽象
到 `Contracts`（`IEditSession` / `IEditNode` / `IEditSessionWriter`），`SessionViewModel` 改持接口。
**纯重构，行为不变**。范围外：GPU 端到端（Z29 / Z30）。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 会话抽象下沉 Contracts；UI 不再引用 Agent 实现类；无反向 / 循环依赖；无新增项目引用 | ✅ |
| G3 | 接口与 FROZEN 一致 | 新增 3 接口（`Contracts`）；`ICommandParser` 参数收窄为 `IEditSession`（已授权，`FROZEN` 9C.5.3 尾部说明）；未改 8.1 既有行 | ✅ |
| G4 | 未授权修改检查 | 无既有冻结签名被改；`ICommandParser` 参数变更为抽象化必需，已记录；其余为内部实现 | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.5；`ARCHITECTURE` §6 追加 6.1 | ✅ |
| G6 | 便携性未破 | 无注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 纯本地重构，不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.5.1 | 新增接口 | 查 `Contracts/Planning/` | `IEditNode` / `IEditSession`（只读）/ `IEditSessionWriter`（写）存在 | ✅ |
| 9C.5.2 | EditSession 实现 | 查 `EditSession.cs` | `EditSession : IEditSession, IEditSessionWriter`；`EditNode : IEditNode` | ✅ |
| 9C.5.3 | UI 不再引用实现类 | `grep` UI 下 `\bEditSession\b`/`\bEditNode\b` | 0 命中 | ✅ |
| 9C.5.4 | SessionViewModel 改持接口 | 查 `SessionViewModel.cs` | `_session: IEditSession`；`_writer: IEditSessionWriter`；`HistoryItem.Node: IEditNode` | ✅ |
| 9C.5.5 | 写入走 writer | 查 `SessionViewModel.cs` | `SetRoot`/`ResetToRoot`/`AppendNode`/`NavigateTo` 经 `_writer` | ✅ |
| 9C.5.6 | parser 参数收窄 | 查 `CommandParser.cs` | `ParseAsync(..., IEditSession, ...)`（两重载） | ✅ |
| 9C.5.7 | App 装配 | 查 `MainWindow.axaml.cs` | 同一 `EditSession` 传 IEditSession + IEditSessionWriter；导出用具体 `_session` | ✅ |
| 9C.5.8 | 行为不变 | `EditSessionTests` / `SessionViewModelTests` / `CommandParserTests` / `SessionExporterTests` | 全过（41） | ✅ |
| 9C.5.9 | 接口边界 | `EditSessionTests` | `GetDepth(null)=0`；接口一致性用例 | ✅ |
| 9C.5.10 | 状态依赖规则 | 查 `ARCHITECTURE.md` §6.1 | 4 条规则已追加（只增） | ✅ |
| 9C.5.11 | BackgroundFill | 查 `ImagePreview.axaml` / `CompareOverlay` | 有绑定 → 保留并报告（不删） | ✅ |
| 9C.5.12 | Z8 行数 | 查改动文件 | 均 < 600 行（`MainWindow.axaml.cs` 594） | ✅ |
| 9C.5.13 | 构建 / 测试 | `dotnet build` / `dotnet test` | 0/0；受影响 41 通过；非 GPU 170 通过 | ✅ |
| 9C.5.14 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.5.15 | 真机交互 | 用户执行一次真机验收 | 图片导入 / 编辑 / 历史点击 / 划像对比 / Esc 无回归 | ⏳ 待用户确认 |
| 9C.5.16 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留、未占 GPU | ⏳ 待用户确认 |

### Step 9C.5 通过标准

- 通用 G1–G8 全过
- 专属 9C.5.1–9C.5.14 全过；9C.5.15 / 9C.5.16 由用户真机确认

### Step 9C.5 遗留说明（非阻断）

- **UI→Agent 依赖未收窄**：保留 `ProjectReference`（因 `ICommandParser` 为 Agent 接口）；
  彻底收窄需将 `ICommandParser` 上提 `Contracts`（另立步 / 需授权）。
- **`CompareOverlay.BackgroundFill` 未删**：XAML 有绑定，保留（死状态）。
- **FROZEN 8.1 既有不一致**：`ISessionExporter` / `SessionExporter` 实际在 Agent，文档记为 App；
  本步未改既有行，仅登记。
- **9C.5-B（不阻塞）**：`SessionViewModel.AppendNode`（执行成功回写节点）属 UI 代 Agent 编排，
  应下沉至 Agent；涉及 `IExecutor` 设计变更，单独立步。

### Step 9C.5 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.5 记录（本步已完成）
2. 在「验收记录」追加"Step 9C.5 通过，日期：____"（待签）
3. 进入后续 Step（`ICommandParser` 上提 / 精确对齐 9C.3-G / 遮罩绘制等，由用户决定）

---

## 待办登记：导入图片与多图合照（9C.5-C / 9C.5-D，2026-09-23）

> 用户真机反馈，**仅登记，未修**。根因与证据详见 `DEVLOG.md` 同段。本段**不改任何代码**。

| # | 问题 | 严重度 | 现象 | 处理方向 |
|---|---|---|---|---|
| 9C.5-C | 发送后附件条未清空（图片残留输入区） | 中（UI bug） | 导入图后发送，缩略图仍在输入区上方 | 待后续小修：`MainWindow.SubmitAsync` 调 `_importBar.Clear()` |
| 9C.5-D | 多图引用 / 合照未实现（`@图N` / 「图N」） | 高（功能缺失） | 「和图2合照」不生效，输出无合照迹象 | 待另立步：`EditRequest` 扩展参考图字段 + 工具 + Python 后端 + 解析规则（先出计划） |

- 说明：9C.5-D 是 9C.3 已登记遗留（「`@` 引用未实现」）的延续；端到端涉及契约与后端，
  单独立步。
- 9C.5-C 为独立小修，与 9C.5-D 无依赖。

---

## Step 9C.6 验收：根节点实体化（导入原图成为 DAG 首个节点）

**目标**：让「导入的原图」成为会话 DAG 的第一个 `EditNode`（`ParentNodeId = null`），使历史列表
从原图开始、用户可回到原图。底座级数据结构改动，**编辑流程行为保持兼容**。
范围外：GPU 端到端（Z29 / Z30），真机交互由用户执行。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 仅改 Agent / UI / App 内部；无新增项目引用、无反向 / 循环依赖 | ✅ |
| G3 | 接口与 FROZEN 一致 | `IEditSession` / `IEditSessionWriter` 签名**零变化**（仅行为语义变化，`FROZEN` 9C.6 尾部说明）；未改已冻结行 | ✅ |
| G4 | 未授权修改检查 | 无既有冻结签名被改；`RootImagePath` 由公开 set 变派生只读属**实现类内部**变化（非契约成员，接口本为 get-only） | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.6 | ✅ |
| G6 | 便携性未破 | 无注册表 / 外部路径配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 纯本地数据结构改动，不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.6.1 | root 实体化 | 查 `EditSession.cs` | `SetRoot` / `ResetToRoot` 建 root 节点（`ParentNodeId = null`、`Command = "原图"`），`CurrentNodeId = root.NodeId` | ✅ |
| 9C.6.2 | `RootImagePath` 派生 | 查 `EditSession.cs` | `public string? RootImagePath => _rootNode?.ImagePath`（无 public set） | ✅ |
| 9C.6.3 | `GetHistory` 含根 | `EditSessionTests` | `GetHistory()[0]` 为 root；root 恒排第一 | ✅ |
| 9C.6.4 | 首编辑节点挂 root | `EditSessionTests.AppendNode_Builds_DAG` | 首编辑节点 `ParentNodeId == root.NodeId` | ✅ |
| 9C.6.5 | `GetPathToCurrent` | `EditSessionTests` | root 当前 → `[root]`；编辑后 → `[root,…,current]`；无 current → 空 | ✅ |
| 9C.6.6 | `GetDepth` | `EditSessionTests.GetDepth_Counts_Ancestors` | root = 0；直接子 = 1；孙 = 2 | ✅ |
| 9C.6.7 | `GetParentImagePath` 三态 | `EditSessionTests` | 编辑节点 → 父 output；root 自身 → null；不匹配 → null | ✅ |
| 9C.6.8 | `AppendNode(null)` 选 A | `EditSessionTests` | 有 root → 挂 root；无 root → 保持 null | ✅ |
| 9C.6.9 | 导出含根图 | `SessionExporterTests` | 输出目录存在 `{rootNodeId}.png`；JSON `nodes` 含 root | ✅ |
| 9C.6.10 | 聊天流无重复原图 | `SessionViewModelTests.Navigate_To_Root_Node…` | `RebuildContext` 跳过 root；无 `User "原图"` 气泡；仅一个「起始图像」气泡 | ✅ |
| 9C.6.11 | 划像对比 | 查 `ImagePreview.Compare` / `GetParentImagePath` | root → 对比禁用；编辑结果 → 父图 = 原图 | ✅ |
| 9C.6.12 | 签名零变化 | 查 `Contracts/Planning/` | `IEditSession` / `IEditSessionWriter` 未改 | ✅ |
| 9C.6.13 | Z8 行数 | 查改动文件 | 均 < 600 行（`EditSession.cs` 251） | ✅ |
| 9C.6.14 | 构建 / 测试 | `dotnet build` / `dotnet test` | 0/0；受影响 57 通过；非 GPU 175 通过 | ✅ |
| 9C.6.15 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.6.16 | 真机交互 | 用户执行一次真机验收 | 导入即现「原图」/ 编辑挂原图下 / 点原图切换与分支 / 点原图禁用对比 / 导含原图 / 无回归 | ⏳ 待用户确认 |
| 9C.6.17 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留、未占 GPU | ⏳ 待用户确认 |

### Step 9C.6 通过标准

- 通用 G1–G8 全过。
- 专属 9C.6.1–9C.6.15 全过；9C.6.16 / 9C.6.17 由用户真机确认。

### Step 9C.6 遗留说明（非阻断）

- **Contracts XML 注释**：`IEditSession` / `IEditSessionWriter` 文档仍述旧语义（「null = root 当前」
  「不建节点」等）；**签名未变**，登记为后续文档整理。
- **`AppendNode` 未知 `parentId`**：静默建悬空节点（遍历有 `MaxTreeDepth` 保护，终止安全）；
  既有行为，本步未改。
- **9C.5-C / 9C.5-D** 未动（多图清空 / 多图引用，另立步）。
- **未跑 GPU 端到端**（Z29 / Z30）。

### 对后续步的接口

- **9C.4 裁切**：本步完成后，裁切结果可直接 `AppendNode(当前节点 id, 裁切输出, "裁切")` 进会话，
  **无需再改底座**；选中 root 后裁切即从原图分支。裁切 UI / 遮罩数据仍属 9C.4。

### Step 9C.6 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.6 记录（本步已完成）
2. 在「验收记录」追加"Step 9C.6 通过，日期：____"（待签）
3. 进入后续 Step（9C.4 裁切 / 9C.5-C / 9C.5-D 等，由用户决定）

---

## Step 9C.4 验收：裁切工具（内裁）

**目标**：在 `ImagePreview` 窗口实现 PS 风格矩形裁切：`ToolMode.Crop` 进入 → 拖拽建框 / 拖动 /
8 手柄缩放 → 框外暗化 → 确认生成新文件（Z24）并 `AppendNode` 进会话；取消 / Esc 退出。
**本步只做内裁**；外扩留 9C.4-B。范围外：GPU 端到端（Z29 / Z30），真机交互由用户执行。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 仅改/增 UI / App 内部；无新增项目引用、无反向 / 循环依赖 | ✅ |
| G3 | 接口与 FROZEN 一致 | `IEditSession` / `IEditSessionWriter` 签名**零变化**；新增类型均非契约，`FROZEN` 9C.4 尾部记录 | ✅ |
| G4 | 未授权修改检查 | 无既有冻结签名被改；`SessionViewModel.AppendEditNode` 为非契约新方法 | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.4 | ✅ |
| G6 | 便携性未破 | 输出落源图同目录 / 程序目录 `output/`；无注册表 / 外部配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 裁切为纯本地 Skia 处理，不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖（仅复用 ZIV.Imaging 共享库） | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.4.1 | 纯逻辑状态机 | 查 `CropState.cs` | 无 Avalonia；`IsActive`/`HasRect`/`IsDragging`/`DragHandle` | ✅ |
| 9C.4.2 | 8 手柄 + Move 命中 | `CropStateTests` | 四角 + 四边中点 + Move，容差命中正确 | ✅ |
| 9C.4.3 | 边界钳制 | `CropStateTests` | 矩形不越界（移动 / 缩放 / 建框） | ✅ |
| 9C.4.4 | 最小尺寸 | `CropStateTests` | ≥16×16；过小建框被丢弃 | ✅ |
| 9C.4.5 | 坐标映射 | 查 `ImagePreview.Crop.cs` | 复用 `ImageViewModel.ViewportToImage` / `ImageToViewport` | ✅ |
| 9C.4.6 | 叠加渲染 | 查 `CropOverlay.axaml.cs` | 框外暗化 + 描边 + 8 手柄；`IsHitTestVisible=false` | ✅ |
| 9C.4.7 | 初始全图框 | 查 `ImagePreview.Crop.cs` | 进入裁切 `SetFullRect()` | ✅ |
| 9C.4.8 | 屏蔽平移 | 查 `ImagePreview.axaml.cs` | 裁切激活时 `OnPressed/Moved/Released` 走裁切，不平移；滚轮缩放保留 | ✅ |
| 9C.4.9 | Esc 优先级 | 查 `OnKeyDown` | 裁切 → 对比 → 关窗 | ✅ |
| 9C.4.10 | 自动退对比 | 查 `UpdateCropMode` | 进裁切前 `CompareState.SetCompareMode(false)` | ✅ |
| 9C.4.11 | 落盘规则 | `ImageCropperTests` | `<stem>_crop_<ts>.png` 同目录；冲突 `_1..`；源图空 → `output/` | ✅ |
| 9C.4.12 | 新文件 / 不覆盖 | `ImageCropperTests` | 输出存在且尺寸正确；源图字节级不变（Z24） | ✅ |
| 9C.4.13 | 进会话 | `SessionViewModelTests` | `AppendEditNode` 按源图节点挂载 + 回退当前；`command="裁切"` | ✅ |
| 9C.4.14 | 确认后预览切换 | 查 `ConfirmCropAsync` | `LoadImage(output)` + 退出裁切 + 触发 `CropCompleted` | ✅ |
| 9C.4.15 | 失败不静默 | 查 `ConfirmCropAsync` | 失败 → 退出裁切 + toast 提示 + 不进会话 | ✅ |
| 9C.4.16 | AOT 验证 | `dotnet publish` | NativeAOT 成功，含 Magick 原生库（Option A 可用） | ✅ |
| 9C.4.17 | Z8 行数 | 查改动文件 | 均 < 600 行（`MainWindow.axaml.cs` 597） | ✅ |
| 9C.4.18 | 构建 / 测试 | `dotnet build` / `dotnet test` | 0/0；受影响 83 通过；非 GPU 214 通过 | ✅ |
| 9C.4.19 | 无 GPU 负载 | 测试 / publish 过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.4.20 | 真机交互 | 用户执行一次真机验收 | 建框 / 移动 / 缩放 / 暗化 / 确认生成 + 历史「裁切」/ 取消不变 / 无回归 | ⏳ 待用户确认 |
| 9C.4.21 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留、未占 GPU | ⏳ 待用户确认 |

### Step 9C.4 通过标准

- 通用 G1–G8 全过。
- 专属 9C.4.1–9C.4.19 全过；9C.4.20 / 9C.4.21 由用户真机确认。

### Step 9C.4 遗留说明（非阻断）

- **外扩裁切（9C.4-B）**：本步只做内裁；外扩（画布扩展 + outpaint）另立步。
- **超大图裁切**：`LoadThumbnail` 无 2GB 像素缓冲上限预检；极端大图返回 `null` 并提示失败。
- **`CropAsync` 取消令牌**：当前恒默认，能力保留未接线。
- **未跑 GPU 端到端**（Z29 / Z30）。

### Step 9C.4 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.4 记录（本步已完成）
2. 在「验收记录」追加"Step 9C.4 通过，日期：____"（待签）
3. 进入后续 Step（9C.4-B 外扩 / 9C.5-C / 9C.5-D 等，由用户决定）

---

## Step 9C.6-B 验收：裁切语义重构（节点级裁切状态）

**目标**：把裁切从「AI 编辑步骤（建节点）」改为「节点的内在属性」——每节点最多一个裁切状态、
可反复调整、**不建节点**；送 AI 管线用裁切结果；划像对比与裁切互斥。范围外：GPU 端到端
（Z29 / Z30），真机交互由用户执行。

### 通用清单（G1–G8）

| # | 检查项 | 通过标准 | 结果 |
|---|---|---|---|
| G1 | 代码能编译 | `dotnet build src\ZIV.AI.sln -c Release` 0 错误 0 警告 | ✅ 0 错误 0 警告 |
| G2 | 依赖方向没破 | 仅改/增 Contracts（新增）/ Agent / UI / App；无新增项目引用 | ✅ |
| G3 | 接口与 FROZEN 一致 | `IEditNode` / `IEditSession` / `IEditSessionWriter` **仅新增成员**，既有签名零变化；`FROZEN` 9C.6-B 尾部记录 | ✅ |
| G4 | 未授权修改检查 | 契约变更已获用户授权（新增 `CropSpec` / `Crop` / 3 方法） | ✅ |
| G5 | 文档已更新 | `DEVLOG` / `ACCEPTANCE` / `FROZEN` 追加 Step 9C.6-B | ✅ |
| G6 | 便携性未破 | 裁切输出落源图同目录 / `output/`；无注册表 / 外部配置 | ✅ |
| G7 | AI 推理进程隔离未破 | 裁切为纯本地 Skia 处理，不加载 Python（Z17） | ✅ |
| G8 | 独立运行不依赖 ZIV | 未新增对 ZIV 应用的依赖 | ✅ |

### 专属清单

| # | 检查项 | 怎么查 | 通过标准 | 结果 |
|---|---|---|---|---|
| 9C.6B.1 | `CropSpec` 契约 | 查 `Contracts/Planning/CropSpec.cs` | X/Y/Width/Height/ResultImagePath（init） | ✅ |
| 9C.6B.2 | `EditNode.Crop` | 查 `IEditNode.cs` / `EditSession.cs` | 可选字段，默认 null | ✅ |
| 9C.6B.3 | `SetNodeCrop` 更新 | `EditSessionTests` | 重建节点保留身份；未知 no-op；root 引用同步 | ✅ |
| 9C.6B.4 | 不建节点 | `SessionViewModelTests` | 设/改裁切后 `History.Count` 不变 | ✅ |
| 9C.6B.5 | 送管线用裁切 | 查 `CommandParser.cs` | 用 `GetCurrentPipelineImagePath()` | ✅ |
| 9C.6B.6 | pipeline 路径 | `EditSessionTests` | `Crop?.ResultImagePath ?? ImagePath`；无 current → null | ✅ |
| 9C.6B.7 | 父 pipeline 路径 | `EditSessionTests` | 新增方法返回父裁切结果；root/未知 → null | ✅ |
| 9C.6B.8 | `GetParentImagePath` 不变 | `EditSessionTests` 既有用例 | 全过（语义未变） | ✅ |
| 9C.6B.9 | 初始框 75% | `CropStateTests` | 居中，四周 12.5% | ✅ |
| 9C.6B.10 | 移动 + 8 手柄 | `CropStateTests` | 整框移动 + 8 手柄缩放；框外按下重建 | ✅ |
| 9C.6B.11 | 对比互斥 | 查 `ImagePreview.Crop/Compare` | 进裁切退对比；进对比退裁切；退出对比不自动进裁切 | ✅ |
| 9C.6B.12 | 对比内容 | 查 `ImagePreview.Compare` / `MainWindow.Preview` | 左=父 pipeline 图；右=当前节点原图；root 禁用 | ✅ |
| 9C.6B.13 | 预览显示 | 查 `LoadNode` | 显示 `Crop?.ResultImagePath ?? ImagePath`；再次进入保留上次框 | ✅ |
| 9C.6B.14 | 导出裁切 | `SessionExporterTests` | `crop` 元数据 + 相对 `result_image_path`；拷贝 `{NodeId}_crop.png` | ✅ |
| 9C.6B.15 | Z8 行数 | 查改动文件 | 均 < 600 行（`ImagePreview.axaml.cs` 584） | ✅ |
| 9C.6B.16 | 构建 / 测试 | `dotnet build` / `dotnet test` | 0/0；非 GPU 228 通过（修订后） | ✅ |
| 9C.6B.17 | 无 GPU 负载 | 测试过程 | 未启动 Python、未加载模型（Z29 / Z30） | ✅ |
| 9C.6B.18 | 真机交互 | 用户执行一次真机验收 | 75% 初始框 / 任意点重建 / 确认落盘切图不建节点 / 再进入保留框 / 送管线用裁切 / 对比互斥 / 无回归 | ⏳ 待用户确认 |
| 9C.6B.19 | 启动 / 无残留 | 启动 exe + 进程表 | 窗口正常、无新 Python 进程；结束无残留、未占 GPU | ⏳ 待用户确认 |

### Step 9C.6-B 通过标准

- 通用 G1–G8 全过。
- 专属 9C.6B.1–9C.6B.17 全过；9C.6B.18 / 9C.6B.19 由用户真机确认。

### Step 9C.6-B 遗留说明（非阻断）

- 预览 `_nodeCrop` 为快照（当前单窗流程一致；未来多预览 / 会话重载需通知机制）。
- 每次裁切产生新文件、旧裁切文件不删（Z24 一致）。
- 非节点路径打开预览时裁切确认静默 no-op（低影响）。
- 外扩裁切 9C.4-B 未做。
- **未跑 GPU 端到端**（Z29 / Z30）。

### Step 9C.6-B 验收后动作

1. 追加 `DEVLOG.md` 的 Step 9C.6-B 记录（本步已完成）
2. 在「验收记录」追加"Step 9C.6-B 通过，日期：____"（待签）
3. 进入后续 Step（9C.4-B 外扩 / 9C.5-C / 9C.5-D 等，由用户决定）

---

## Step 9C.6-B 修订（2026-09-23 · 真机反馈）

> 用户真机验收发现三项，已修复并复审。**不改已冻结行**，本段为追加修订说明。

| # | 问题 | 修复 |
|---|---|---|
| R1 | 裁切后再次进入裁切没保存 / 不能调整 | 进入裁切加载**原图**（坐标基准），恢复存储框；退出恢复显示图；确认恒以原图为源（不链式裁切） |
| R2 | 裁切结果没返回聊天记录 | `RebuildContext` 用 pipeline 图渲染气泡；`SetNodeCrop` 重建聊天 |
| R3 | 拖拽重建方案废弃 | 恢复**整框移动 + 8 手柄缩放**（框外按下重建） |

- **复审 Blocker B1 已修**：聊天气泡携带裁切结果路径时，节点解析（`FindNodeByImagePath` /
  `GetParentPipelineImagePath`）扩展为同时匹配 `ImagePath` 与 `Crop.ResultImagePath`。
- **S1/S2 已修**：pending 气泡按身份替换（防生成中重建越界）；裁切激活时 `LoadNode` 载原图。
- 修订后：`dotnet build` 0/0；非 GPU 全量 **228 通过 / 0 失败**。
- 9C.6B.10 交互改为「整框移动 + 8 手柄缩放」（上表 9C.6B.10 行已同步）。
