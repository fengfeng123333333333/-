@echo off
REM Restore the toolchain on a fresh machine: download the .NET 10 SDK and
REM unzip it into the .dotnet folder outside the repo.
REM No admin rights, nothing written to C:\Program Files, system .NET untouched.
REM Usage: build\setup-sdk.cmd
REM NOTE: keep this file pure ASCII (see dotnet.cmd for why).
setlocal
set "WS=%~dp0..\.."
set "VER=10.0.404"
set "ZIP=%WS%\.dotnet-sdk.zip"
set "DST=%WS%\.dotnet"

if exist "%DST%\dotnet.exe" (
    echo [OK] Local SDK already present, skipping download.
    "%DST%\dotnet.exe" --version
    exit /b 0
)

echo [1/3] Downloading .NET SDK %VER% ...
curl -L --max-time 900 -o "%ZIP%" "https://builds.dotnet.microsoft.com/dotnet/Sdk/%VER%/dotnet-sdk-%VER%-win-x64.zip"
if errorlevel 1 (
    echo [ERROR] Download failed. Behind a proxy, make sure builds.dotnet.microsoft.com is reachable.
    exit /b 1
)

echo [2/3] Checking size ...
for %%A in ("%ZIP%") do echo       %%~zA bytes

echo [3/3] Extracting to %DST% ...
powershell -NoProfile -Command "Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%DST%' -Force"
if errorlevel 1 (
    echo [ERROR] Extraction failed.
    exit /b 1
)

echo.
echo [OK] Done:
"%DST%\dotnet.exe" --version
