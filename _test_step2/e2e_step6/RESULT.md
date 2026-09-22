# Step 6 GPU 端到端验证结果（e2e_step6）

- 日期：2026-09-22
- 目的：验证 `Executor → QW21edit → IpcInferenceClient → Python pipeline` 真实链路可用性
- 输入：`_test_step2/user_input_1024.png`（1024×640）
- 命令式输入（手工构造单步 `EditPlan`，不经 Planner）：
  - `ToolName = "QW21edit"`
  - `prompt = "把背景替换为古代中式茶肆，保留人物与前景"`
  - `steps = 25`、`seed = 42`、`denoise = 1.0`
  - `Mask = null`（无 mask → 参考条件编辑路径）
- 结论：**通过**。链路完整出图，`TaskState.Succeeded`，原图未变，无残留进程。

## 1. GPU 前后对比（Z30）

| 时点 | 显存 | 说明 |
|---|---|---|
| 测试前（shell） | **1254 MiB** | 基线 ~900；唯一 python 计算进程为 `windows-mcp`（非推理），用户已确认继续 |
| 测试前（程序内） | 1446 MiB | 采样器首次读数 |
| **峰值（程序采样，500ms）** | **13792 MiB** | 采样期间 |
| 测试后（shell） | **1438 MiB** | 回落 |
| 测试后（程序） | 1350 MiB | 回落 |

- 无残留 ComfyUI / Python 推理进程（仅 `windows-mcp` 两个常驻进程仍在）。

## 2. 耗时分解（程序内相对时钟）

| 阶段 | 时间窗 | 耗时 |
|---|---|---|
| 进程启动 + `import comfy` + 提交/接受 | 0.00 → 10.51s | **~10.5 s** |
| 惰性模型加载（dit / te / vae） | 10.51 → 11.45s | **~0.94 s**（dit 0.31 / te 0.40 / vae 0.23） |
| `moving_to_gpu`（首次 `load_models_gpu`） | 11.45 → 18.46s | **~7.0 s** |
| encode → 首个采样步 | 18.46 → 20.89s | **~2.4 s** |
| 采样（25 步） | 20.89 → 32.53s | **~11.6 s**（~0.46 s/步） |
| VAE decode + 保存 | 32.53 → 33.92s | **~1.4 s** |
| **总计** | | **35.20 s** |

- 首次惰性加载（含 `import comfy`）合计约 **11.4 s**（0→11.45s），与 Step 2.2/3 记录一致。
- 进度单调递增，`loading_model:dit/te/vae` → `model_loaded` → `moving_to_gpu` → `sampling` → `vae_decode` → `succeeded`，与预期一致。

## 3. 输出路径与验证

- `TaskState.Status` = **Succeeded**
- `TaskId` = `7042de25947e49879f6d1e9ba9e560fb`
- `StepState`：`ff225bcde16f412eb9b888fd5494c9e2` / Succeeded / dur=33.90s
- **输出文件**：`D:\devlop\ZIV.AI\_test_step2\ff225bcde16f412eb9b888fd5494c9e2.png`
- 尺寸 **1536×960**（side 口径长边 1536，源 1024×640 16:10），1,769,268 字节
- 输出 SHA-256：`C223721F80D97B4774E7A3873145918C9F39601FD7435D4966DFD7DBCD7AEEE1`

### output_path 尊重性（关键）

- `ToolResult.OutputImagePath` = `_test_step2\ff225...png`；`File.Exists` = **True**；
  **后端实际写入路径 = Executor 传给 Tool 的 output_path** → **一致（PASS）**。
