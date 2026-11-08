@echo off
REM DownloadMaster - run every regression test project in tests\.
REM Test files are written to test-output\ (never your Downloads or Videos folders).
setlocal EnableDelayedExpansion
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
cd /d "%ROOT%"

set "DOWNLOADMASTER_DATA_DIR=%ROOT%\test-output\sandbox"
set "FAILED=0"

for /d %%D in ("%ROOT%\tests\*") do (
    if exist "%%D\*.csproj" (
        echo.
        echo === %%~nxD ===
        dotnet run --project "%%D" -c Release
        if errorlevel 1 set "FAILED=1"
    )
)

echo.
if "%FAILED%"=="1" (
    echo Some tests FAILED.
    if /I not "%~1"=="--no-pause" pause
    exit /b 1
)
echo All tests passed.
if /I not "%~1"=="--no-pause" pause
exit /b 0
