@echo off
REM 启动调试版。本机只有 .NET 运行时、已把 SDK 解压在仓库外 .dotnet，
REM 因此必须显式指定 DOTNET_ROOT，否则 apphost 找不到运行时会静默退出（原版踩过这个坑）。
REM 两个根要分开：仓库根（放可执行文件）与 SDK 根（仓库外一层），
REM 与 build\dotnet.cmd 的 ..\..\.dotnet 保持一致，别写成同一个。
setlocal
set REPO=%~dp0..
set SDK_ROOT=%~dp0..\..
set "DOTNET_ROOT=%SDK_ROOT%\.dotnet"
set "EXE=%REPO%\build\out\FbAuto.Presentation\Debug\net10.0-windows\FbAuto.Presentation.exe"

if not exist "%EXE%" (
    echo [错误] 未找到可执行文件，请先运行 build\dotnet.cmd build FbAuto.slnx
    exit /b 1
)

if not exist "%DOTNET_ROOT%\dotnet.exe" (
    echo [警告] 未找到仓库内 SDK：%DOTNET_ROOT%\dotnet.exe
    echo        将回退到系统已安装的运行时；若程序闪退，请先执行 build\setup-sdk.cmd
)

start "" "%EXE%"
