@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul

rem One-click publish for ZIV.AI portable.
rem Calls PowerShell 7 (publish.ps1 requires v7). The default target
rem D:\Program Files\ZIV\ZIV.AI needs administrator rights to write.

set "PS=pwsh"
where pwsh >nul 2>nul
if not errorlevel 1 goto run
if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "PS=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not exist "%ProgramFiles%\PowerShell\7\pwsh.exe" goto nops

:run
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*
set "CODE=%ERRORLEVEL%"
echo.
if "%CODE%"=="0" echo [OK] Publish finished.
if not "%CODE%"=="0" echo [FAILED] publish.ps1 exit code %CODE%  -  if this is an access error, right-click this file and choose "Run as administrator".
pause
exit /b %CODE%

:nops
echo [ERROR] PowerShell 7 ^(pwsh^) not found. Install it first: https://aka.ms/powershell
pause
exit /b 1
