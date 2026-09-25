#requires -Version 7
<#
.SYNOPSIS
  发布 ZIV.AI 便携版到固定目录（铁律 Z16 / D-9）。

.DESCRIPTION
  正式便携版一律发布到固定目录，使文件关联 / 默认程序注册表里记录的 exe 绝对路径保持稳定，
  避免每次换目录都要重新注册并改系统设置。
  发布前清理旧构建产物，但保留用户状态（settings.ini / _cache / Template）与关联辅助脚本（*.bat）。

.PARAMETER OutputDir
  发布目录。默认 D:\Program Files\ZIV.AI；也可用环境变量 ZIV_AI_PUBLISH_DIR 覆盖。
#>
param(
    [string]$OutputDir = $(if ($env:ZIV_AI_PUBLISH_DIR) { $env:ZIV_AI_PUBLISH_DIR } else { 'D:\Program Files\ZIV.AI' })
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\ZivAiEditor.App\ZivAiEditor.App.csproj'

Write-Host '== ZIV.AI portable publish ==' -ForegroundColor Cyan
Write-Host "target: $OutputDir"

$exe = Join-Path $OutputDir 'ZivAiEditor.App.exe'
if (Test-Path $exe) {
    try {
        $stream = [System.IO.File]::Open($exe, 'Open', 'ReadWrite', 'None')
        $stream.Close()
    }
    catch {
        throw "ZivAiEditor.App.exe 正在运行，无法发布。请先关闭 ZIV.AI 再重试。"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# 清理旧构建产物；保留用户状态（settings.ini / _cache / Template）与关联脚本（*.bat）。
# 保留整个 Template/ 目录，使模板系统 T2 的用户覆盖 Template/commands.user.json 不被发布清掉；
# 内置 commands.json / loras.json / models.json 仍由 dotnet publish 的内容项覆盖为最新。
Get-ChildItem -LiteralPath $OutputDir -Force | Where-Object {
    $_.Name -ne 'settings.ini' -and $_.Name -ne '_cache' -and $_.Name -ne 'Template' -and $_.Extension -ne '.bat'
} | Remove-Item -Recurse -Force

# Publish without debug symbols: a NativeAOT Release build otherwise emits a large .pdb
# (plus package-shipped native symbols). DebugType=None suppresses the managed symbols;
# the residual *.pdb (native package symbols) are deleted as a backstop.
dotnet publish $project -c Release -o $OutputDir -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Get-ChildItem -LiteralPath $OutputDir -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "done: $(Join-Path $OutputDir 'ZivAiEditor.App.exe')" -ForegroundColor Green
