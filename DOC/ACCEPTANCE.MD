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
