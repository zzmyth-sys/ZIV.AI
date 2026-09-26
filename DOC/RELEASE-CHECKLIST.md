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

## 9. 首次发布不带开发机路径（B2，2026-09-27，只增）

- **现状（B2 后）**：`ZivAiEditor.App.csproj` 不再拷贝仓库根 `settings.ini`；改为拷贝
  `settings.ini.template`。程序目录 `settings.ini` 由 `SettingsLoader.EnsurePresent` 首启播种。
- **发布前核查**：发布到全新目录后，确认发布目录内 `settings.ini` **不含** `D:\devlop\...` 开发机路径
  （方案甲下首次发布应**无** `settings.ini`，启动 App 后才由模板生成）。
- **发布前核查**：Python 进程环境含 `ZIV_AI_MODELS_REGISTRY` / `ZIV_AI_LORA_REGISTRY`，指向程序目录
  `Template/` 下的同名文件（Template 同源）。
- **发布前核查**：`publish.ps1` 仍保留已存在的用户 `settings.ini`（重复发布不覆盖用户配置）。

## 10. publish.ps1 修复记录（P1/P2/P3，2026-09-27，只增）

- **P1 锁检测**：`[System.IO.File]::Open` 改为 `FileAccess.Read` + `FileShare.None`；`catch` 收窄为
  `catch [System.IO.IOException]`（仅「共享冲突 = 文件被占用」），其它异常（`UnauthorizedAccessException`
  等）不再被误报为「正在运行」，原样冒泡暴露真实原因。
- **P2-1 stale settings.ini 警告（不删文件）**：发布前读 `<OutputDir>/settings.ini`，若路径键
  （`python_exe` / `script` / `dit_path` / `te_path` / `vae_path`）的值以 `D:\devlop\` 开头 →
  `Write-Warning`；**不删文件**。
  - **升级发布需手删**：已有发布目录若残留带开发机路径的 `settings.ini`，脚本只警告、不清理；
    如需干净发布，请**手动删除该 `settings.ini`** 后重新发布（首启会由模板重新播种）。
- **P2-2 内置 json 强制刷新**：csproj 以 `CopyToOutputDirectory=PreserveNewest` 拷贝，仅在源比目标新时覆盖；
  发布前显式删除 `<OutputDir>/Template/{commands,loras,models}.json` 以强制刷新
  （保留 `commands.user.json` 用户覆盖）。
- **P2-3 白名单**：清理保留条件新增 `settings.ini.template`，不再依赖「csproj 增量拷贝一定执行」。
- **P3 引号**：`dotnet publish` 改为 `& dotnet publish "$project" ... -o "$OutputDir" ...`（调用运算符 +
  参数双引号），消除含空格路径的分词风险。

## 11. 发布包含 ZIV 自有后端管线（2026-09-27，只增）

- **背景**：§2「不打包后端」指的是**第三方**部分（ComfyUI / `python_embeded` / 模型权重）；
  ZIV **自有**的后端管线（`python/server/*.py`，约 125 KB，第一方代码）**必须随发布**，
  否则用户拿不到后端（不能要用户自己开发）。
- **改动**：`ZivAiEditor.App.csproj` 新增内容项：
  `..\..\python\server\**\*.py` → `Link="python\server\%(RecursiveDir)%(Filename)%(Extension)"`，
  `Exclude="..\..\python\server\test_*.py"`，`CopyToOutputDirectory=PreserveNewest`。
- **效果**：构建 / 发布输出含 `python\server\main.py`（+ 运行时模块，排除 `test_*.py` / `__pycache__`），
  使 A9 默认 `[backend] script = <程序目录>\python\server\main.py` 指向真实文件。
- **仍由用户自取（第三方，写依赖说明即可）**：ComfyUI 便携版、`python_embeded`、模型权重。
- **发布前核查**：发布目录含 `python\server\main.py` 及运行时 `.py`；不含 `test_*.py`。
- **遗留（A10）**：`python/server/config.py:5 COMFY_ROOT` 仍硬编码开发机路径；用自取的 ComfyUI 时需可配
  （另开 Step）。

## 12. COMFY_ROOT 纳入设置（硬编码残留更新，2026-09-27，只增）

- **§1 路径硬编码状态更新**：`python/server/config.py:5 COMFY_ROOT` 已改为**可配**
  （`[backend] comfy_root` → env `ZIV_AI_COMFY_ROOT`，含 `python_exe` 反推回退）；
  开发默认值暂留（GitHub 收尾步统一清）。
- **仍未解**：`config.py:7 MODEL_ROOT`（`C:\AI\ComfyUI_PIC\...`）仍硬编码；`COMFY_ROOT` 开发默认值仍在。
- **发布前核查**：设置窗口可配「ComfyUI 目录」；无效 COMFY_ROOT 时 Python 报可读异常（非 ModuleNotFoundError）。
