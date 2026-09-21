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
