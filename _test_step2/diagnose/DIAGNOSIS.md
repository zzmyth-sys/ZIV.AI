# ZIV.AI pipeline 纯噪声：根因诊断报告

- 日期：2026-09-22
- 环境：Windows 10 / RTX 4080 16GB / ComfyUI v0.37.0 / comfy_kitchen 0.2.35 / Python 3.13.14
- 诊断脚本：`_test_step2/diagnose/diag_schedule.py`；输出图 `_test_step2/diagnose/*.png`
- 结论：**根因是「文本编码器与 DiT 不匹配」——所配 TE 是 Qwen3.5-9B，而 ComfyUI 的
  Qwen-Image-2.1 集成要求 Qwen3-VL-8B。采样日程、VAE、int8+convrot 量化加载均不是原因。**
- **已确认修复**：改用 `qwen3vl_8b_int8_convrot.safetensors` 后，T2I 与参考图编辑**均正常出图**
  （见 §7）。

---

## 1. t5_e2e_inpaint.py vs pipeline.py 采样配置差异

| 项 | t5 | pipeline.py | 差异 |
|---|---|---|---|
| `get_schedule` | `get_schedule(STEPS, seq_len)` | 同 | 无 |
| sigmas 来源 | `torch.tensor(get_schedule(...), f32)` | `get_schedule(...).to(f32)` | 无（同源） |
| model_sampling 绑定 | 不绑定，用模型内置 | 同 | 无（两者都绕过） |
| CFGGuider | `CFGGuider(model)`+`set_conds`+`set_cfg(1.0)` | 同 | 无 |
| sampler_name | `euler` | `euler` | 无 |
| latent 初始化 | `torch.zeros([1,64,h//16,w//16])` | 同 | 无 |
| conditioning | `encode_from_tokens_scheduled`+`reference_latents` | 同（`_encode` 同逻辑） | 无 |
| VAE decode | `vae.decode(samples)` | 同 | 无 |

**t5 与 pipeline 的采样配置一致**；`output_e2e_edit_512.png`（t5 的产物）即为噪声 →
"t5 能正常出图"的前提不成立，噪声从 Step 2.3 起一直存在。

## 2. 最小复现实验结果（每项均保存图，肉眼核对）

| 变体 | 配置 | mean | std | 结果 |
|---|---|---|---|---|
| ROUNDTRIP | `vae.encode(原图)`→`vae.decode` | 0.486 | 0.342 | **有效**（蓝底黄圆清晰） |
| V0 | 当前路径：`get_schedule` + `CFGGuider` | 0.582 | 0.248 | **噪声** |
| V1 | sigmas 改用模型 `model_sampling`(`calculate_sigmas simple`) | 0.586 | 0.246 | **噪声** |
| V2s | 官方 `comfy.sample.sample`（scheduler=simple） | 0.586 | 0.246 | **噪声** |
| V2n | 官方 `comfy.sample.sample`（scheduler=normal） | 0.582 | 0.248 | **噪声** |
| V3 | 官方 `comfy.sample.sample`，**无参考图 T2I** | 0.558 | 0.288 | **噪声** |

要点：
- **替换 sigma 日程（V0→V1→V2）没有改善**，且 V0/V1/V2 统计几乎相同 → 日程不是根因。
- **VAE 往返清晰** → VAE 编解码正常。
- **T2I（无参考图）同样是噪声** → 与 `reference_latents` 注入无关，问题在更底层
  （DiT 权重 或 prompt 条件）。
- 运行中打印：`model_sampling=ModelSampling shift=0.69`（= Qwen-Image-2.1 的 `sampling_settings`）。

## 3. 量化加载：已确认受支持（不是根因）

