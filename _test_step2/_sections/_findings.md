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
