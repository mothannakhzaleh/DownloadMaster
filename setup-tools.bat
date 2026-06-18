@echo off
setlocal
cd /d "%~dp0"
set "TOOLS=DownloadMaster\tools"
set "FFMPEG_DST=%TOOLS%\ffmpeg"

echo.
echo  DownloadMaster - setup bundled tools
echo  Copyright (c) 2026 mothannakh - https://satisfy.live/
echo.

if not exist "%TOOLS%" mkdir "%TOOLS%"
if not exist "%FFMPEG_DST%" mkdir "%FFMPEG_DST%"

if not exist "%TOOLS%\yt-dlp.exe" (
    echo Downloading yt-dlp.exe...
    powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile '%TOOLS%\yt-dlp.exe'"
    if errorlevel 1 (
        echo ERROR: Failed to download yt-dlp.exe
        pause
        exit /b 1
    )
) else (
    echo yt-dlp.exe already present.
)

if not exist "%FFMPEG_DST%\ffmpeg.exe" (
    echo.
    echo FFmpeg not found in %FFMPEG_DST%
    echo.
    echo Download a Windows FFmpeg build and copy these files here:
    echo   %FFMPEG_DST%\ffmpeg.exe
    echo   %FFMPEG_DST%\ffprobe.exe
    echo.
    echo Recommended source:
    echo   https://www.gyan.dev/ffmpeg/builds/
    echo   https://github.com/BtbN/FFmpeg-Builds/releases
    echo.
) else (
    echo FFmpeg already present in tools\ffmpeg\
)

echo.
echo Tools folder:
dir /B "%TOOLS%" 2>nul
dir /B "%FFMPEG_DST%" 2>nul
echo.
pause
