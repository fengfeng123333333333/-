@echo off
REM 在新机器上恢复工具链：下载 .NET 10 SDK 并解压到仓库外的 .dotnet 目录。
REM 不需管理员权限，不写入 C:\Program Files，不影响系统已有 .NET。
REM 用法：build\setup-sdk.cmd
setlocal
set "WS=%~dp0..\.."
set "VER=10.0.404"
set "ZIP=%WS%\.dotnet-sdk.zip"
set "DST=%WS%\.dotnet"

if exist "%DST%\dotnet.exe" (
    echo [OK] 本地 SDK 已存在，跳过下载。
    "%DST%\dotnet.exe" --version
    exit /b 0
)

echo [1/3] 下载 .NET SDK %VER% ...
curl -L --max-time 900 -o "%ZIP%" "https://builds.dotnet.microsoft.com/dotnet/Sdk/%VER%/dotnet-sdk-%VER%-win-x64.zip"
if errorlevel 1 (
    echo [ERROR] 下载失败。若在代理环境下，请先确认代理可访问 builds.dotnet.microsoft.com
    exit /b 1
)

echo [2/3] 校验大小 ...
for %%A in ("%ZIP%") do echo       %%~zA 字节

echo [3/3] 解压到 %DST% ...
powershell -NoProfile -Command "Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%DST%' -Force"
if errorlevel 1 (
    echo [ERROR] 解压失败
    exit /b 1
)

echo.
echo [OK] 完成：
"%DST%\dotnet.exe" --version
