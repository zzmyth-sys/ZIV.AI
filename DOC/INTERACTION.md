# ZIV.AI 对话式交互设计（INTERACTION）

- 文档状态：**设计已裁决，实现待后续 Step**（非冻结草案）
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
- **新增（Agent 层）**：`CommandParser` / `EditSession` / `EditNode`。
  （命令解析与会话 DAG 是编排逻辑，归 Agent，符合 `ARCHITECTURE.md` §2 / §3。）
- **新增（App 层）**：`SessionExporter`（会话导出属平台 / 文件 IO，归 App）。
- **新增（UI 层，Step 9+）**：聊天流 + 历史节点列表。
- 依赖方向不变：`App → UI → Agent/Tools/Backend → Contracts`；Agent 只依赖 `Contracts`。

## 6. 不在本期范围

- LLM 意图理解（Skill 系统）
- 多轮上下文保持（Context Folding）
- 会话持久化（SQLite）
- 自动恢复
- 经典 img2img（见 `OPTIMIZATION.md` §2.1.2）

## 7. 待确认项

- `commands.json` 的具体命令集。
- 历史节点的 UI 展示形式（列表 / 时间轴 / 缩略图）。
- 保存目录的默认值。
