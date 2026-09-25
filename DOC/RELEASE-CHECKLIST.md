# ZIV.AI 发布前检查清单

- 状态：开发期登记，发布前必做
- 用途：记录发布前必须处理的技术债

## 1. 路径硬编码（Z14）

- 现状：`PythonProcessManager.cs:22-24`、`SettingsLoader.cs:12-14`、
  `settings.ini.template` 的 `[backend]` 段均硬编码 `D:\devlop\ZIV.AI\...`
- 影响：发布到 `D:\Program Files\ZIV.AI` 后仍指向开发机路径
- 处理：发布前改为读配置 / 相对路径（程序根 = `Path.GetDirectoryName(Environment.ProcessPath)`）

## 2. publish.ps1 不打包后端

- 现状：只发布 C# App，不带 ComfyUI/python
- 处理：发布前决定策略——打包后端到安装包，或要求用户单独安装

## 3. SettingsLoader.FindTemplate 找错文件

- 现状：向上遍历父目录找 `settings.ini`，但从未引用 `settings.ini.template`
- 影响：模板机制实际失效
- 处理：改为找 `settings.ini.template` 并复制到程序目录为 `settings.ini`

## 4. 其他待办

- （后续追加）

## 5. 第 7 步现状核查（2026-09-24，只增）

- **第 1–3 项现状不变**；第 7 步裁决 **D3：只列不改**，发布前单独一步处理。
  - 路径硬编码：`PythonProcessManager.cs:22-24`、`Shell/SettingsLoader.cs:12-14`、
    `settings.ini.template` 的 `[backend]` 段仍指向开发机路径。
  - `publish.ps1` 仍只发布 C# App，不带 ComfyUI / python。
  - `SettingsLoader.FindTemplate()` 仍向上找 `settings.ini`，未引用 `settings.ini.template`。
- **新增参考（7-H）**：project 流程下沉 `App/Flows/ProjectFlowRunner` 的前置条件见
  `DOC/FROZEN.md`「模块边界迁移 · 第 7 步」7D2 段（本文件不改既有 1–4 段）。

## 6. TE-Speed 外置加速模块（2026-09-25，只增）

- **语义**：外置模块，**不 vendor、不进发布包**（第三方闭源 `.pyd`、无 LICENSE，vendor 有法律风险）；
  默认关，缺失不致命。
- **保留**：加载机制（`config.TE_SPEED_*` / `pipeline.apply_te_speed`）+ 文档
  （`DOC/OPTIMIZATION.md` §1.7 的放置 / 开启 / 删除 / 更新说明）。
- **用户自取**：https://github.com/tl2012tl/TE-Speed-QwenImage21 →
  放到 `<ComfyUI>/custom_nodes/TE-Speed-QwenImage21/`，设 `ZIV_AI_TE_SPEED=1` 开启。
- **发布前核查**：确认发布包内**不含** `TE-Speed-QwenImage21/`（其位于 `.gitignore` 的 `Comfyui/` 下，
  且已在 `.gitignore` 显式排除）。
- 挂账：**Z-019**（见 `DOC/FROZEN.md`）。

## 7. WD14 Tagger 外置模块（2026-09-25，只增）

- **语义**：外置模块，**不进发布包**（节点 + ONNX 模型位于 `.gitignore` 的 `Comfyui/` 下）。
- **保留**：加载机制（`config.TAGGER_*` / `python/server/tagger.py`）+ 文档（`DOC/OPTIMIZATION.md` §8）。
- **依赖**：`python_embeded` 需装 `onnxruntime`（CPU）；模型 `wd-vit-tagger-v3` 由用户自备
  （放 `<ComfyUI>/custom_nodes/comfyui-wd14-tagger/models/`）。
- **发布前核查**：确认发布包内**不含** `comfyui-wd14-tagger/` 与模型。
- 挂账：**Z-020**（见 `DOC/FROZEN.md`）。

## 8. 模板系统用户覆盖（T2，2026-09-25，只增）

- **数据**：用户模板覆盖 `Template/commands.user.json`（程序目录，Z14）；缺失 = 无覆盖。
- **发布保留**：`publish.ps1` 清理旧产物时保留整个 `Template/` 目录，避免清掉用户覆盖；
  内置 `commands.json` / `loras.json` / `models.json` 由 `dotnet publish` 覆盖为最新。
- **发布前核查**：确认发布流程后 `Template/commands.user.json`（若存在）仍在。
- 挂账：**Z-021**（T5 UI / T3 接线，见 `DOC/FROZEN.md`）。
