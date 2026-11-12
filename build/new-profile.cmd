@echo off
rem ============================================================
rem  Create an isolated Chrome profile folder + launcher for one account.
rem  Usage:  build\new-profile.cmd acc02
rem  Output: D:\FbProfiles\acc02\  and  start-acc02.cmd
rem  NOTE: keep this file pure ASCII (see dotnet.cmd for why).
rem        Generated launcher names must be ASCII too, otherwise the name
rem        written from a UTF-8 source gets mangled by the ANSI code page.
rem ============================================================
setlocal
if "%~1"=="" (
    echo Usage: new-profile.cmd ^<account-alias^>     e.g. new-profile.cmd acc02
    exit /b 1
)
set "ALIAS=%~1"
set "ROOT=D:\FbProfiles"
set "PROFILE=%ROOT%\%ALIAS%"
set "CHROME=C:\Program Files\Google\Chrome\Application\chrome.exe"

if not exist "%CHROME%" (
    echo [ERROR] Chrome not found: %CHROME%
    exit /b 1
)
if not exist "%PROFILE%" mkdir "%PROFILE%"

> "%ROOT%\start-%ALIAS%.cmd" (
    echo @echo off
    echo rem Isolated Chrome profile for %ALIAS%
    echo start "" "%CHROME%" --user-data-dir="%PROFILE%" --no-first-run --no-default-browser-check --new-window "https://www.facebook.com/"
)

echo [OK] Profile dir : %PROFILE%
echo [OK] Launcher    : %ROOT%\start-%ALIAS%.cmd
echo      Double-click it, log in to Facebook, then close the window.
