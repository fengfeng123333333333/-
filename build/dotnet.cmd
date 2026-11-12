@echo off
REM Use the repo-local SDK in ..\.dotnet instead of whatever is on PATH.
REM Usage: build\dotnet.cmd --version
REM        build\dotnet.cmd build FbAuto.slnx
REM NOTE: keep this file pure ASCII. cmd.exe parses .cmd files with the system
REM       ANSI code page; UTF-8 comments get shredded into bogus commands.
setlocal
set "DOTNET_ROOT=%~dp0..\..\.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
if not exist "%DOTNET_ROOT%\dotnet.exe" (
    echo [ERROR] Local SDK not found: %DOTNET_ROOT%\dotnet.exe
    echo         Run build\setup-sdk.cmd first.
    exit /b 1
)
"%DOTNET_ROOT%\dotnet.exe" %*
