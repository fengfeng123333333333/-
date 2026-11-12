@echo off
REM Run unit/smoke tests through build\dotnet.cmd so the repo-local SDK is used.
REM Usage: build\test.cmd             run all tests
REM        build\test.cmd -v n        list every test name
REM NOTE: keep this file pure ASCII (see dotnet.cmd for why).
setlocal
call "%~dp0dotnet.cmd" test "%~dp0..\tests\FbAuto.Tests\FbAuto.Tests.csproj" --nologo %*
exit /b %ERRORLEVEL%
