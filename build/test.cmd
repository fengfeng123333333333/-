@echo off
REM 跑单元/冒烟测试。走 build\dotnet.cmd 统一本地 SDK，避免 PATH 里没有 dotnet。
REM 用法：build\test.cmd            全部测试
REM       build\test.cmd -v n       看每条用例名
setlocal
call "%~dp0dotnet.cmd" test "%~dp0..\tests\FbAuto.Tests\FbAuto.Tests.csproj" --nologo %*
exit /b %ERRORLEVEL%