- DiT 权重为 ComfyUI 原生量化「version 1」：`weight`(I8) + `weight_scale`(F32) + `comfy_quant`(U8)。
- `comfy_quant` 元数据：`{"format":"int8_tensorwise","convrot":true,"convrot_groupsize":256}`。
- `comfy/quant_ops.py` 有 `int8_tensorwise`→`TensorWiseINT8Layout`；`comfy/ops.py:1231/1314`
  处理 `convrot`；`comfy_kitchen 0.2.35` 的 `TensorWiseINT8Layout.Params` **明确含
  `convrot`/`convrot_groupsize`**（`comfy_kitchen/tensor/int8.py`）。
- → **int8+convrot 的解量化路径在本构建中受支持**（4080 SM 8.9 支持 int8 tensor core）。

## 4. 根因：TE 与 DiT 不匹配

**证据链（全部来自静态读取，未再起模型）**：

1. DiT 探测为 **Qwen-Image-2.1**：`comfy/model_detection.py:962` 命中 `qwen_image21_keys`；
   运行时 `model_sampling.shift=0.69` 与 `comfy/supported_models.py:2055-2063` 的
   `class QwenImage21` 完全一致。
2. ComfyUI 的 Qwen-Image-2.1 集成**指定 TE = Qwen3-VL-8B**：
   - `comfy/supported_models.py:2079-2082`：`QwenImage21.clip_target` →
     `hunyuan_video.llama_detect(sd, "qwen3vl_8b.transformer.")` +
     `comfy.text_encoders.qwen_image21.te/QwenImage21Tokenizer`。
   - `comfy/sd.py:1955-1958`：`CLIPType.QWEN_IMAGE and te_model == TEModel.QWEN3VL_8B`
     才走 `qwen_image21` 分支。
3. 实际 TE 文件是 **Qwen3.5-9B**：
   - 文件名 `qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors`。
   - safetensors 键含 `model.language_model.layers.0.linear_attn.in_proj_qkv.*` →
     `linear_attn`（GatedDeltaNet）是 **Qwen3.5** 专有结构
     （`comfy/text_encoders/qwen35.py:411`），Qwen3-VL 用标准注意力。
   - Step 2 实测记录：`cond_stage_model Qwen35TEModel_`（`_test_step2/REPORT.md` 任务 2）。
4. 因此 `detect_te_model` 判为 `QWEN35_9B`，`sd.py:1929` 分支（**无 clip_type 判断**）把它
   加载为 `comfy.text_encoders.qwen35.te`，而**不是** `qwen_image21`。于是 Qwen-Image-2.1 的
   DiT 被喂入**另一套架构文本编码器**产出的 conditioning。
   - 二者 hidden_size 恰好都是 4096 → 不报形状错误，但特征语义不对 → DiT 输出崩溃为噪声；
     T2I 与 Edit 同样噪声也印证了这一点。

> 说明：这属于**模型文件与 ComfyUI 版本不配套**，不是 ZIV.AI 代码（`pipeline.py`/`handlers.py`）
> 或采样参数的缺陷。`get_schedule` 用 Flux 经验 mu 虽与模型 `shift=0.69` 不一致（属次要问题），
> 但已实测其对"是否出图"没有影响。

## 5. 修复建议（不改冻结契约）

1. **首选：换用配套模型**。使用 ComfyUI 官方 Qwen-Image-2.1 组合——
   DiT `qwen_image_2.1` + **TE `qwen3vl_8b`**（`supported_models.py` 指定的那一个），
   再接 `TextEncodeQwenImage21`。当前 `qwen3.5_9b_..._pe_i2i` TE 与官方集成不匹配。
2. **若必须用该自定义 TE**：确认其配套 DiT/ComfyUI 版本与推理脚本（可能是 `pe_i2i` 提示词
   增强专用流程，而非主文本编码器），按厂商脚本走，不要用手工 `CFGGuider` 管线。
3. **次要修正（可选）**：pipeline 的 `get_schedule`（Flux 经验 mu）应改为从模型
   `model_sampling` 派生 sigmas（`comfy.samplers.calculate_sigmas`），与 Qwen-Image-2.1 的
   `shift=0.69` 对齐；或直接走 `comfy.sample.sample`。此项**不改变本次结论**，但更规范。
