@echo off
setlocal EnableDelayedExpansion
title DownloadMaster - Publish Standalone
cd /d "%~dp0"

set "ROOT=%~dp0"
set "PROJECT=%ROOT%DownloadMaster"
set "OUTPUT=%ROOT%publish"
set "TOOLS=%PROJECT%\tools"

echo.
echo  ============================================
echo   DownloadMaster - Publish Standalone (x64)
echo  ============================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install .NET 8 SDK.
    pause
    exit /b 1
)

REM --- Ensure bundled tools exist ---
if not exist "%TOOLS%\yt-dlp.exe" (
    echo yt-dlp.exe missing. Running setup-tools.bat...
    call "%ROOT%setup-tools.bat"
)

if not exist "%TOOLS%\ffmpeg\ffmpeg.exe" (
    echo FFmpeg missing. Running setup-tools.bat...
    call "%ROOT%setup-tools.bat"
)

REM --- Clean Debug ---
echo [1/4] Cleaning Debug output...
cd /d "%PROJECT%"
dotnet clean DownloadMaster.csproj -c Debug >nul 2>&1
if exist "bin\Debug" rmdir /s /q "bin\Debug"
if exist "obj\Debug" rmdir /s /q "obj\Debug"
if exist "obj\x64\Debug" rmdir /s /q "obj\x64\Debug"

REM --- Clean previous publish ---
echo [2/4] Cleaning previous publish folder...
cd /d "%ROOT%"
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"

REM --- Publish single-file exe ---
echo [3/4] Publishing Release x64 single-file exe...
cd /d "%PROJECT%"
dotnet publish DownloadMaster.csproj -c Release --no-incremental ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%OUTPUT%"

if errorlevel 1 (
    echo.
    echo ERROR: Publish failed.
    pause
    exit /b 1
)

REM --- Copy tools next to exe ---
echo [4/4] Copying tools (yt-dlp + FFmpeg)...
mkdir "%OUTPUT%\tools\ffmpeg" 2>nul
copy /Y "%TOOLS%\yt-dlp.exe" "%OUTPUT%\tools\" >nul
copy /Y "%TOOLS%\ffmpeg\ffmpeg.exe" "%OUTPUT%\tools\ffmpeg\" >nul
copy /Y "%TOOLS%\ffmpeg\ffprobe.exe" "%OUTPUT%\tools\ffmpeg\" >nul
copy /Y "%TOOLS%\README.txt" "%OUTPUT%\tools\" >nul 2>nul

echo.
echo  ============================================
echo   Publish complete!
echo  ============================================
echo.
echo   Folder:  %OUTPUT%
echo   Run:     %OUTPUT%\DownloadMaster.exe
echo.
echo   Contents:
echo     DownloadMaster.exe   (standalone, no .NET install needed)
echo     tools\yt-dlp.exe
echo     tools\ffmpeg\ffmpeg.exe
echo     tools\ffmpeg\ffprobe.exe
echo.
echo   You can copy the entire "publish" folder anywhere.
echo.

if /I "%~1"=="--no-pause" exit /b 0
pause
