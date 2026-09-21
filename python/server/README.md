# ZIV.AI Python 推理后端（Step 2.1）

最小 IPC 连通性验证：C# 作为 Named Pipe **服务端**创建管道并拉起本进程，本进程作为
**客户端**连接管道，完成 `ping` → `pong` 往返后，管道断开即退出。

- 依据契约：`contracts/ipc-protocol.md`（帧格式与消息类型）
- 关联：`DOC/FROZEN.md` Step 2、`DOC/SPEC.md` Z17 / Z23
- 本步范围：只做连通性；**不加载模型、不做推理**（Step 2.2 / 2.3）

## 文件

| 文件 | 说明 |
|---|---|
| `main.py` | 入口：解析参数 → 连接管道 → 主循环 → 断开后清理退出 |
| `ipc.py` | 长度前缀帧读写 + JSON 编解码（含 NaN/Infinity 清洗）+ 二进制帧预留 |
| `handlers.py` | `ping` → `pong`、`submit` → 惰性加载进度、`shutdown` → `shutdown_ack` |
| `config.py` | 模型绝对路径、管道名默认值、日志配置 |

## 依赖

**零第三方依赖**，仅用 Python 标准库（`json` / `struct` / `ctypes` / `subprocess` /
`argparse` / `logging` / `threading` / `gc`）。显存读取优先走 `nvml.dll`（ctypes），
失败时回退 `nvidia-smi`，均不安装 `pywin32`。

解释器使用 ComfyUI 便携版自带 Python：

```
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe
```

## 启动

正常由 C# `PythonProcessManager` 拉起，无需手动执行。手动调试：

```powershell
# 先由 C# 侧创建管道并监听，再运行：
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\python\server\main.py `
  --pipe-name "\\.\pipe\zivai.infer.v1" --log-level DEBUG
```

参数：

| 参数 | 默认值 | 说明 |
|---|---|---|
| `--pipe-name` | `\\.\pipe\zivai.infer.v1` | 完整管道路径，或短名（自动补前缀） |
| `--connect-timeout` | `15` | 连接管道的重试超时（秒） |
| `--log-level` | `INFO` | 日志级别（输出到 stderr） |
| `--log-file` | 无 | 可选日志文件 |

## 帧格式

```
[4 字节小端长度 L][1 字节帧类型][载荷，长度 = L - 1]
```

- `0x01` = JSON 控制帧
- `0x02` = 二进制帧（预留；Step 2.1 只忽略，不产生）

## 消息

| 方向 | `type` | 说明 |
|---|---|---|
| C# → Python | `ping` | 探测后端与模型加载状态 |
| C# → Python | `submit` | 提交任务；模型未加载时先惰性加载 |
| C# → Python | `shutdown` | 请求退出 |
| Python → C# | `pong` | 含 `status` / `version` / `model_status` / `vram_used_mb` / `models` |
| Python → C# | `accepted` / `progress` | 任务已接受；加载与采样进度（含 `stage` / `sub_stage`） |
| Python → C# | `shutdown_ack` | 确认退出 |
| Python → C# | `error` | 未知消息类型等 |

## 生命周期

管道断开（EOF / `BrokenPipeError` / `OSError`）→ 清理（`torch.cuda.empty_cache()` 若已加载 +
`gc.collect()`）→ 退出进程。退出码 0；连接/初始化失败为 1。
