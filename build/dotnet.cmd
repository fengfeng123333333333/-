@echo off
REM 统一走仓库外的本地 SDK（.dotnet），不依赖系统 PATH 里的 dotnet。
REM 用法：build\dotnet.cmd --version
REM       build\dotnet.cmd build FbAuto.sln
setlocal
set "DOTNET_ROOT=%~dp0..\..\.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
if not exist "%DOTNET_ROOT%\dotnet.exe" (
    echo [ERROR] 本地 SDK 不存在：%DOTNET_ROOT%\dotnet.exe
    echo         请先执行 build\setup-sdk.cmd
    exit /b 1
)
"%DOTNET_ROOT%\dotnet.exe" %*
