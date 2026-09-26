@echo off
setlocal
set "APP=%~dp0src\ZivAiEditor.App\bin\Release\net8.0-windows\win-x64\ZivAiEditor.App.exe"
if not exist "%APP%" set "APP=%~dp0src\ZivAiEditor.App\bin\Debug\net8.0-windows\win-x64\ZivAiEditor.App.exe"
if not exist "%APP%" (
  echo [ERROR] ZivAiEditor.App.exe not found.
  echo Build first: dotnet build src\ZIV.AI.sln -c Release
  pause
  exit /b 1
)
start "" "%APP%"
