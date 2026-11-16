@echo off
REM Build a self-contained win-x64 folder that can be copied as-is to another
REM Windows machine (an overseas cloud host, a colleague's PC, ...).
REM Self-contained = the target machine does NOT need the .NET 10 runtime.
REM
REM Output: build\dist\FbAuto
REM Chrome is NOT bundled: the app launches the locally installed chrome.exe
REM (see ChromeLocator). Install Chrome on the target machine.
REM license.dat is NOT bundled: the credential is encrypted with a key derived
REM from the machine code, so it would fail to decrypt elsewhere. Activate on
REM the target machine with a license code instead.
REM
REM Usage: build\publish.cmd
REM NOTE: keep this file pure ASCII. cmd.exe parses .cmd with the ANSI code page,
REM       UTF-8 Chinese breaks the line and yields "not recognized as a command".
setlocal
set "DIST=%~dp0dist\FbAuto"

if exist "%DIST%" (
    echo [1/3] Cleaning %DIST%
    rmdir /s /q "%DIST%"
)

echo [2/3] Publishing (self-contained win-x64, Release)
call "%~dp0dotnet.cmd" publish "%~dp0..\src\FbAuto.Presentation\FbAuto.Presentation.csproj" ^
    -c Release -r win-x64 --self-contained true ^
    -p:DebugType=none -p:DebugSymbols=false ^
    -o "%DIST%"
if errorlevel 1 (
    echo [ERROR] Publish failed.
    exit /b 1
)

echo [3/3] Copying helper files
if exist "%~dp0..\docs\accounts.sample.csv" copy /y "%~dp0..\docs\accounts.sample.csv" "%DIST%\accounts.sample.csv" >nul
if exist "%~dp0..\docs\accounts-HOWTO.md" copy /y "%~dp0..\docs\accounts-HOWTO.md" "%DIST%\accounts-HOWTO.md" >nul
if exist "%~dp0check-env.cmd" copy /y "%~dp0check-env.cmd" "%DIST%\check-env.cmd" >nul
if exist "%~dp0check-env.ps1" copy /y "%~dp0check-env.ps1" "%DIST%\check-env.ps1" >nul

echo.
echo [OK] %DIST%
echo      Copy that whole folder to the target machine and run FbAuto.Presentation.exe
exit /b 0
