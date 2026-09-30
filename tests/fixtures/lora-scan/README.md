# lora-scan 对齐 fixture（只读）

跨语言对齐测试**共享**的同一份「公共 LoRA 场景」，供：

- C#：`src/ZivAiEditor.Tests/LoraScanAlignmentTests.cs`（调 `SettingsWindow.ScanPublicLoras`）
- Python：`python/server/test_lora_scan_alignment.py`（调 `lora-manager.scan_public`）

## 内容

- `models/loras/`
  - `a.safetensors` — `loras.json` 记 `owner:model` → **不该列**（模板私有）
  - `b.safetensors` — `loras.json` 记 `owner:Plugin` → **不该列**（插件自有）
  - `c.safetensors` — 未记录 → **该列**（公共）
  - `d.ckpt` — 未记录 → **该列**（覆盖扩展名集合，`.ckpt` 非 safetensors）
- `loras.json` — 数据表（仅 a/b 两条记录）
- `expected.json` — `{ "public_loras": ["c.safetensors", "d.ckpt"] }`（**集合**，无顺序）

## 约定

- 这些是**空占位文件**，不实际加载，仅用于文件名扫描。
- 两侧测试**只断言集合相等**（不断言顺序，因 C# 与 Python 排序不同）。
- `d.ckpt` 是必需的：若只放 `.safetensors`，C# 曾经只扫 `*.safetensors` 的旧规则不会被测试暴露。
