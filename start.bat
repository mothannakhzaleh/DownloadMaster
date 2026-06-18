@echo off
setlocal
cd /d "%~dp0DownloadMaster"

echo Cleaning Debug output...
dotnet clean DownloadMaster.csproj -c Debug >nul 2>&1
if exist "bin\Debug" rmdir /s /q "bin\Debug"
if exist "obj\Debug" rmdir /s /q "obj\Debug"

echo Building Release x64...
dotnet build DownloadMaster.csproj -c Release --no-incremental
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)

set "EXE=%~dp0DownloadMaster\bin\x64\Release\net8.0-windows\win-x64\DownloadMaster.exe"
if not exist "%EXE%" (
    echo ERROR: Built exe not found:
    echo   %EXE%
    pause
    exit /b 1
)

echo Starting DownloadMaster...
start "" "%EXE%"
