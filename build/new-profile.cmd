@echo off
rem ============================================================
rem  为指定账号生成独立 Chrome 配置目录与启动器
rem  用法：build\new-profile.cmd acc02
rem  产物：D:\FbProfiles\acc02\ 目录 + 启动acc02.cmd 双击脚本
rem ============================================================
setlocal
if "%~1"=="" (
    echo 用法: new-profile.cmd ^<账号别名^>     例如: new-profile.cmd acc02
    exit /b 1
)
set "ALIAS=%~1"
set "ROOT=D:\FbProfiles"
set "PROFILE=%ROOT%\%ALIAS%"
set "CHROME=C:\Program Files\Google\Chrome\Application\chrome.exe"

if not exist "%CHROME%" (
    echo [ERROR] 未找到 Chrome: %CHROME%
    exit /b 1
)
if not exist "%PROFILE%" mkdir "%PROFILE%"

> "%ROOT%\启动%ALIAS%.cmd" (
    echo @echo off
    echo rem %ALIAS% 独立 Chrome 配置（与日常号隔离）
    echo start "" "%CHROME%" --user-data-dir="%PROFILE%" --no-first-run --no-default-browser-check --new-window "https://www.facebook.com/"
)

echo [OK] 配置目录：%PROFILE%
echo [OK] 启动脚本：%ROOT%\启动%ALIAS%.cmd
echo      双击后登录 Facebook，登录完成关闭窗口即可。
