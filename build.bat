@echo off
setlocal
cd /d "%~dp0DownloadMaster"

echo Cleaning Debug output...
dotnet clean DownloadMaster.csproj -c Debug >nul 2>&1
if exist "bin\Debug" rmdir /s /q "bin\Debug"
if exist "obj\Debug" rmdir /s /q "obj\Debug"

echo Building Release x64...
dotnet build DownloadMaster.csproj -c Release -r win-x64 --no-incremental
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)

echo Build OK: bin\x64\Release\net8.0-windows\win-x64\
pause
