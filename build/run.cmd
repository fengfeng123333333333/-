@echo off
REM Launch the debug build.
REM This machine has only the .NET runtime; the SDK lives outside the repo in .dotnet,
REM so DOTNET_ROOT must be set explicitly or the apphost exits silently.
REM Two different roots: REPO (holds the exe) and SDK_ROOT (one level above REPO).
REM NOTE: keep this file pure ASCII (see dotnet.cmd for why).
setlocal
set REPO=%~dp0..
set SDK_ROOT=%~dp0..\..
set "DOTNET_ROOT=%SDK_ROOT%\.dotnet"
set "EXE=%REPO%\build\out\FbAuto.Presentation\Debug\net10.0-windows\FbAuto.Presentation.exe"

if not exist "%EXE%" (
    echo [ERROR] Executable not found. Run: build\dotnet.cmd build FbAuto.slnx
    exit /b 1
)

if not exist "%DOTNET_ROOT%\dotnet.exe" (
    echo [WARN] Local SDK not found: %DOTNET_ROOT%\dotnet.exe
    echo        Falling back to the system runtime. If the app exits immediately,
    echo        run build\setup-sdk.cmd first.
)

start "" "%EXE%"
