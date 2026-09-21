# ZIV.AI IPC 传输契约（IPC Protocol）

- 文档状态：**Step 2 冻结**（2026-09-21）；**修订至 ipc_version 0.4**（Step 3 修订后）
- **ipc_version：`0.4`**
- 用途：定义 C# 前端（`ZivAiEditor.App` / `ZivAiEditor.Backend`）与 Python 推理进程之间的
  **跨进程传输契约**。取代 Step 1 的「OpenAPI 作为跨进程唯一契约」定位（见 `FROZEN.md` Step 2）。
- 依据：`_test_step2/REPORT.md`（11 项实测）。
- 关联：`contracts/openapi.yaml`（**Schema 参考**，本协议 payload 沿用其结构）；
  `FROZEN.md` Step 2；`SPEC.md` Z17 / Z23。

> **修订记录（ipc_version 0.1 → 0.2，2026-09-21 · Step 2.1 实测后）**
>
> - **管道方向**：`Python = server / C# = client` → **`C# = Server / Python = Client`**。
> - **消息类型收敛**：删除 `health` / `health_result`，改为 **`ping` / `pong`**。
> - **`preview`**：明确为 **`0x02` 二进制帧**，承载 **JPEG** 字节流（来源
>   `latent_preview.get_previewer()`）。
> - 依据：`_test_step2/REPORT.md` 任务 7（Named Pipe 性能）与任务 10（ACL）。

> **修订记录（ipc_version 0.2 → 0.3，2026-09-21 · Step 2.2 修订后）**
>
> - **`progress` 扩展**：新增可选字段 `stage` / `sub_stage`（用于观测惰性加载阶段），
>   并新增 §3.3「加载阶段进度序列」。属**向后兼容的协议扩展**（新字段可选，旧端忽略）。
> - **`pong.vram_used_mb` 口径明确**为 **NVML 当前 GPU 占用**（原写
>   `torch.cuda.memory_allocated()`）；并说明 ComfyUI **延迟加载权重**——
>   模型 load 后未推理前该值接近基线属正常。
> - **清理**：`health` / `health_result` 的兼容分支已从 Python / C# 实现中移除
>   （契约 0.2 已删该消息类型）。
> - 依据：Step 2.2 复测——`load_models_gpu()` 前 `torch.cuda.memory_allocated() == 0`、
>   参数 device 为 `cpu`；调用后约 **6920 MB**、device 为 `cuda:0`。

> **修订记录（ipc_version 0.3 → 0.4，2026-09-21 · Step 3 修订后）**
>
> - **新增 `heartbeat` 消息（Python → C#）**：周期心跳，载荷
>   `vram_used_mb`（NVML 当前占用）与 `current_task_id`（无任务时为 `null`）。
>   C# 侧连续未收到（默认 > 30 s，即 3 次）即判定后端失联并触发 `HeartbeatLost` 事件
>   （自动重启留待 Step 4）。属**向后兼容的协议扩展**（旧端忽略未知消息）。
> - **`pong` 不变**：`ping` / `pong` 仍为按需健康探测；`heartbeat` 为其周期性补充。
> - 依据：Step 3 重定义（原 ACCEPTANCE 的「SGLang 服务」随 D-7 废弃）——
>   本步实现**空闲卸载（Z21）+ heartbeat**；详见 `FROZEN.md` Step 3 段。

---

## 1. 传输层

| 项 | 约定 |
|---|---|
| 通道 | Windows **Named Pipe**（`PipeOptions.Asynchronous`，`PipeTransmissionMode.Byte`） |
| 方向 | **C# = Server**：`PythonProcessManager` 先创建 `NamedPipeServerStream`（含 ACL），**再拉起 Python 进程**；**Python = Client**：启动后主动连接。**启动时序无竞态**（管道先于进程存在，`WaitForConnection` 等待即可）。 |
| 管道名 | `\\.\pipe\zivai.infer.v1`（版本化；通道版本升级时改名） |
| ACL | **仅当前用户**（`PipeSecurity` + `SetAccessRuleProtection(true, false)` + `NamedPipeServerStreamAcl.Create`）。**禁止**使用默认 ACL（实测默认允许 `Everyone` / `Anonymous` 读，见 §8）。 |
| 并发 | 单任务**串行**（Z18）：同一时刻只处理一个 `submit`；后续 `submit` 排队或拒绝。 |
| 缓冲 | 建议 1 MB（实测 1MB 缓冲下 7.91MB 帧单程 2.71ms）。 |

