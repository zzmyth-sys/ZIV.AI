# ZIV.AI 对话式交互设计（INTERACTION）

- 文档状态：**逻辑层 + UI 已实现（Step 8 + Step 9A）**（非冻结草案）
- 用途：记录对话式交互模型与关键决策，避免遗忘
- 关联：[`ARCHITECTURE.md`](ARCHITECTURE.md)（分层）· [`SPEC.md`](SPEC.md)（Planner / Executor / Tool）·
  [`FROZEN.md`](FROZEN.md)（Z17–Z30）· [`OPTIMIZATION.md`](OPTIMIZATION.md)（文档风格参考）

> 本文件只记录**已裁决的设计决策**，不展开实现细节；实现拆到后续 Step（见 §5 / §6）。
> 不改 `ARCHITECTURE.md` / `SPEC.md` / `FROZEN.md` 等已冻结文档。

## 1. 目标

- 插件采用**聊天式交互**，**不使用模板按钮面板**（取代 `SPEC.md` 的模板面板形态）。
- 支持**斜杠命令**（`/命令 参数`）映射到预定义编辑操作。
- 支持**从历史节点回溯修改**（「从上一步 / 上几步修改」）。
- **不走 LLM 意图理解**，命令解析**硬编码**、确定性、可离线。

## 2. 命令层

- **定义文件**：`Template/commands.json`（硬编码解析；该目录/文件为后续实现新建）。
- **结构示例**：

```json
{
  "commands": [
    {
      "name": "/头发换颜色",
      "params": ["color"],
      "template": "将头发颜色改为{color}，保持发型、面部特征、背景不变"
    },
    {
      "name": "/移除背景",
      "params": [],
      "template": "移除背景，保留主体完整边缘"
    }
  ]
}
```

- **解析规则**：
  - 输入以 `/` 开头 → 匹配命令名 → 参数替换模板 → 构造**单步 `EditPlan`**。
  - 输入不以 `/` 开头 → 原文作为 `prompt` → 构造**单步 `EditPlan`**。
- **不调用 Planner**（`IPlanner`）：可绕过，或内联 `FallbackPlanner` 的等价逻辑（单步、`QW21edit`）。
- 工具名沿用 Step 6 冻结的 `QW21edit`（见 `FROZEN.md` 6E.6）。

## 3. 会话层（EditSession）

- **内存 DAG 结构，不持久化**（进程内；关闭即丢，见 §4）。
- **节点字段**（`EditNode`）：`NodeId` / `ParentNodeId` / `ImagePath` / `Command` / `CreatedAt`。
- **当前工作节点**：`CurrentNodeId`。
- **操作**：
  - 发送命令：从 `CurrentNodeId` 出发，生成新节点，挂在该节点下。
  - 点击历史节点：`CurrentNodeId` 切换，后续编辑从该节点**分支**（不覆盖旧分支）。
- 每个节点对应一次已执行的编辑输出图（新文件，Z24）。

## 4. 关闭行为

- 插件关闭时弹窗：**「保存本次会话？」**
  - **是** → 导出 `session.json` + 图片文件到**用户选定目录**。
  - **否** → 直接丢弃，**不报错**。
- **不自动恢复**；用户若需恢复，可手动导入 JSON（**若后续需要再加**，非本期范围）。

## 5. 与现有架构的关系

- **不变**：`Contracts` / `Executor` / `Tool` / `Backend` / Python 管线；`IPlanner` / `IExecutor` /
  `IEditTool` / `IToolRegistry` 契约不改。
- **新增（Agent 层）**：`CommandParser` / `EditSession` / `EditNode` / `ISessionExporter` /
  `SessionExporter`。
  （命令解析、会话 DAG 与导出是编排 / 会话逻辑，归 Agent，符合 `ARCHITECTURE.md` §2 / §3；
  `SessionExporter` 操作 `EditSession` 且零平台依赖，故与 `EditSession` 同层，见
  `FROZEN.md`「修订说明（Step 8 归属修正）」8R.1。）
- **新增（UI 层，Step 9+）**：聊天流 + 历史节点列表。
- 依赖方向不变：`App → UI → Agent/Tools/Backend → Contracts`；Agent 只依赖 `Contracts`。

> **实现状态（Step 8）**：逻辑层已落地——`CommandParser` / `EditSession` / `EditNode`（Agent）、
> `ISessionExporter` / `SessionExporter`（Agent，见 8R.1 归属修正）、`Template/commands.json`、
> `AppContext` 装配。
> 契约与冻结记录见 `FROZEN.md` Step 8。`/扩图` 由 `width`/`height` 参数翻译为
> `ResolutionPolicy{Explicit}`（`QW21outpaint` 要求）。UI、LLM 意图理解、`@图片N` 仍后置。

> **实现状态（Step 9A）**：UI 层已落地——`SessionViewModel`（聊天流 + 历史节点列表）与
> `LaunchOptions`（CLI 参数解析）在 `ZivAiEditor.UI`；`SingleInstance`（Mutex + Named Pipe）、
> 自绘 chrome 主窗口（`MainWindow`）、关闭询问对话框（`ConfirmDialog`）在 `ZivAiEditor.App`；
> `Themes/ZivColors.axaml` 配色与 ZIV 一致。UI **只经契约访问 Agent 层**
> （`ICommandParser` / `IExecutor` / `ISessionExporter` / `EditSession`），不直接调
> `IpcInferenceClient`。契约与冻结记录见 `FROZEN.md` Step 9A。URL 协议、LLM 意图理解、
> `@图片N` 仍后置（与 ZIV 侧实际联调为 Step 9B）。
>
> **收尾修正（2026-09-22）**：聊天流「生成中…」气泡已接通**渐进预览**——App 层订阅
> `IpcInferenceClient.PreviewReceived`（`0x02` JPEG 帧）→ 以 `byte[]` 转给 UI 更新气泡内
> `Image`；状态栏在解析失败 / 完成后复位为「就绪」。详见 `FROZEN.md` 9A.8。

## 6. 不在本期范围

- LLM 意图理解（Skill 系统）
- 多轮上下文保持（Context Folding）
- 会话持久化（SQLite）
- 自动恢复
- 经典 img2img（见 `OPTIMIZATION.md` §2.1.2）

## 7. 待确认项

- ~~`commands.json` 的具体命令集。~~ **已定（Step 8）**：初始 4 条（`/换背景` `/去水印`
  `/去物体` `/扩图`），见 `Template/commands.json` 与 `FROZEN.md` 8.2；后续可增补。
- ~~历史节点的 UI 展示形式（列表 / 时间轴 / 缩略图）~~ **已定（Step 9A）**：左栏**列表** +
  按 `ParentNodeId` **树形缩进**；点击节点切换当前上下文。
- ~~保存目录的默认值~~ **已定（Step 9A）**：关闭时弹窗询问，由用户经系统文件夹选择器指定。
