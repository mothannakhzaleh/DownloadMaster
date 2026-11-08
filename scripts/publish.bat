@echo off
REM DownloadMaster - build the ready-to-ship Windows app into ship\win-x64\
REM   ship\win-x64\DownloadMaster.exe      single file, .NET runtime included
REM   ship\win-x64\tools\                  yt-dlp + FFmpeg
REM   ship\win-x64\browser-extension\      Chrome / Edge extension (installed from the app)
REM Copy the whole win-x64 folder anywhere and run DownloadMaster.exe.
setlocal EnableDelayedExpansion
title DownloadMaster - Publish (ship)
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
cd /d "%ROOT%"

set "RID=win-x64"
set "PROJECT=%ROOT%\DownloadMaster\DownloadMaster.csproj"
set "TOOLS=%ROOT%\DownloadMaster\tools"
set "OUTPUT=%ROOT%\ship\%RID%"

echo.
echo  ============================================
echo   DownloadMaster - Publish to ship\%RID%
echo  ============================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install the .NET 10 SDK.
    if /I not "%~1"=="--no-pause" pause
    exit /b 1
)

REM --- Bundled tools (downloaded once into DownloadMaster\tools) ---
set "NEED_TOOLS=0"
if not exist "%TOOLS%\yt-dlp.exe" set "NEED_TOOLS=1"
if not exist "%TOOLS%\ffmpeg\ffmpeg.exe" set "NEED_TOOLS=1"
if not exist "%TOOLS%\ffmpeg\ffprobe.exe" set "NEED_TOOLS=1"
if "%NEED_TOOLS%"=="1" (
    echo [1/3] Downloading yt-dlp and FFmpeg...
    call "%~dp0setup-tools.bat" --no-pause
    if errorlevel 1 (
        echo ERROR: Tool setup failed.
        if /I not "%~1"=="--no-pause" pause
        exit /b 1
    )
) else (
    echo [1/3] Tools already present.
)

REM --- Fresh output folder ---
echo [2/3] Cleaning ship\%RID% ...
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"

REM --- Publish single-file exe (tools + extension are copied beside it by the csproj) ---
echo [3/3] Publishing self-contained single-file exe...
dotnet publish "%PROJECT%" -c Release -r %RID% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None ^
  -o "%OUTPUT%"
if errorlevel 1 (
    echo.
    echo ERROR: Publish failed.
    if /I not "%~1"=="--no-pause" pause
    exit /b 1
)

if exist "%OUTPUT%\tools\ffmpeg\.gitkeep" del /f /q "%OUTPUT%\tools\ffmpeg\.gitkeep"
copy /Y "%ROOT%\LICENSE" "%OUTPUT%\" >nul 2>nul
copy /Y "%ROOT%\THIRD_PARTY_LICENSES.md" "%OUTPUT%\" >nul 2>nul

echo.
echo  ============================================
echo   Ready: %OUTPUT%
echo  ============================================
echo   DownloadMaster.exe      run this (nothing else to install)
echo   tools\                  yt-dlp + FFmpeg
echo   browser-extension\      tray menu ^> Install browser extension
echo.
if /I not "%~1"=="--no-pause" pause
exit /b 0