> 实测吞吐（`_test_step2/REPORT.md` 任务 7）：576 KB **0.396 ms**（1420 MB/s）/
> 2.25 MB **0.824 ms**（2731 MB/s）/ 7.91 MB **2.711 ms**（2918 MB/s）。

## 2. 帧格式

所有帧统一为长度前缀：

```
┌──────────────┬──────────┬─────────────────────────┐
│ 4 字节        │ 1 字节    │ N 字节                   │
│ 小端长度 L     │ 帧类型    │ 载荷（长度 = L - 1）      │
└──────────────┴──────────┴─────────────────────────┘
```

- `L` = `1 + payload_length`，小端 `int32`。
- 帧类型：
  - `0x01` = **JSON 控制帧**（载荷为 UTF-8 JSON）
  - `0x02` = **preview 二进制帧**（**仅用于 `preview` 消息**，载荷为 **JPEG** 字节流；
    结构见 §3.5）。**不预留其他用途**。
- 读取端必须先读满 4 字节长度，再按长度读满整帧（严禁按“一次 read 拿全部”假设）。

## 3. 消息（JSON 控制帧）

### 3.1 C# → Python（请求）

| `type` | 字段 | 说明 |
|---|---|---|
| `ping` | `request_id` | 探测后端与模型加载状态（IPC 惯例；取代原 `health`） |
| `submit` | `request_id`, `task_id`, `op`, `payload` | 提交一次推理任务；`payload` 见 §3.4 |
| `cancel` | `task_id` | 取消指定任务 |
| `shutdown` | — | 请求后端进程优雅退出（由应用退出时发送） |

### 3.2 Python → C#（响应 / 事件）

| `type` | 字段 | 说明 |
|---|---|---|
| `pong` | `request_id`, `model_status`, `vram_used_mb`, `idle_unload_seconds` | **等价于健康检查响应**（取代原 `health_result`）：`model_status` 为各模型加载状态（沿用 `HealthStatus.models` 结构）；`vram_used_mb` 为**当前时刻 GPU 实际占用**，**NVML 口径**（`nvmlDeviceGetMemoryInfo`，当前经 ctypes 调用 `nvml.dll`，等价于 `pynvml.nvmlDeviceGetMemoryInfo`）。**注意：ComfyUI 延迟加载权重**（load 后权重在 CPU，首次推理才上 GPU），**模型 load 后未推理前此值接近基线属正常**；与 `nvidia-smi` 存在 WDDM 计账差异，如需对齐由 C# 侧单独查询；`idle_unload_seconds` 为空闲卸载超时（Z21）。 |
| `accepted` | `task_id` | 已入队 / 开始（对应 `TaskAccepted`） |
| `progress` | `task_id`, `step`, `total`, `fraction`, `message?`, `stage?`, `sub_stage?` | 进度事件（`fraction` = 0..1）。`stage`：`loading_model` \| `sampling` \| `vae_decode`；`sub_stage` 仅在 `stage="loading_model"` 时有效（`dit` \| `te` \| `vae`），`stage="sampling"` 时为 `ready` 或 `null`。加载阶段序列见 §3.3。 |
| `preview` | `task_id`, `step`, `total` | **后紧跟一个 `0x02` 二进制帧**（JPEG 预览小图，结构见 §3.5） |
| `result` | `task_id`, `output_path` | 成功；输出新文件路径（Z24） |
| `canceled` | `task_id` | 已取消 |
| `error` | `task_id`, `code`, `message` | 失败 |
| `heartbeat` | `vram_used_mb`, `current_task_id` | **周期性心跳**（Step 3；每 `HEARTBEAT_INTERVAL_S` 秒，默认 10 s）。`vram_used_mb` 为 NVML 当前 GPU 占用；`current_task_id` 为在飞任务 id，空闲时为 `null`。C# 侧若超过 `HeartbeatLostAfterMs`（默认 30 s）未收到，判定后端失联并触发 `HeartbeatLost`（重启留待 Step 4）。 |

### 3.3 加载阶段进度序列（lazy load）

首个 `submit` 触发**惰性加载**时，Python 按以下顺序推送 `progress`（Step 2.2 实测）：

| # | `stage` | `sub_stage` | `fraction` | 说明 |
|---|---|---|---|---|
| 1 | `loading_model` | `dit` | `0.0` | DiT 开始加载 |
| 2 | `loading_model` | `dit` | `0.33` | DiT 加载完成 |
| 3 | `loading_model` | `te` | `0.33` | 文本编码器开始加载 |
| 4 | `loading_model` | `te` | `0.66` | 文本编码器加载完成 |
| 5 | `loading_model` | `vae` | `0.66` | VAE 开始加载 |
| 6 | `loading_model` | `vae` | `1.0` | VAE 加载完成 |
| 7 | `sampling` | `ready` | `1.0` | 模型三件套就绪，进入采样阶段（Step 2.3 接管） |

