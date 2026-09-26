# /扩图 intermittent-failure probe (read-only; run on the real machine).
#
# Copies one run's outpaint mask + crop canvas out of the app's _cache and prints
# structural stats, so two runs (effective / not effective) can be diffed offline.
# It changes nothing in the app.
#
# Usage (effective run first, then the not-effective run):
#   pwsh -File _test_step2\outpaint_probe.ps1 -SessionId <id> -NodeId <id> -Label run1
#   pwsh -File _test_step2\outpaint_probe.ps1 -SessionId <id> -NodeId <id> -Label run2
#
# SessionId / NodeId are visible in the app log when ZIV_AI_MASK_DIAG=1 is set, or in
# session.json under sessions\{SessionId}\.
param(
    [string]$BaseDir = "D:\devlop\ZIV.AI\src\ZivAiEditor.App\bin\Release\net8.0-windows\win-x64",
    [Parameter(Mandatory = $true)][string]$SessionId,
    [Parameter(Mandatory = $true)][string]$NodeId,
    [string]$OutDir = "E:\temp\opencode\outpaint_probe",
    [string]$Label = "run"
)

$py = "D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe"
$analyze = "D:\devlop\ZIV.AI\_test_step2\outpaint_probe_analyze.py"
$dst = Join-Path $OutDir $Label
New-Item -ItemType Directory -Force -Path $dst | Out-Null

$mask = Join-Path $BaseDir "_cache\masks\$SessionId\${NodeId}_outpaint.png"
$crop = Join-Path $BaseDir "_cache\crops\$SessionId\$NodeId.png"

foreach ($p in @($mask, $crop)) {
    if (Test-Path -LiteralPath $p) {
        Copy-Item -LiteralPath $p -Destination $dst -Force
        Write-Host "copied   $p"
    }
    else {
        Write-Host "MISSING  $p"
    }
}

Write-Host ""
& $py -s $analyze $mask $crop
Write-Host ""
Write-Host "Captured to $dst -- compare run1 vs run2 (mask present? grey coverage? soft?)."
Write-Host "Also record per run: UI resolution tier, attachment count, plan.Mask non-null (ZIV_AI_MASK_DIAG=1 log)."
