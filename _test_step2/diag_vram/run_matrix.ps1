# GPU matrix runner for the VRAM waveform measurement (observation only).
#
#   powershell -ExecutionPolicy Bypass -File _test_step2/diag_vram/run_matrix.ps1
#
# Preflight: aborts when the GPU is not idle (unless -Force). For each size it
# runs a cold and a hot submit; cold first stops leftover ZIV backends so the
# model really starts `not_loaded`. Finally it runs the CPU-only analyser.
param(
    [switch]$Force,
    [string]$Repo = 'D:\devlop\ZIV.AI',
    [int]$GpuIdleThresholdMiB = 2500
)

$ErrorActionPreference = 'Stop'
$outDir = Join-Path $Repo '_test_step2\diag_vram'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# --- Preflight: GPU must be idle (a stray backend breaks the cold baseline) ---
$used = [int](nvidia-smi --query-gpu=memory.used --format=csv,noheader,nounits | Select-Object -First 1)
Write-Host "GPU used = $used MiB (threshold $GpuIdleThresholdMiB)"
if ($used -gt $GpuIdleThresholdMiB -and -not $Force) {
    throw "GPU not idle ($used MiB used). Free VRAM or pass -Force."
}

# --- Build the runner (no GPU needed to build) ---
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

$sizes = 1024, 1536, 2048
foreach ($size in $sizes) {
    Stop-ZivBackends
    Start-Sleep -Seconds 2
    Write-Host "=== cold $size ==="
    & $exe --size $size --mode cold --run-id "${size}_cold"
    if ($LASTEXITCODE -ne 0) { throw "cold run failed for $size (exit $LASTEXITCODE)" }

    Write-Host "=== hot $size ==="
    & $exe --size $size --mode hot --run-id "${size}_hot"
    if ($LASTEXITCODE -ne 0) { throw "hot run failed for $size (exit $LASTEXITCODE)" }
}

Write-Host "=== analyse ==="
python (Join-Path $outDir 'plot.py') --dir $outDir