- 加载阶段 `step` / `total` 均为 `0`（加载不是采样步）；`fraction` 单调不减。
- `stage="sampling"` + `sub_stage="ready"` 是「模型已加载」的显式信号；随后 `ping` 返回
  `model_status="loaded"`。
- 模型已加载时的后续 `submit` **不**再推送 `loading_model` 帧。
- `stage="vae_decode"` 预留给后续版本（VAE 解码阶段），当前实现未产生。
- 实测：冷加载约 **8.4 s**（单进程；其中首次 `import comfy` 约 7.5 s），
  `dit` / `te` / `vae` 纯加载约 **0.05 / 0.6 / 0.3 s**。

### 3.4 `submit.payload`（沿用 `openapi.yaml` 的 `ImageEditRequest`）

```json
{
  "image_path": "C:\\path\\main.png",
  "mask_path": null,
  "prompt": "make the background a snowy mountain",
  "steps": 20,
  "seed": -1,
  "denoise": 1.0,
  "output_path": null
}
```

- `op`（消息顶层）取值：`inpaint` / `img2img` / `upscale` / `segment` / `outpaint`
  （对应 `openapi.yaml` 的端点；`openapi.yaml` 现为 Schema 参考）。
- `mask_path` 为二值 PNG（只含 0 / 255，Z19）；缺省表示整图。
- 模型路径由**后端配置**提供（绝对路径直传），**不**在 payload 内（见 §6）。

### 3.5 `preview` 二进制帧（`0x02`）

- **用途**：仅用于 `preview` 消息的缩略图（**JPEG** 字节流），紧跟在 `preview` 控制帧之后。
- **载荷结构**（不含外层 4B 长度与 1B 类型，见 §2）：

```
┌──────────────────┬──────────────┬──────────────────┬────────────┐
│ 4B task_id 长度 L1 │ task_id (L1) │ 4B JPEG 长度 L2   │ JPEG (L2)  │
└──────────────────┴──────────────┴──────────────────┴────────────┘
```

  - 所有整数为**小端 int32**。
  - `task_id` 为 UTF-8 字节；`JPEG` 为 JPEG 编码字节流。
- **来源**：Python 侧调用 `latent_preview.get_previewer(device, latent_format)` 获取预览器，
  在采样回调 `callback(step, x0, x, total)` 中调用
  `previewer.decode_latent_to_preview_image("JPEG", x0)` 生成 JPEG 字节。
- **发送频率**：**每步发送**；可通过配置**降频**（默认每步）。
- **尺寸**：与 latent 分辨率一致（天然低分辨率）。
- **不预留其他 `0x02` 用途。**

## 4. 时序

```
C#(server)                                              Python(client)
   │  NamedPipeServerStream.Create + PipeSecurity（仅当前用户）
   │  WaitForConnection（阻塞等待）                        │
   │  Process.Start(python.exe ...) ────────────────────►│  启动
   │  ◄────────────────────────── connect \\.\pipe\...    │  主动连接
   │  {type:ping, request_id} ──────────────────────────►│
   │  ◄──────────────── {type:pong, model_status, ...}    │
   │  {type:submit, op:"inpaint", payload:{...}} ───────►│
   │  ◄──────────────── {type:accepted, task_id}          │
   │  ◄──────────────── {type:progress, step:1/20}        │
   │  ◄─ {type:preview} + [0x02: task_id + JPEG]          │  (每步，可降频)
   │  ◄──────────────── {type:progress, step:20/20}       │
   │  ◄──────────────── {type:result, output_path}        │
   │  {type:shutdown} ──────────────────────────────────►│  (应用退出)
```

- 进度来自 ComfyUI 采样 `callback(step, x0, x, total)`（实测每步触发）。
- 取消：C# 发 `{type:cancel}`；Python 调 `comfy.model_management.interrupt_current_processing()`，
  采样循环抛出 `InterruptProcessingException`，Python **必须**以
  `except comfy.model_management.InterruptProcessingException` 或 `except BaseException` 捕获
  （实测该异常继承 **`BaseException`**），随后回 `{type:canceled}`。

## 5. 安全

- 管道 **仅当前用户**（§1）。禁止默认 ACL（实测默认允许 `Everyone` / `Anonymous` 读）。
- 仅本机（Named Pipe 本身即本机）；不开网络端口。
- 不在日志中输出图像内容或用户 prompt 全文。

