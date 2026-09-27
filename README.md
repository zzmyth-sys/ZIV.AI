# ZIV.AI

> **三图槽位（主图 / 参考图 / 遮罩图）+ 一句自然语言 → 自动规划编辑步骤并执行。**
>
> 本地优先的 AI 图像编辑桌面应用（Windows），同时可作为 [ZIV](../ZIV) 主程序的进程外插件被调用。

核心原则：**全本地、不联网、不破坏原图、推理进程隔离、模型空闲卸载。**

---

## 特性

- **三图 + 一 prompt**：主图（缺省且有 prompt 时转文生图 T2I）/ 参考图 / 遮罩图 + 一句自然语言指令。
- **对话式编辑**：聊天流 + 历史节点树（内存 DAG，可分支，不覆盖旧结果），关闭时可导出 `session.json`。
- **命令模板**：内置 `/换背景` `/换装` `/合照` `/生成` `/去水印` `/扩图` `/全景` 等（`Template/commands.json`）。
- **手绘遮罩**：画笔 / 橡皮 / 撤销，导出严格二值 PNG（只含 0 / 255）。
- **多步执行**：Planner 产出有序步骤，Executor 串行执行，支持进度、取消、重跑。
- **分辨率策略**：可指定输出分辨率档位（Fast / Balanced / HighQuality）与固定宽高。
- **本地推理**：Python 后端 + ComfyUI in-process 管线，C# 侧只经 IPC 访问，不加载 Python 运行时。

## 架构概览

C# 前端（Avalonia）+ Python 推理后端（独立进程），两者只经 **Named Pipe（长度前缀 IPC）** 通信。

```
 L6  ZivAiEditor.App          Avalonia 壳、装配、单实例、CLI / URL 协议 / 本地 HTTP 入口
 L5  ZivAiEditor.UI           视图模型 / 交互规则（无 Avalonia；视图在 App）
 L4  ZivAiEditor.Agent        Planner / Executor（步级编排）
     ZivAiEditor.Tools        IEditTool 实现 + ToolRegistry
     ZivAiEditor.Backend      IpcInferenceClient（IPC）+ Python 进程管理
     ZivAiEditor.Imaging      IImagingService（crop / mask 导出 / 羽化）
 L3  ZivAiEditor.Contracts    契约与模型（不依赖 Avalonia / 平台）
 L2  ZIV.Core / ZIV.Imaging   共享库（项目引用，不复制）
 ─────────────────────────── 进程边界 ───────────────────────────
     Python 推理后端          ComfyUI v0.37.0 in-process 管线
```

依赖方向严格单向：`App → UI → Agent/Tools/Backend → Contracts → ZIV.Core/Imaging`。

## 目录结构

```
ZIV.AI/
├─ src/                     # .NET 解决方案（ZIV.AI.sln，8 个 ZivAiEditor.* 项目）
│  ├─ ZivAiEditor.App/          # WinExe，Avalonia 壳、装配、CLI / URL / HTTP 入口
│  ├─ ZivAiEditor.UI/           # 视图模型 / 规则（无 Avalonia）
│  ├─ ZivAiEditor.Agent/        # Planner / Executor
│  ├─ ZivAiEditor.Tools/        # 编辑工具 + 注册表
│  ├─ ZivAiEditor.Backend/      # IPC 推理客户端 + Python 进程管理
│  ├─ ZivAiEditor.Imaging/      # 图像栅格原语
│  ├─ ZivAiEditor.Contracts/    # 契约与模型
│  └─ ZivAiEditor.Tests/        # xunit
├─ python/server/           # Python 推理后端（ComfyUI 管线、tagger、IPC、空闲卸载）
├─ Comfyui/                 # ComfyUI v0.37.0 便携版 + python_embeded
├─ contracts/               # ipc-protocol.md（跨进程契约）；openapi.yaml（Schema 参考）
├─ Template/                # commands.json / models.json / loras.json（命令与模型模板）
├─ DOC/                     # 规格、架构、冻结记录、验收、开发日志等文档
├─ output/                  # 批量 / 无主图场景的输出目录
├─ publish.ps1              # 便携版发布脚本
├─ settings.ini.template    # 配置模板（实际 settings.ini 不入库）
└─ global.json              # 固定 SDK 10.0.401
```

## 构建与运行

**前置条件**

