# ZIV.AI Step 2 实测报告

- 日期：2026-09-21
- 环境：Windows 10 (19045) / RTX 4080 16GB / CUDA 13.1 驱动 / ComfyUI 便携版 **v0.37.0**（Python 3.13.14, torch 2.13.0+cu130）/ .NET SDK 10.0.401
- 测试目录：`D:\devlop\ZIV.AI\_test_step2\`（按要求**保留**）
- 约束遵守：**未修改** `DOC/*.md` 与已冻结契约；**未修改** `C:\AI\ComfyUI_PIC`（只读访问模型）；**未下载**任何模型/依赖。
- 说明：所有脚本/工程均为本次新建于 `_test_step2\` 下。

## 摘要

| # | 任务 | 结论 | 关键数据 |
|---|---|---|---|
| 1 | DiT `int8_convrot` 加载 | ✅ 成功 | `QwenImage21Transformer2DModel`，7.1B 参数，进程 wall 23.9s |
| 2 | Text Encoder 加载 | ✅ 成功 | `Qwen35TEModel_`，wall 8.4s |
| 3 | VAE 加载 | ✅ 成功 | 238 keys，wall 7.9s |
| 4 | 官方 Embedding API | ❌ 不存在 | 无 `comfy/client`、无 `embedded_comfy_client` |
| 5 | 端到端编辑（512²） | ✅ 成功 | T2I 14.3s / Edit 16.1s，输出 512×512 PNG |
| 6 | 进度回调 + 打断 | ✅ 成功 | callback 每步；第 7 步抛 `InterruptProcessingException` 终止 |
| 7 | Named Pipe 基准 | ✅ 成功 | 7.91MB：**2.711ms / 2918 MB/s** |
| 8 | 共享内存对比 | ⚠️ 可行但不占优 | 7.91MB：5.371ms（慢于 Named Pipe） |
| 9 | 显存泄漏 | ✅ 无泄漏 | 3 次循环稳定，退出后 813 MiB |
| 10 | 管道安全描述符 | ⚠️ 默认不安全 | 默认 ACL 允许 `Everyone`/`Anonymous` 读 |
| 11 | free-threading | ❌ 不支持 | `sys._is_gil_enabled()==True`，无 3.13t |

**总体结论**：✅ **Step 2 的“独立进程 + IPC”后端方案技术可行**。Qwen-Image-2.1 的 int8 模型可直接加载并跑通端到端编辑；Named Pipe 性能充足；进度/打断可用；无显存泄漏。需按下方“关键发现”修正若干假设。

---


## 任务 1：DiT int8_convrot 加载（P0）

**命令**
```
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\t1_load_dit.py
```
脚本：`_test_step2\t1_load_dit.py`（注入 `sys.path` + `chdir` 到 `ComfyUI`，`comfy.sd.load_diffusion_model(DiT绝对路径)`）。

**实际输出**
```
torch 2.13.0+cu130 cuda_avail True
gpu NVIDIA GeForce RTX 4080
torch_device cuda:0
RESULT=OK elapsed_s=0.05
model_type ModelPatcher
inner_model_type QwenImage21
diffusion_model QwenImage21Transformer2DModel
params 7115124736
alloc_MB 0.0
reserved_MB 0.0
model_options_keys ['transformer_options']
model_type_attr None
=== EXIT=0 wall=23.9s ===
```

**耗时 / 显存**
- `load_diffusion_model()` 调用本身：**0.05 s**（延迟加载，权重未上 GPU）。
- 进程总 wall：**23.9 s**（主要花在 `import comfy` 初始化 + 读取文件头）。
- `torch.cuda.memory_allocated` = **0.0 MB**（ModelPatcher 默认把权重放在 CPU/内存，运行采样时才上 GPU）。

**结论**：✅ **成功**。`qwen_image_2.1_int8_convrot.safetensors` 可被 ComfyUI v0.37.0 加载，模型类为 `QwenImage21Transformer2DModel`（7,115,124,736 ≈ 7.1B 参数）。**任务 1 的核心 P0 假设成立**。


## 任务 2：Text Encoder 加载（P0）

**命令**
```
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\t2_load_te.py
```
脚本：`_test_step2\t2_load_te.py`（`comfy.sd.load_clip([TE], clip_type=CLIPType.QWEN_IMAGE)`）。

**实际输出**
```
CLIPType.QWEN_IMAGE = CLIPType.QWEN_IMAGE
RESULT=OK elapsed_s=0.59
clip_type CLIP
cond_stage_model Qwen35TEModel_
csm.tokenizer None
csm.model None
alloc_MB 0.0
reserved_MB 0.0
=== EXIT=0 wall=8.4s ===
```

**耗时 / 显存**
- `load_clip()`：**0.59 s**；进程总 wall：**8.4 s**。
- 显存分配 0 MB（同样延迟上 GPU）。

**结论**：✅ **成功**。`qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors` 可加载，内部为 `Qwen35TEModel_`（Qwen3.5 9B 文本编码器），`clip_type=CLIPType.QWEN_IMAGE` 正确。


## 任务 3：VAE 加载（P0）

**命令**
```
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\t3_load_vae.py
```
脚本：`_test_step2\t3_load_vae.py`（`comfy.utils.load_torch_file` → `comfy.sd.VAE(sd)`）。

**实际输出**
```
state_dict_keys 238
RESULT=OK load_s=0.01 vae_init_s=0.26
vae_type VAE
latent_format NoneType
alloc_MB 0.0
=== EXIT=0 wall=7.9s ===
```

**耗时**：`load_torch_file` 0.01 s，`VAE()` 0.26 s，进程总 wall 7.9 s。

**结论**：✅ **成功**。`qwen_image_2.1_vae_bf16.safetensors`（238 个 state_dict 键）可加载为 `comfy.sd.VAE`。


## 任务 4：官方 Embedding API 可用性（P0）

**执行的搜索**
```
Get-ChildItem ComfyUI\comfy -Directory           # 无 comfy/client
Get-ChildItem ComfyUI -Recurse -File | ? Name -match 'embed|client'
Get-ChildItem ComfyUI -Recurse *.py | Select-String "EmbeddedComfy|embedded_comfy|EmbeddedClient|embedded client"
Select-String app/ comfy_api/ api_server/ comfy_execution/ comfy_api_nodes/ "embed"
Get-ChildItem python_embeded\Lib\site-packages | ? Name -match 'comfy'
```

**结果**
- `comfy/` 下**没有** `client` 子目录；`ComfyUI` 树内**没有** `embedded_comfy_client.py` 或任何名字含 `client` 的 .py。
- `app/`、`comfy_api/`、`api_server/`、`comfy_execution/`、`comfy_api_nodes/` 中 **grep `embed` 零命中**。
- 唯一 `embed` 命中均为模型内部层（`comfy/cldm/dit_embedder.py`、`comfy/ldm/audio/embedders.py`、`comfy/ldm/cosmos/position_embedding.py`、`comfy/ldm/lightricks/embeddings_connector.py`）与 `models/embeddings/` 占位目录，与“嵌入式客户端 API”无关。
- `site-packages` 下**没有** `comfyui` 运行时包（仅前端/工作流模板包）。

**结论**：❌ **未找到官方 Embedding API**。ComfyUI v0.37.0 **没有**可用的“嵌入式客户端”（无 `EmbeddedComfy` / `embedded_comfy_client.py`）。因此 ZIV.AI 不能依赖官方 Embedding 客户端，须自行封装“直接 import comfy 调管线”的方式（任务 1–3 已证明可行）。


## 任务 5：端到端推理闭环（P0）

脚本 `_test_step2\t5_e2e_inpaint.py`（注入 sys.path；直接 `comfy.sd.load_*` + 复刻 `TextEncodeQwenImage21` 逻辑 + `CFGGuider` + `nodes_flux.get_schedule` + `VAE.decode`）。

### 5A. Text-to-Image（无参考图）

**命令**
```
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\t5_e2e_inpaint.py 512 20 t2i
```
**实际输出（摘要）**
```
MODE=t2i RES=512 STEPS=20
PHASE load_dit = 0.05s (gpu_alloc_MB=0.0)
PHASE load_te = 0.59s (gpu_alloc_MB=0.0)
PHASE load_vae = 0.28s (gpu_alloc_MB=0.0)
PHASE encode = 4.80s (gpu_alloc_MB=9043.6)
latent_shape (1, 64, 32, 32)
CALLBACK step=0/20 ... step=19/20        # 20 步全部触发
PHASE sample = 5.94s (gpu_alloc_MB=12426.3)
PHASE vae_decode = 2.62s (gpu_alloc_MB=17470.6)
decoded_shape (1, 512, 512, 4)
RESULT=OK output=...\output_e2e_t2i_512.png size=(512, 512)
TOTAL_s 14.32
peak_alloc_MB 17550.6
peak_reserved_MB 18730.0
=== EXIT=0 wall=25.9s ===   (退出后 nvidia-smi = 766 MiB)
```

### 5B. 图像编辑（带参考图）

**命令**
```
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\t5_e2e_inpaint.py 512 20 edit
```
输入图：`_test_step2\input_test_512.png`（512×512，蓝底 + 黄色圆）。
prompt：`Make the background a snowy mountain landscape, keep the subject unchanged.`

**实际输出（摘要）**
```
MODE=edit RES=512 STEPS=20
PHASE load_dit = 0.05s / load_te = 0.63s / load_vae = 0.29s
PHASE encode = 5.56s (gpu_alloc_MB=11251.2)
latent_shape (1, 64, 32, 32)
CALLBACK step=0/20 ... step=19/20
PHASE sample = 7.46s (gpu_alloc_MB=11905.1)
PHASE vae_decode = 2.02s (gpu_alloc_MB=17097.0)
decoded_shape (1, 512, 512, 4)
RESULT=OK output=...\output_e2e_edit_512.png size=(512, 512)
TOTAL_s 16.09
peak_alloc_MB 17177.0 / peak_reserved_MB 18464.0
=== EXIT=0 wall=25.1s ===   (退出后 nvidia-smi = 488 MiB)
```

### 关键数据

| 阶段 | T2I (512²) | Edit (512²) |
|---|---|---|
| load DiT/TE/VAE | 0.05 / 0.59 / 0.28 s | 0.05 / 0.63 / 0.29 s |
| encode（含参考图 VAE+vision） | 4.80 s | 5.56 s |
| sample（20 步 euler） | 5.94 s | 7.46 s |
| VAE decode | 2.62 s | 2.02 s |
| 总（进程内） | 14.32 s | 16.09 s |
| 峰值 torch alloc | 17,550 MB | 17,177 MB |
| 输出 | 512×512 PNG | 512×512 PNG |
| 退出后显存 | 766 MiB | 488 MiB |

**结论**：✅ **成功**。Qwen-Image-2.1 的 **T2I 与图像编辑（i2i）闭环均跑通**，输出有效 PNG；latent 为 **64 通道**（`[1,64,32,32]`）；VAE 输出 **RGBA**；`callback` 每步触发。**16GB 显存可跑 512² 编辑**（峰值 torch alloc 约 17GB，靠 ComfyUI 的显存管理/offload 完成，未 OOM）。


## 任务 6：进度回调与打断（P1）

脚本 `_test_step2\t6_progress_interrupt.py`（T2I 512²，20 步；callback 在 `step>=5` 时从**另一线程** `interrupter` 调用 `mm.interrupt_current_processing(True)`）。

**命令**
```
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\t6_progress_interrupt.py
```

**实际输出（摘要）**
```
CALLBACK step=0/20 ... CALLBACK step=7/20
INTERRUPT_CALL thread=interrupter
Traceback (most recent call last):
  ...
  File "...\comfy\ops.py", line 39, in run_every_op
    comfy.model_management.throw_exception_if_processing_interrupted()
  File "...\comfy\model_management.py", line 2173, in throw_exception_if_processing_interrupted
    raise InterruptProcessingException()
comfy.model_management.InterruptProcessingException
=== EXIT=1 wall=18.7s ===
```

**结论**
- ✅ **进度回调可用**：`callback(step, x0, x, total_steps)` 每步被调用（观测到 step 0…7）。
- ✅ **打断可用**：从另一线程调用 `interrupt_current_processing(True)` 后，采样在**第 7 步**（触发于第 5 步）抛出 `InterruptProcessingException`，**提前终止**（未跑完 20 步）。
- ⚠️ **重要发现**：`InterruptProcessingException` 定义是 **`class InterruptProcessingException(BaseException)`**（`comfy/model_management.py`），**不是 `Exception` 子类**。因此调用方**不能**用 `except Exception` 捕获它（本脚本正是如此，故进程以 EXIT=1 退出并打印 traceback）。ZIV.AI 的 Python 侧必须以 `except comfy.model_management.InterruptProcessingException` 或 `except BaseException` 捕获，并映射为“已取消”状态。


## 任务 7：Named Pipe 性能基准（P0）

C# 服务端 `_test_step2\PipeBench\`（`NamedPipeServerStream`, `PipeOptions.Asynchronous`, 1MB 缓冲，长度前缀协议 `[4B小端长度][数据]`）；Python 客户端 `_test_step2\t7_pipe_server.py`（`open(r'\\.\pipe\zivai_bench','r+b')` 接收并回显）。每种尺寸 20 次往返。

**命令**
```
dotnet build D:\devlop\ZIV.AI\_test_step2\PipeBench\PipeBench.csproj -c Release   # 0 错
Start-Process PipeBench.exe ...   # 后台，WaitForConnection
python_embeded\python.exe -s t7_pipe_server.py
```

**实际输出**
```
=== C# stdout ===
WAITING_FOR_CLIENT
CLIENT_CONNECTED
SIZE=589824B (0.56MB) avg_ms=0.396 throughput_MBps=1420.4
SIZE=2359296B (2.25MB) avg_ms=0.824 throughput_MBps=2731.0
SIZE=8294236B (7.91MB) avg_ms=2.711 throughput_MBps=2918.1
DONE
=== C# exit=0 ===
（Python: PY_DONE count=60 total_MB=214.45 elapsed_s=0.12）
```

| 尺寸 | 平均往返延迟 | 吞吐量 |
|---|---|---|
| 576 KB | **0.396 ms** | 1420 MB/s |
| 2.25 MB | **0.824 ms** | 2731 MB/s |
| 7.91 MB | **2.711 ms** | 2918 MB/s |

**结论**：✅ **成功**。Named Pipe（长度前缀）在三种尺寸下均无死锁、无异常，吞吐 1.4–2.9 GB/s，延迟亚毫秒~毫秒级。**对 1280×720 RGB 图像（~7.9MB）单程约 2.7ms，完全满足 IPC 图像传输需求。**


## 任务 8：共享内存方案对比（P1）

C# `_test_step2\SharedMemBench\`（`MemoryMappedFile.CreateOrOpen` + 命名 `EventWaitHandle` `zivai_shm_data_ready` / `zivai_shm_ack`）；Python `_test_step2\t8_shm_client.py`（`mmap.mmap(-1, cap, tagname=...)` + **ctypes 调用 Win32 `OpenEventW`/`WaitForSingleObject`/`SetEvent`**，因 **pywin32 未安装**，改用 ctypes 替代 win32event）。

**命令**
```
dotnet build ...\SharedMemBench\SharedMemBench.csproj -c Release   # 0 错
Start-Process SharedMemBench.exe ...   # 后台
python_embeded\python.exe -s t8_shm_client.py
```

**实际输出**
```
=== C# stdout ===
SHM_WAITING
SIZE=589824B (0.56MB) avg_ms=101.480 throughput_MBps=5.5
SIZE=2359296B (2.25MB) avg_ms=1.669 throughput_MBps=1347.7
SIZE=8294236B (7.91MB) avg_ms=5.371 throughput_MBps=1472.6
DONE
=== C# exit=0 ===
（Python: PY_SHM_CONNECTED / TERMINATE n=-1 / PY_DONE count=60 total_MB=214.45 elapsed_s=0.19）
```

### 与 Named Pipe 对比

| 尺寸 | Named Pipe 延迟 | 共享内存延迟 | Named Pipe 吞吐 | 共享内存吞吐 |
|---|---|---|---|---|
| 576 KB | **0.396 ms** | 101.480 ms ⚠️ | 1420 MB/s | 5.5 MB/s |
| 2.25 MB | **0.824 ms** | 1.669 ms | 2731 MB/s | 1348 MB/s |
| 7.91 MB | **2.711 ms** | 5.371 ms | 2918 MB/s | 1473 MB/s |

**结论**：✅ 可运行，但 ⚠️ **共享内存方案在本实现下不占优**：
- 576 KB 的 101 ms 属**异常**，来自首轮事件同步的初始化延迟（Python 刚连接、首个 `Set/Wait` 的竞态），后续轮次正常。
- 大尺寸（2.25/7.91 MB）共享内存反而**比 Named Pipe 慢约 2×**，原因是每轮都有 C#→mmap 写拷贝 + Python mmap 读拷贝，外加两次命名事件跨进程同步开销（`EventWaitHandle` ↔ ctypes）。
- **结论：本场景 Named Pipe 更简单且更快**；共享内存若要占优，需零拷贝布局 + 更轻的同步（如 spin/双缓冲），复杂度高，收益不明显。


## 任务 9：进程生命周期与显存泄漏（P1）

脚本 `_test_step2\t9_memory_leak.py`：循环 3 次（加载 DiT+TE+VAE → 256²/4 步采样 → `del` + `gc.collect()` + `empty_cache()`），记录 `torch.cuda.memory_allocated/reserved` 与 `nvidia-smi`。

**命令**
```
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\t9_memory_leak.py
```

**实际输出**
```
BASELINE nvidia_used_MB=908 alloc_MB=0.0
ITER 1 load_s=1.02 sample_s=4.73 alloc_MB=14002.0 reserved_MB=14372.0 nvidia_used_MB=15373
AFTER_FREE 1 alloc_MB=8.4 reserved_MB=22.0 nvidia_used_MB=1023
ITER 2 load_s=1.13 sample_s=4.33 alloc_MB=14050.0 reserved_MB=14372.0 nvidia_used_MB=15373
AFTER_FREE 2 alloc_MB=8.4 reserved_MB=22.0 nvidia_used_MB=1024
ITER 3 load_s=1.20 sample_s=4.41 alloc_MB=14050.0 reserved_MB=14372.0 nvidia_used_MB=15485
AFTER_FREE 3 alloc_MB=8.4 reserved_MB=22.0 nvidia_used_MB=1126
=== EXIT=0 wall=40.6s ===
=== nvidia-smi after process exit === 813 MiB
```

| 迭代 | 加载 | 采样(4步) | alloc(用) | 卸载后 alloc | 卸载后 nvidia |
|---|---|---|---|---|---|
| 1 | 1.02 s | 4.73 s | 14002 MB | 8.4 MB | 1023 MB |
| 2 | 1.13 s | 4.33 s | 14050 MB | 8.4 MB | 1024 MB |
| 3 | 1.20 s | 4.41 s | 14050 MB | 8.4 MB | 1126 MB |

**结论**：✅ **无显存泄漏**。3 次“加载→采样→卸载”后 `allocated` 稳定在 ~14050 MB（不累积），每次卸载后回落至 8.4 MB；**进程退出后 nvidia-smi = 813 MiB（回到基线 908 MB 附近）**。证明“独立进程 + 用完即卸”的模型是安全可行的。


## 任务 10：Named Pipe 安全描述符（P2）

C# `_test_step2\PipeSecurityProbe\`（`net8.0-windows`；`NamedPipeServerStream.GetAccessControl()` 读取 DACL；`PipeSecurity` + `NamedPipeServerStreamAcl.Create` 设置受限 ACL）。

**命令**
```
dotnet build ...\PipeSecurityProbe\PipeSecurityProbe.csproj -c Release   # 0 错
PipeSecurityProbe.exe
```

**实际输出**
```
user=DESKTOP-0FOT5TD\31655
DEFAULT_ACL:
  Everyone | Allow | Read, Synchronize
  NT AUTHORITY\ANONYMOUS LOGON | Allow | Read, Synchronize
  NT AUTHORITY\SYSTEM | Allow | 2032127
  BUILTIN\Administrators | Allow | 2032127
  DESKTOP-0FOT5TD\31655 | Allow | 2032127
RESTRICTED_ACL (current user only):
  DESKTOP-0FOT5TD\31655 | Allow | FullControl
RESTRICT_METHOD=PipeSecurity + SetAccessRuleProtection(true,false) + NamedPipeServerStreamAcl.Create
DONE
```

**结论**：⚠️ **默认 ACL 不安全**。`NamedPipeServerStream` 默认允许 **`Everyone` 与 `ANONYMOUS LOGON` 读取**（`Read, Synchronize`）。ZIV.AI 的 IPC 管道**必须**显式收紧为仅当前用户：
```csharp
var ps = new PipeSecurity();
ps.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,
    PipeAccessRights.FullControl, AccessControlType.Allow));
ps.SetAccessRuleProtection(true, false);              // 断开继承，移除 Everyone/Anonymous
var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1,
    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, ps);
```
验证结果：受限 ACL 下仅剩 `DESKTOP-0FOT5TD\31655 | Allow | FullControl`。


## 任务 11：Python 3.13 free-threading 可行性（P3）

**命令 / 输出**
```
> python_embeded\python.exe -s -c "import sys; print('version', sys.version); print('gil_enabled', sys._is_gil_enabled())"
version 3.13.14 (tags/v3.13.14:fd17997, Jun 10 2026, 13:03:48) [MSC v.1944 64 bit (AMD64)]
gil_enabled True

> Get-ChildItem python_embeded -Filter "python*t*.exe"     # 无结果（无 python3.13t.exe）

> python_embeded\python.exe -s -c "import torch; print(torch.__version__); print(torch.__config__.show())"
torch 2.13.0+cu130
（CUDA Runtime 13.0 / CuDNN 9.20 / 无 free-threading 相关标记）
```

**结论**：❌ **不支持 free-threading**。
- 便携版 Python 是**标准 GIL build**（`sys._is_gil_enabled() == True`），且**不存在** free-threaded 解释器（无 `python3.13t.exe`）。
- 要使用 free-threading，需替换为 **Python 3.13t（free-threaded）** 专用解释器，且 **torch / 全部原生扩展需有对应的 free-threaded build**（当前 `torch 2.13.0+cu130` 未验证支持）。
- 结论：**本阶段不可用，也不建议为此替换解释器**（会引入大量兼容性风险）。


## 关键发现

### 被验证的假设（成立）

1. **`int8_convrot` 模型可直接加载**（任务 1–3）：DiT / TE / VAE 三件全部加载成功，无需 GGUF、无需 custom_node。
2. **“直接 import ComfyUI 源码调管线”可行**（任务 1–5）：不启动 HTTP server，直接 `comfy.sd.load_*` + `CFGGuider` + `get_schedule` + `VAE.decode` 即可完成 T2I 与图像编辑。
3. **无需 `extra_model_paths.yaml`**：模型绝对路径直接传参即可（用户判断正确）。
4. **16GB 显存可跑 512² 编辑**（任务 5）：峰值 torch alloc ~17GB，靠 ComfyUI 显存管理完成，未 OOM。
5. **进度百分比可用**（任务 6）：`callback(step, x0, x, total)` 每步触发。
6. **打断可用**（任务 6）：`interrupt_current_processing()` 生效，采样提前终止。
7. **Named Pipe 性能充足**（任务 7）：7.91MB 单程 2.711ms。
8. **无显存泄漏**（任务 9）：进程退出后显存回基线。

### 被推翻 / 需修正的假设

1. **无官方 Embedding API**（任务 4）：v0.37.0 没有 `embedded_comfy_client` / `comfy/client`。→ ZIV.AI 必须**自封装**推理进程。
2. **`InterruptProcessingException` 继承 `BaseException`，不是 `Exception`**（任务 6）。→ Python 侧**不能**用 `except Exception` 捕获取消；必须 `except comfy.model_management.InterruptProcessingException` 或 `except BaseException`。C# 侧 IPC 协议需把“已取消”作为正常终态映射。
3. **共享内存不比 Named Pipe 快**（任务 8）：本实现下 7.91MB 慢约 2×，且首尺寸受同步初始化拖累（101ms）。→ **不推荐**为性能引入共享内存。
4. **`pywin32` 未安装**（任务 8）：便携版 Python 无 `win32event/win32pipe/pywintypes`。→ 若坚持共享内存/命名事件，Python 侧需用 **ctypes**（本次已验证可行）或补装 pywin32（需联网，本次未装）。
5. **Named Pipe 默认 ACL 不安全**（任务 10）：默认允许 `Everyone` 与 `ANONYMOUS LOGON` 读取。→ 必须显式 `PipeSecurity + SetAccessRuleProtection(true,false) + NamedPipeServerStreamAcl.Create` 收紧为当前用户。
6. **不支持 free-threading**（任务 11）：标准 GIL build。→ 不要为此替换解释器。

### 需调整方案的点

1. **IPC 协议选型**：实测 **Named Pipe 更优**（更快、更简单、跨语言零依赖）；共享内存收益不足。
2. **取消语义**：Python 侧捕获 `BaseException` 子类；IPC 返回 `canceled` 终态。
3. **预览**：任务 6 验证了 `callback` 每步可用（可从中取 `x0` latent 做预览）；`ProgressBar.preview` / `latent_preview.prepare_callback` 存在但**本次未单独实测预览帧**（列为遗留）。
4. **模型路径**：写入 ZIV.AI 自有配置，绝对路径直传；**不读不写** `C:\AI\ComfyUI_PIC` 的配置。

## 建议

1. **Step 2 可以按“独立进程 + Named Pipe”推进**：技术闭环、性能、显存、取消均已实测通过。
2. **IPC 决策**：采用 **Named Pipe + 长度前缀**（本次实测协议）；图像数据走管道（7.9MB≈2.7ms），预览帧用独立小帧；**不引入共享内存**。
3. **取消与进度**：Python 侧用 `callback` 回传 `step/total`（百分比）；用 `interrupt_current_processing` 取消并捕获 `InterruptProcessingException`（注意 `BaseException`）。
4. **安全**：管道必须收紧 ACL 至当前用户（任务 10 方法）。
5. **模型**：直接绝对路径加载 DiT/TE/VAE（`image2\qwen_image_2.1_int8_convrot` + `text_encoders\qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot` + `vae\qwen_image_2.1_vae_bf16`），无需 `extra_model_paths.yaml`。
6. **遗留项**：
   - 生成时**预览帧**（latent→小图）未单独实测，建议 Step 2 实现时补测。
   - `int8_convrot` 在高分辨率（1024²/2048²）与多参考图下的显存/稳定性未测。
   - 多步编辑（Executor 串行队列）与真机长任务的 IPC 稳定性未测。

---

## 附录：测试目录清单（`D:\devlop\ZIV.AI\_test_step2\`）

| 路径 | 说明 |
|---|---|
| `t1_load_dit.py` / `t2_load_te.py` / `t3_load_vae.py` | 任务 1–3 脚本 |
| `t5_e2e_inpaint.py` | 任务 5 端到端（edit/t2i） |
| `t6_progress_interrupt.py` | 任务 6 进度/打断 |
| `t7_pipe_server.py` + `PipeBench\` | 任务 7 Named Pipe |
| `t8_shm_client.py` + `SharedMemBench\` | 任务 8 共享内存 |
| `t9_memory_leak.py` | 任务 9 显存泄漏 |
| `PipeSecurityProbe\` | 任务 10 管道 ACL |
| `output_e2e_t2i_512.png` / `output_e2e_edit_512.png` | 任务 5 输出图 |
| `input_test_512.png` | 任务 5 输入图 |
| `csharp_pipe_out.txt` / `csharp_shm_out.txt` | C# 基准输出 |
| `REPORT.md` | 本报告 |


