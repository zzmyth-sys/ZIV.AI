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
