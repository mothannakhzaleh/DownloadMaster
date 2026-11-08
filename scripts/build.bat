@echo off
REM DownloadMaster - build the app (Release) for development.
REM Output: src\DownloadMaster\bin\Release\net10.0\DownloadMaster.exe
setlocal
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
cd /d "%ROOT%"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
    if /I not "%~1"=="--no-pause" pause
    exit /b 1
)

echo Building DownloadMaster (Release)...
dotnet build "src\DownloadMaster\DownloadMaster.csproj" -c Release
if errorlevel 1 (
    echo Build failed.
    if /I not "%~1"=="--no-pause" pause
    exit /b 1
)

echo.
echo Build OK: %ROOT%\src\DownloadMaster\bin\Release\net10.0\DownloadMaster.exe
if /I not "%~1"=="--no-pause" pause
exit /b 0
