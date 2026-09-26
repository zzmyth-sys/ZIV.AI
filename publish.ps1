#requires -Version 7
<#
.SYNOPSIS
  发布 ZIV.AI 便携版到固定目录（铁律 Z16 / D-9）。

.DESCRIPTION
  正式便携版一律发布到固定目录，使文件关联 / 默认程序注册表里记录的 exe 绝对路径保持稳定，
  避免每次换目录都要重新注册并改系统设置。
  默认发布到 ZIV 便携版根目录下的独立子目录 ZIV.AI\，与 ZIV.App.exe 分属不同目录
  （两者均读写同名 settings.ini / Template\，且 NativeAOT 原生 DLL 必须与各自 exe 同目录）。
  发布前清理旧构建产物，但保留用户状态（settings.ini / settings.ini.template / _cache / Template）
  与关联辅助脚本（*.bat），并隐藏 _cache。

.PARAMETER OutputDir
  发布目录。默认 D:\Program Files\ZIV\ZIV.AI；也可用环境变量 ZIV_AI_PUBLISH_DIR 覆盖。
#>
param(
    [string]$OutputDir = $(if ($env:ZIV_AI_PUBLISH_DIR) { $env:ZIV_AI_PUBLISH_DIR } else { 'D:\Program Files\ZIV\ZIV.AI' })
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\ZivAiEditor.App\ZivAiEditor.App.csproj'

Write-Host '== ZIV.AI portable publish ==' -ForegroundColor Cyan
Write-Host "target: $OutputDir"

$exe = Join-Path $OutputDir 'ZivAiEditor.App.exe'
if (Test-Path $exe) {
    # P1: probe with read-only access so a non-elevated session (e.g. Program Files ACL)
    # is not misreported as "running". Only a sharing violation (IOException) means the
    # app holds the exe; any other error bubbles up with its real cause.
    $stream = $null
    try {
        $stream = [System.IO.File]::Open(
            $exe, 'Open', [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] {
        throw "ZivAiEditor.App.exe 正在运行，无法发布。请先关闭 ZIV.AI 再重试。"
    }
    finally {
        if ($null -ne $stream) { $stream.Close() }
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# P2-1: warn (do NOT delete) when an existing settings.ini still holds dev-machine paths.
# Rule: a path key (python_exe / script / dit_path / te_path / vae_path) whose value starts
# with a dev prefix such as D:\devlop\.
$settingsIni = Join-Path $OutputDir 'settings.ini'
if (Test-Path -LiteralPath $settingsIni) {
    $devPrefixes = @('D:\devlop\')
    $devHit = $false
    foreach ($raw in (Get-Content -LiteralPath $settingsIni -ErrorAction SilentlyContinue)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line[0] -eq ';' -or $line[0] -eq '#') { continue }
        $eq = $line.IndexOf('=')
        if ($eq -le 0) { continue }
        $key = $line.Substring(0, $eq).Trim().ToLowerInvariant()
        if ($key -notin @('python_exe', 'script', 'dit_path', 'te_path', 'vae_path')) { continue }
        $value = $line.Substring($eq + 1).Trim()
        foreach ($prefix in $devPrefixes) {
            if ($value.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { $devHit = $true }
        }
    }
    if ($devHit) {
        Write-Warning "检测到 settings.ini 含开发机路径（D:\devlop\...）；发布包将保留此文件。如需清理请手动删除后重新发布。"
    }
}

# 清理旧构建产物；保留用户状态（settings.ini / settings.ini.template / _cache / Template）与关联脚本（*.bat）。
# 保留整个 Template/ 目录，使模板系统 T2 的用户覆盖 Template/commands.user.json 不被发布清掉。
Get-ChildItem -LiteralPath $OutputDir -Force | Where-Object {
    $_.Name -ne 'settings.ini' -and $_.Name -ne 'settings.ini.template' -and $_.Name -ne '_cache' -and $_.Name -ne 'Template' -and $_.Extension -ne '.bat'
} | Remove-Item -Recurse -Force

# P2-2: the built-in Template json files are copied with CopyToOutputDirectory=PreserveNewest,
# which only overwrites when the source is newer than the target. Delete the preserved copies
# first so the next publish is guaranteed to refresh them (Template/commands.user.json, the user
# override, is kept).
foreach ($builtinName in @('commands.json', 'loras.json', 'models.json')) {
    $builtinPath = Join-Path $OutputDir "Template\$builtinName"
    if (Test-Path -LiteralPath $builtinPath) {
        Remove-Item -LiteralPath $builtinPath -Force
    }
}

# Publish without debug symbols: a NativeAOT Release build otherwise emits a large .pdb
# (plus package-shipped native symbols). DebugType=None suppresses the managed symbols;
# the residual *.pdb (native package symbols) are deleted as a backstop.
& dotnet publish "$project" -c Release -o "$OutputDir" -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Get-ChildItem -LiteralPath $OutputDir -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force

# _cache 隐藏（保持可写；资源管理器默认不显示）。
$cache = Join-Path $OutputDir '_cache'
if (Test-Path $cache) {
    $item = Get-Item -LiteralPath $cache -Force
    $item.Attributes = $item.Attributes -bor [System.IO.FileAttributes]::Hidden
}

Write-Host "done: $(Join-Path $OutputDir 'ZivAiEditor.App.exe')" -ForegroundColor Green
