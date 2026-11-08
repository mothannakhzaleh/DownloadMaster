@echo off
REM DownloadMaster - build and start the app.
REM   run.bat            normal run (your real settings)
REM   run.bat --sandbox  test run: settings + downloads go to test-output\sandbox (never your Downloads)
setlocal
for %%I in ("%~dp0..") do set "ROOT=%%~fI"

call "%~dp0build.bat" --no-pause
if errorlevel 1 (
    pause
    exit /b 1
)

set "EXE=%ROOT%\DownloadMaster\bin\Release\net10.0\DownloadMaster.exe"
if not exist "%EXE%" (
    echo ERROR: Built exe not found: %EXE%
    pause
    exit /b 1
)

if /I "%~1"=="--sandbox" (
    set "DOWNLOADMASTER_DATA_DIR=%ROOT%\test-output\sandbox"
    echo Sandbox: %ROOT%\test-output\sandbox
)

echo Starting DownloadMaster...
start "" "%EXE%"
exit /b 0