- 旁证：源目录**无** `user_input_1024_ai_*.png` 之类的后端默认文件（`stray _ai_ files: (none)`）。
- **重要发现（非缺陷，属设计现状）**：任务预期的 `_test_step2\e2e_step6\work\` **未被使用**（work 目录为空）。
  原因：`EditPlan` **没有** `WorkingDirectory` 字段；`Executor` 用 `ResolveWorkingDirectory(plan.MainImagePath)`
  自行把 `ToolInput.WorkingDirectory` 设为主图所在目录。因此中间/输出文件落在**主图同目录**，
  `QwenImage21EditTool` 据此派生 `{主图目录}/{StepId}.png`（StepId 为 Guid，唯一，不覆盖源图，Z24 成立）。
  → 调用方**无法**通过 `EditPlan` 指定中间结果目录；如需隔离到工作目录，需后续为 `Executor`/`EditPlan`
  增加工作目录入口（契约议题，非本步范围）。

## 4. 原图哈希前后对比（Z24）

| | SHA-256 |
|---|---|
| 前 | `88A9B5F9E217E00D9EC8C333277D89095BA91EED1798FEAA4751300FA5E14AC7` |
| 后 | `88A9B5F9E217E00D9EC8C333277D89095BA91EED1798FEAA4751300FA5E14AC7` |
| 结果 | **一致**（原图未被修改） |

## 5. 画面质量（客观指标 + 主观）

按 `ZivAiEditor.Tests/ImageQuality` 口径（8×8 块均值方差占比 + lag-1 自相关）：

| 图 | var | blockRatio（≥0.80） | lag1（≥0.90） | 结论 |
|---|---|---|---|---|
| 源图 1024×640 | 0.0842 | 0.894 | 0.986 | 正常 |
| 输出 1536×960 | 0.0293 | **0.914** | **0.991** | **非噪声，结构有效** |

- **主观评价**：本验证环境无法直接目视图片，故以结构指标为准。输出 `blockRatio=0.914 / lag1=0.991`
  明显高于噪声阈值，属**结构完整的正常图像**；方差 0.029 低于源图 0.084，符合「背景被整体重绘为
  更平滑的室内场景、前景保留」的预期方向。**建议人工目视复核一次**以确认「茶肆背景 + 人物/前景保留」
  的语义是否符合 prompt。

## 6. 遇到的问题

1. **`SettingsLoader` 无法复用**：它是 `ZivAiEditor.App` 的 `internal` 类型，验证程序不引用 App
   （WinExe + Avalonia）。改为在验证程序内读取仓库根 `settings.ini` 的 `[backend]` 段，缺省回退
   `PythonBackendOptions` 默认值（值与 settings.ini 一致）。
2. **`EditPlan` 无 `WorkingDirectory`**：任务要求「WorkingDirectory = e2e_step6\work\」，但冻结的
   `EditPlan` 没有该字段；`Executor` 从主图目录派生，故输出落在 `_test_step2\`（见 §3 发现）。
   未改任何源码，仅记录。
3. 无 OOM、无异常、无重试。

## 7. 验证程序文件清单（均为新增临时文件，未改源码）

```
_test_step2/e2e_step6/
  e2e_step6.csproj        # net8.0-windows Exe，引用 Contracts/Agent/Tools/Backend
  Program.cs              # 链路：PythonProcessManager → IpcInferenceClient → ToolRegistry
                          #      → QwenImage21EditTool → ExecutionQueue → Executor
  quality_check.py        # CPU 只读画质指标（numpy/PIL）
  work/                   # （空；未被 Executor 使用，见 §3）
  RESULT.md               # 本文件
  bin/ obj/               # 构建产物
```

## 8. 遗留项

- `Executor` 不支持调用方指定中间结果目录（从主图目录派生）；如需 `work/` 隔离，后续 Step 评估。
- 未做多步真实链路（本次单步）；多步真实链路（步骤 2 输入 = 步骤 1 输出）仍由 mock 覆盖。
- 未做取消（`CancelAsync` 透传后端）的真实 GPU 验证。
- 画面语义（茶肆背景 / 人物保留）建议人工目视复核。
- 经典 img2img 未实现（见 `OPTIMIZATION.md` §2.1.2）。