- Windows（兼容 Win10）
- .NET SDK `10.0.401`（由 `global.json` 固定）
- 本机 GPU（开发目标 RTX 4080 16GB）；Python 后端使用 `Comfyui/python_embeded/python.exe`
- 共享库 [ZIV](../ZIV) 与本仓库同级目录（`ZIV.Core` / `ZIV.Imaging` 走项目引用）

**构建**

```powershell
dotnet build src\ZIV.AI.sln -c Release
```

**测试**（按影响面执行，避免无谓的 GPU 负载）

```powershell
dotnet test src\ZIV.AI.sln
```

**运行**

```powershell
dotnet run --project src\ZivAiEditor.App
```

**发布（便携版）**

```powershell
pwsh -File publish.ps1
# 默认发布到 D:\Program Files\ZIV.AI，可用 ZIV_AI_PUBLISH_DIR 覆盖
```

**配置**：复制 `settings.ini.template` 为程序目录下的 `settings.ini`。配置包含后端管道名、Python 路径、Planner / 提示词重写 LLM 端点等，均随程序目录存放（便携，不进注册表）。

## 文档

| 文档 | 说明 |
|---|---|
| [`DOC/SPEC.md`](DOC/SPEC.md) | 产品规格与铁律 Z1–Z30（唯一验收基准） |
| [`DOC/ARCHITECTURE.md`](DOC/ARCHITECTURE.md) | 分层、依赖规则、契约、决策记录 D-1… |
| [`DOC/FROZEN.md`](DOC/FROZEN.md) | 已冻结接口与逐步修订记录 |
| [`DOC/ACCEPTANCE.MD`](DOC/ACCEPTANCE.MD) | 各 Step 验收清单与结果 |
| [`DOC/DEVLOG.md`](DOC/DEVLOG.md) | 开发日志 |
| [`DOC/INTERACTION.md`](DOC/INTERACTION.md) | 交互设计 |
| [`DOC/INTERFACES.md`](DOC/INTERFACES.md) | 对外接口 |
| [`DOC/OPTIMIZATION.md`](DOC/OPTIMIZATION.md) | 推理优化项与基线数据 |
| [`DOC/WD+模板系统.md`](DOC/WD+模板系统.md) | WD 标签 + 提示词模板实验记录 |
| [`contracts/ipc-protocol.md`](contracts/ipc-protocol.md) | 跨进程 IPC 传输契约 |
| [`contracts/openapi.yaml`](contracts/openapi.yaml) | HTTP Schema 参考（降级保留） |

## 铁律摘要

架构硬约束，任何实现 / 重构都不得突破。完整定义见 `DOC/SPEC.md` §6。

- **Z1–Z16**：继承自 ZIV（无静态中枢、单向依赖、契约纯净、平台隔离、资源释放、缓存有界、仅便携版……）。
- **Z17** 推理进程隔离 · **Z18** GPU 资源串行 · **Z19** 遮罩二值且不预填充
- **Z20** 任务状态持久化 · **Z21** 模型空闲卸载 · **Z22** Planner 可降级
- **Z23** AI 模块独立更新 · **Z24** 不破坏原图 · **Z25** 独立解决方案 · **Z26** 共享库不复制
- **Z27** 进程隔离不破 · **Z28** 独立运行不依赖 ZIV
- **Z29** 测试按影响面执行 · **Z30** GPU 任务先确认空闲（保护硬件资产）

## 状态

当前已落地到 **Step 9C.13**（UI 交互与预览增强）。逐步进展与新冻结契约见 `DOC/DEVLOG.md`、`DOC/FROZEN.md`。

## 前置依赖

ZIV.AI 复用 ZIV 项目的共享库（`ZIV.Core` / `ZIV.Imaging`）。
首次构建前，请先获取 ZIV 仓库并放在 ZIV.AI 的**同级目录**：

```
%REPO_ROOT%\
  ├─ ZIV\       ← ZIV 仓库
  └─ ZIV.AI\    ← 本项目
```

获取方式（二选一）：

- `git clone https://github.com/zzmyth-sys/ZIV.git` （ZIV 公开后）
- 或复制已有的 ZIV 目录到同级位置

然后 `dotnet build src\ZIV.AI.sln -c Release`

## 未来计划

ZIV 正式发布到 GitHub 后，本项目将切换为 git submodule 引用，
简化前置步骤。（当前为同级目录依赖）