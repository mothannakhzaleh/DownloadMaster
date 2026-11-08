@echo off
setlocal
REM Downloads yt-dlp + FFmpeg into src\DownloadMaster\tools (bundled with every build).
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
cd /d "%ROOT%"
set "TOOLS=src\DownloadMaster\tools"
set "FFMPEG_DST=%TOOLS%\ffmpeg"
set "FFMPEG_ZIP_URL=https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"

echo.
echo  DownloadMaster - setup bundled tools (Windows)
echo  Copyright (c) 2026 mothannakh - https://satisfy.live/
echo  On Linux / macOS run scripts/setup-tools.sh instead.
echo.

if not exist "%TOOLS%" mkdir "%TOOLS%"
if not exist "%FFMPEG_DST%" mkdir "%FFMPEG_DST%"

if not exist "%TOOLS%\yt-dlp.exe" (
    echo Downloading yt-dlp.exe...
    powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile '%TOOLS%\yt-dlp.exe'"
    if errorlevel 1 (
        echo ERROR: Failed to download yt-dlp.exe
        pause
        exit /b 1
    )
) else (
    echo yt-dlp.exe already present.
)

if exist "%FFMPEG_DST%\ffmpeg.exe" if exist "%FFMPEG_DST%\ffprobe.exe" (
    echo FFmpeg already present in tools\ffmpeg\
    goto done
)

echo.
echo Downloading FFmpeg (BtbN win64 build, ~150 MB)...
powershell -NoProfile -Command ^
    "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue';" ^
    "$tmp = Join-Path $env:TEMP ('dm-ffmpeg-' + [guid]::NewGuid());" ^
    "New-Item -ItemType Directory -Path $tmp | Out-Null;" ^
    "try {" ^
    "  $zip = Join-Path $tmp 'ffmpeg.zip';" ^
    "  Invoke-WebRequest -Uri '%FFMPEG_ZIP_URL%' -OutFile $zip;" ^
    "  Expand-Archive -LiteralPath $zip -DestinationPath $tmp -Force;" ^
    "  foreach ($t in 'ffmpeg.exe','ffprobe.exe') {" ^
    "    $f = Get-ChildItem -LiteralPath $tmp -Recurse -Filter $t | Where-Object { $_.Directory.Name -eq 'bin' } | Select-Object -First 1;" ^
    "    if (-not $f) { throw ($t + ' not found in archive') };" ^
    "    Copy-Item -LiteralPath $f.FullName -Destination '%FFMPEG_DST%' -Force" ^
    "  }" ^
    "} finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }"
if errorlevel 1 (
    echo.
    echo ERROR: FFmpeg download failed. Download a Windows FFmpeg build and copy:
    echo   %FFMPEG_DST%\ffmpeg.exe
    echo   %FFMPEG_DST%\ffprobe.exe
    echo.
    echo Sources:
    echo   https://github.com/BtbN/FFmpeg-Builds/releases
    echo   https://www.gyan.dev/ffmpeg/builds/
    pause
    exit /b 1
)

:done
echo.
echo Tools folder:
dir /B "%TOOLS%" 2>nul
dir /B "%FFMPEG_DST%" 2>nul
echo.
if /I "%~1"=="--no-pause" exit /b 0
pause
