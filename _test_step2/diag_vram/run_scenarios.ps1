# GPU scenario runner (observation only): inpaint / multi4 / outpaint at 1536.
#
#   powershell -ExecutionPolicy Bypass -File _test_step2/diag_vram/run_scenarios.ps1
#
# Preflight: aborts when the GPU is not idle (unless -Force). Each scenario runs
# in a fresh process (cold) so the load phase is included, then the CPU-only
# analyser refreshes summary.md over every *.jsonl in the folder.
param(
    [switch]$Force,
    [string]$Repo = 'D:\devlop\ZIV.AI',
    [int]$GpuIdleThresholdMiB = 2500
)

$ErrorActionPreference = 'Stop'
$outDir = Join-Path $Repo '_test_step2\diag_vram'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$used = [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits | Select-Object -First 1)
Write-Host "GPU used = $used MiB (threshold $GpuIdleThresholdMiB)"
if ($used -gt $GpuIdleThresholdMiB -and -not $Force) {
    throw "GPU not idle ($used MiB used). Free VRAM or pass -Force."
}

$csproj = Join-Path $outDir 'DiagMatrix\DiagMatrix.csproj'
dotnet build $csproj -c Release | Out-Host
$exe = Join-Path $outDir 'DiagMatrix\bin\Release\net8.0-windows\DiagMatrix.exe'
if (-not (Test-Path $exe)) { throw "runner not built: $exe" }

function Stop-ZivBackends {
    Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
        Where-Object { $_.CommandLine -like '*python\server\main.py*' } |
        ForEach-Object {
            Write-Host "stopping leftover backend PID $($_.ProcessId)"
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}

$scenarios = @(
    @{ Name = 'inpaint';  RunId = 'inpaint_1536' },
    @{ Name = 'multi4';   RunId = 'multi4_1536' },
    @{ Name = 'outpaint'; RunId = 'outpaint_1536' }
)

foreach ($scenario in $scenarios) {
    Stop-ZivBackends
    Start-Sleep -Seconds 2
    Write-Host "=== $($scenario.Name) ($($scenario.RunId)) ==="
    & $exe --scenario $scenario.Name --size 1536 --run-id $scenario.RunId
    if ($LASTEXITCODE -ne 0) { throw "$($scenario.Name) failed (exit $LASTEXITCODE)" }
}

Write-Host "=== analyse ==="
python (Join-Path $outDir 'plot.py') --dir $outDir
