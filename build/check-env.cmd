@echo off
REM Environment self-check for a target machine (overseas cloud host or any PC
REM that should run the published build). Collects OS, timezone, Chrome,
REM BitBrowser, network reachability and app file integrity into env-report.txt.
REM
REM Usage: double-click, or run check-env.cmd in the app folder.
REM Keep this file pure ASCII. cmd.exe parses .cmd with the ANSI code page.
setlocal
set "HERE=%~dp0"

if not exist "%HERE%check-env.ps1" (
    echo [ERROR] check-env.ps1 is missing next to this file.
    echo         Copy both files into the app folder and try again.
    pause
    exit /b 1
)

echo Collecting environment info, please wait (about 1 minute)...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%check-env.ps1"

echo.
echo ---------------------------------------------------------------
echo Done. Send env-report.txt back (it is in this folder).
echo ---------------------------------------------------------------
pause
exit /b 0