## 6. 模型与后端配置（非契约，供实现参考）

- 后端 = ComfyUI v0.37.0 便携版（`D:\devlop\ZIV.AI\Comfyui`），in-process `import comfy`。
- 模型三件套以**绝对路径**直传（只读引用 `C:\AI\ComfyUI_PIC`，不拷贝、不修改）：
  DiT `...\image2\qwen_image_2.1_int8_convrot.safetensors`；
  TE `...\text_encoders\qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors`；
  VAE `...\vae\qwen_image_2.1_vae_bf16.safetensors`。
- **无需** `extra_model_paths.yaml`。

## 7. 版本与兼容

- **`ipc_version`：`0.4`**（管道名 `\\.\pipe\zivai.infer.v1` 为**通道版本**，与协议版本独立）。
  `ipc_version 0.1` 为**追溯设定**（原文档无版本字段）。
- **0.1 → 0.2 变更点**：
  1. **管道方向**：`Python=server / C#=client` → `C#=Server / Python=Client`（C# 掌控 Python 生命周期，启动无竞态）。
  2. **消息类型收敛**：删除 `health` / `health_result`，改为 `ping` / `pong`（`pong` 承载 `model_status` / `vram_used_mb` / `idle_unload_seconds`）。
  3. **新增 `preview` 二进制帧**：`0x02` 明确为 preview 专用 JPEG 帧（§3.5）。
- **0.2 → 0.3 变更点**（Step 2.2）：
  1. **`progress` 新增可选字段** `stage` / `sub_stage`（加载阶段可观测）；新增
     §3.3「加载阶段进度序列」。**向后兼容**：新字段可选，旧端忽略即可。
  2. **`pong.vram_used_mb` 口径明确**为 **NVML 当前 GPU 占用**，并记录 ComfyUI
     **延迟加载权重**的事实（load 后未推理前接近基线属正常）。
  3. **实现清理**：`health` / `health_result` 兼容分支从 Python / C# 代码中移除。
- **0.3 → 0.4 变更点**（Step 3）：
  1. **新增 `heartbeat` 消息**（Python → C#，§3.2）：周期上报 `vram_used_mb` /
     `current_task_id`；C# 侧超时未收触发 `HeartbeatLost`。**向后兼容**（旧端忽略）。
- 协议变更时升 `ipc_version`（必要时同时升管道名 `v2`），旧前端可并存。
- `openapi.yaml` 保留为 **Schema 参考**（`ImageEditRequest` / `TaskAccepted` /
  `TaskStatusResponse` 等结构即本协议 payload 的形状来源）。

## 8. 实测依据

| 项 | 实测数值 / 结论 | 来源（`_test_step2/REPORT.md`） |
|---|---|---|
| Named Pipe 延迟/吞吐 | 576 KB **0.396 ms / 1420 MB/s**；2.25 MB **0.824 ms / 2731 MB/s**；7.91 MB **2.711 ms / 2918 MB/s** | 任务 7 |
| 管道方向（C# 创建 + Python 连接） | C# `NamedPipeServerStream` 先创建、Python 后连接，收发正常、无死锁 | 任务 7 |
| 默认 ACL | 允许 `Everyone` / `ANONYMOUS LOGON` 读（`Read, Synchronize`）——**不安全** | 任务 10 |
| 收紧 ACL | `PipeSecurity` + `SetAccessRuleProtection(true,false)` + `NamedPipeServerStreamAcl.Create` → 仅 `DESKTOP-0FOT5TD\31655 | Allow | FullControl` | 任务 10 |
| 取消异常 | `InterruptProcessingException` 继承 `BaseException`（非 `Exception`） | 任务 6 |
| 进度回调 | `callback(step, x0, x, total)` 每步触发 | 任务 6 |
| 显存 | 无泄漏；进程退出回基线（~813 MiB） | 任务 9 |
| 延迟加载（Step 2.2 复测） | `load_models_gpu()` 前 `torch.cuda.memory_allocated() == 0`、DiT 参数 device = `cpu`；调用后 alloc ≈ **6920 MB**、device = `cuda:0` | Step 2.2 复测 |

> **遗留**：`preview` 二进制帧（`0x02`）的**端到端收发**尚未单独实测（`latent_preview` 预览器
> 与 `decode_latent_to_preview_image` 路径待 Step 2 实现时补测）；本契约按实测到的 `callback`
> 能力与 ComfyUI 预览器接口先行冻结设计。
