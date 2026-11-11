@echo off
REM 启动调试版。本机只有 .NET 运行时、已把 SDK 解压在仓库外 .dotnet，
REM 因此必须显式指定 DOTNET_ROOT，否则 apphost 找不到运行时会静默退出（原版踩过这个坑）。
setlocal
set ROOT=%~dp0..
set DOTNET_ROOT=%ROOT%\.dotnet
set EXE=%ROOT%\build\out\FbAuto.Presentation\Debug\net10.0-windows\FbAuto.Presentation.exe

if not exist "%EXE%" (
    echo [错误] 未找到可执行文件，请先运行 build\dotnet.cmd build FbAuto.slnx
    exit /b 1
)

start "" "%EXE%"
