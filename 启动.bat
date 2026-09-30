@echo off
setlocal
rem 如需开发机统一到便携版 settings.ini，取消下一行注释并改成你的便携版路径：
rem set "ZIV_AI_SETTINGS_PATH=D:\Program Files\ZIV\ZIV.AI\settings.ini"
set "APP=%~dp0src\ZivAiEditor.App\bin\Release\net8.0-windows\win-x64\ZivAiEditor.App.exe"
if not exist "%APP%" set "APP=%~dp0src\ZivAiEditor.App\bin\Debug\net8.0-windows\win-x64\ZivAiEditor.App.exe"
if not exist "%APP%" (
  echo [ERROR] ZivAiEditor.App.exe not found.
  echo Build first: dotnet build src\ZIV.AI.sln -c Release
  pause
  exit /b 1
)
start "" "%APP%"
