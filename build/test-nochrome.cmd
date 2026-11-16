@echo off
REM Run only the tests that do NOT need a real Chrome.
REM
REM Why this exists: the Chrome-driven integration suite (7 classes) occasionally
REM stalls inside Playwright under a full run. Until that is fixed, this script
REM gives a fast, deterministic green run for everything else.
REM
REM Chrome-needing classes are excluded by name. Add new ones to the filter below.
REM Use build\test.cmd to run the whole suite (Chrome included) again.
REM NOTE: keep this file pure ASCII. cmd.exe parses .cmd with the ANSI code page,
REM       UTF-8 Chinese breaks the line and yields "not recognized as a command".
setlocal
set FILTER=FullyQualifiedName!~ChromeChannelIntegrationTests^&FullyQualifiedName!~PageFlowIntegrationTests^&FullyQualifiedName!~PlaywrightPageInteropIntegrationTests^&FullyQualifiedName!~ProfileEditFlowIntegrationTests^&FullyQualifiedName!~TaskQueueRunnerIntegrationTests^&FullyQualifiedName!~MessageDeleteFlowIntegrationTests^&FullyQualifiedName!~VideoUploadFlowIntegrationTests
call "%~dp0dotnet.cmd" test "%~dp0..\tests\FbAuto.Tests\FbAuto.Tests.csproj" --nologo --no-build --filter "%FILTER%" %*
exit /b %ERRORLEVEL%
