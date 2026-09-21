# ZIV.AI Step 4 端到端编辑验收报告

- 日期：2026-09-22
- 环境：Windows 10 / RTX 4080 16GB / ComfyUI v0.37.0（Python 3.13.14）/ .NET SDK 10.0.401
- 验收程序：`_test_step2/e2e_acceptance/`（net8.0-windows，引用 `ZivAiEditor.Backend`）
- 结论：**验失败（QUALITY FAIL）——后端输出为纯噪声，未完成背景替换，人物/前景未保留。**

> 输入文件说明：任务给定路径 `user_input.png` 不存在；经用户确认，使用同目录
> `换背景.png`（2560×1599, 12,301,890 B），并复制为 `user_input.png` 作为验收输入。

## 1. 输入图片信息

| 项 | 值 |
|---|---|
| 原图（`换背景.png` / `user_input.png`） | 2560×1599, RGB, 12,301,890 B |
| 缩放后（`user_input_768.png`） | 768×480, RGB, 320,448 B（LANCZOS，保持 16:10） |
| 原图 SHA-256（运行前） | `302fdd289217705a4ce6c39d294fc7d0f0f6e40844c2b71cc0f3ea038f7e0aaf` |
| 原图 SHA-256（运行后） | `302fdd289217705a4ce6c39d294fc7d0f0f6e40844c2b71cc0f3ea038f7e0aaf`（**一致，Z24 满足**） |

## 2. 使用的 prompt 与实际参数

- **Prompt**：`把背景替换为古代中式茶肆，画面内应有木质桌椅、悬挂的灯笼、古朴的装饰，保留画面中的人物、衣着与前景陈设不变，去掉右上角水印`
- `Steps = 20`、`Seed = 42`、`Denoise = 1.0`（见下）、`ImagePath = user_input_768.png`、`MaskPath = null`、`OutputPath = user_output_768.png`
- 未触发 OOM，未走 640×400 / denoise=0.6 降级路径。

## 3. pipeline 处理方式（任务 1 读码结论）

`python/server/pipeline.py`（未修改）：

1. **无 mask 时从纯噪声开始（T2I），不是从原图 latent（img2img）。**
   依据 `_encode()`：`latent_samples = references[0]` **仅当** `mask is not None and references`；
   无 mask 时走 `torch.zeros([1,64,h,w])`（零 latent），随后 `guider.sample(noise, samples_latent, ...)`。
2. **不支持 `denoise < 1.0` 的部分重绘。** `run()` 根本没有读取 `request["denoise"]`；
   `sample()` 调用 `guider.sample(...)` 未传 `denoise`（默认 1.0）。故 `Denoise=0.75` 无意义，
   实际按 **1.0** 执行。
3. **支持「原图作为 reference_latents 注入」。** `_encode()` 把原图 resize 到 512 面积
   （`config.DEFAULT_RESOLUTION=512`，仅按纵横比）→ `vae.encode` → `reference_latents`
   注入 positive/negative conditioning。

→ 因此采用**参考图条件生成（reference-conditioned T2I）**：传 `image_path`、不传 mask。
另外：**pipeline 会把输入归一化到 512 面积**，输出尺寸与缩放后的 768×480 无关，实测输出
**640×416**。

## 4. 耗时分解（默认配置）

| 项 | 值 |
|---|---|
| 模型加载 | 4,430 ms |
| moving_to_gpu | 5,530 ms |
| 采样（20 步） | 1,612 ms |
| VAE decode | 2,305 ms |
| 命令总耗时（含 Python 启动/连接） | 24,043 ms |
| pipeline 内部 `duration_ms` | 15,146 ms |
| sampling progress 帧数 | 20 |
| preview 帧数 | 20 |
| 首个 preview 帧尺寸 | 40×26 |

## 5. 输出图片信息

- 路径：`D:\devlop\ZIV.AI\_test_step2\user_output_768.png`
- 尺寸：**640×416**，模式 **RGBA**，大小 **657,450 B**
- 可打开性：PIL `verify()` + 重新 `load()` 均 OK
- 客观指标：mean≈0.400、std≈0.125（低方差、无结构，与"噪声"一致；输入图 std≈0.290）

## 6. 峰值显存

| 配置 | gpu_peak | 运行后 |
|---|---|---|
| 默认（`disable_smart_memory=True`，Step 3.4） | **13,147 MiB** | 10,009 MiB（模型驻留，进程退出后回落 775 MiB） |
| 诊断：`ZIV_AI_DISABLE_SMART_MEMORY=0` | 16,018 MiB | 16,014 MiB |

## 7. 异常 / 警告

- 无 OOM、无异常抛出；`SubmitInpaintAsync` 返回 `Succeeded`，输出文件存在。
- **重大异常**：输出为**纯噪声**，无任何可辨识画面。经对比，该现象**不是本次引入**：
  - `_test_step2/output_e2e_edit_512.png`（Step 2 任务 5 "编辑"产物）= 噪声
  - `_test_step2/output_e2e_t2i_512.png`（Step 2 任务 5 "T2I"产物）= 噪声
  - `_test_step2/baseline_out_1..3.png`（Step 4 基线产物）= 噪声
  - 而 `input_test_512.png` / `user_input_768.png` 正常。
  即：**该后端此前从未产出过有效画面**；既有 Step 2/4 的"通过"只校验了文件存在与尺寸，
  未校验图像内容。
- 已做诊断：把 Step 3 默认的 `disable_smart_memory` 关闭重跑，输出**仍为噪声**，
  故与显存策略无关，指向**采样/管线配置本身**（如 `get_schedule` 与 Qwen-Image-2.1
  采样族不匹配、或 `CFGGuider.sample` 直连参数不当）。
  按约束未修改 `pipeline.py` / `handlers.py`，仅报告。

## 8. 主观评价

- **人物是否保留**：否。画面无人物、无窗棂、无案桌，完全不可辨认。
- **背景替换效果**：未实现。输出为高频率彩色噪声。
- **明显瑕疵**：整图即瑕疵（100% 噪声）。
- **总体**：**验收失败**。端到端链路（IPC/进度/预览/结果/文件写出/Z24 原图安全）功能性正常，
  但**生成质量不合格**。

## 9. 复现命令

```powershell
# 输入准备（已执行）
python -s -c "from PIL import Image; Image.open(r'...\换背景.png').resize((768,480), Image.LANCZOS).save(r'...\user_input_768.png')"

# 验收
dotnet build D:\devlop\ZIV.AI\_test_step2\e2e_acceptance\e2e_acceptance.csproj -c Release
D:\devlop\ZIV.AI\_test_step2\e2e_acceptance\bin\Release\net8.0-windows\e2e_acceptance.exe
```

## 10. 相关产物

| 文件 | 说明 |
|---|---|
| `user_input_768.png` | 缩放后输入（768×480） |
| `user_output_768.png` | 本次输出（640×416，**噪声**） |
| `user_output_768_noise.png` | 默认配置首次运行输出（噪声，备份） |
| `RESULT.md` | 本报告 |