4. **测试补强**：`IpcInferenceTests` / 基线只校验尺寸与文件存在，应增加**画面有效性断言**
   （如局部方差/结构阈值）或内容比对，避免"噪声也通过"。

## 6. 附录：复现方式

```powershell
D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe -s D:\devlop\ZIV.AI\_test_step2\diagnose\diag_schedule.py
```
（脚本加载模型并跑 ROUNDTRIP/V0/V1/V2s/V2n/V3，输出到 `diagnose/*.png`。）

---

## 7. 确认实验：换用 Qwen3-VL-8B TE 后正常出图

- 新 TE：`C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3vl_8b_int8_convrot.safetensors`
  （Qwen3-VL-8B：`model.layers.0.self_attn.q_proj.weight [4096,4096]`、MLP `[12288,4096]`，
  含 `visual`/`deepstack`，无 `linear_attn`；`int8_tensorwise + convrot`）。
- 脚本：`_test_step2/diagnose/diag_with_correct_te.py`。
- 关键日志：**`cond_stage_model = QwenImage21TEModel_`**（此前为 `Qwen35TEModel_`）。

| 用例（`comfy.sample.sample`, euler, simple, 20 步, seed 42） | mean | std | 结果 |
|---|---|---|---|
| T2I cfg=1.0 | 0.728 | 0.279 | **有效**（雪山日出） |
| T2I cfg=4.0 | 0.706 | 0.253 | **有效** |
| Edit（参考图）cfg=1.0 | 0.792 | 0.208 | **有效**（背景替换为雪山，黄色主体保留） |
| Edit（参考图）cfg=4.0 | 0.920 | 0.129 | **有效** |
| Edit + 管线自带 `get_schedule` + cfg=4.0 | 0.823 | 0.175 | **有效** |

**结论**：根因确认为 TE/DiT 不匹配。换成 Qwen3-VL-8B TE 后，无论官方 `comfy.sample.sample`
还是管线自带的 `get_schedule` 路径都能出图；`get_schedule`（Flux 经验 mu）只是次要的
质量/规范问题，不影响正确性。峰值显存 17.8 GB。

## 8. 修复动作建议（需授权）

1. **必须**：把 `python/server/config.py` 的 `TEXT_ENCODER_PATH` 从
   `qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors` 改为
   `qwen3vl_8b_int8_convrot.safetensors`。
2. **建议**：`pipeline.py` 的 CFG 由 `1.0` 提升到 **4.0**（实测 cfg=4.0 细节更好；Qwen-Image 官方
   亦用较高 CFG），并把 `get_schedule` 改为由 `model_sampling` 派生 sigmas（可选）。
3. 回归：换 TE 后重跑 `IpcInferenceTests` / 基线，并加画面有效性断言。

> 上述改动涉及 `config.py` / `pipeline.py`，按约定**需另行授权**后再执行。

### 8.1 已应用修复（2026-09-22，经授权）

- **已改**：`python/server/config.py` 的 `TEXT_ENCODER_PATH` →
  `qwen3vl_8b_int8_convrot.safetensors`。未改 `pipeline.py` / `handlers.py` / `Contracts` / `DOC/`。
- **端到端复验**（`e2e_acceptance.exe`，经 IPC 后端，prompt=换背景为古代中式茶肆）：
  `status=Succeeded`，输出 `user_output_768.png` 640×416。
  **画面正确**：背景替换为古代中式茶肆（木桌椅、灯笼、古朴陈设），人物与前景保留，
  右上角水印去除。峰值显存 12,686 MiB。
- **回归**：`dotnet test ZIV.AI.sln` → **14 通过 / 0 失败**。
- 次要项（未做）：CFG 仍为 1.0；`get_schedule` 仍为 Flux 经验 mu（不影响正确性）。
