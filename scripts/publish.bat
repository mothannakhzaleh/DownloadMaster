@echo off
REM DownloadMaster - build the ready-to-ship Windows app into ship\win-x64\
REM   ship\win-x64\DownloadMaster.exe      single file, .NET runtime included
REM   ship\win-x64\tools\                  yt-dlp + FFmpeg
REM   ship\win-x64\browser-extension\      Chrome / Edge extension (installed from the app)
REM   ship\win-x64\downloadmaster-cli.exe  command-line tool (when src\DownloadMaster.Cli exists)
REM Copy the whole win-x64 folder anywhere and run DownloadMaster.exe.
REM
REM Options:
REM   --portable   also write DownloadMaster.portable: the build keeps its settings and data in
REM                ship\win-x64\data instead of %AppData%\DownloadMaster
REM   --no-pause   don't wait for a key at the end (used by release-github.bat)
setlocal EnableDelayedExpansion
title DownloadMaster - Publish (ship)
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
cd /d "%ROOT%"

set "NO_PAUSE=0"
set "PORTABLE=0"
:parse_args
if "%~1"=="" goto args_done
if /I "%~1"=="--no-pause" (
    set "NO_PAUSE=1"
) else if /I "%~1"=="--portable" (
    set "PORTABLE=1"
) else (
    echo ERROR: unknown option %~1
    echo Usage: publish.bat [--portable] [--no-pause]
    exit /b 1
)
shift
goto parse_args
:args_done

set "RID=win-x64"
set "PROJECT=%ROOT%\src\DownloadMaster\DownloadMaster.csproj"
set "TOOLS=%ROOT%\src\DownloadMaster\tools"
set "OUTPUT=%ROOT%\ship\%RID%"
set "CLI_PROJECT=%ROOT%\src\DownloadMaster.Cli\DownloadMaster.Cli.csproj"
set "CLI_TMP=%ROOT%\ship\.cli-%RID%"

echo.
echo  ============================================
echo   DownloadMaster - Publish to ship\%RID%
echo  ============================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install the .NET 10 SDK.
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)

REM --- Bundled tools (downloaded once into src\DownloadMaster\tools) ---
set "NEED_TOOLS=0"
if not exist "%TOOLS%\yt-dlp.exe" set "NEED_TOOLS=1"
if not exist "%TOOLS%\ffmpeg\ffmpeg.exe" set "NEED_TOOLS=1"
if not exist "%TOOLS%\ffmpeg\ffprobe.exe" set "NEED_TOOLS=1"
if "%NEED_TOOLS%"=="1" (
    echo [1/4] Downloading yt-dlp and FFmpeg...
    call "%~dp0setup-tools.bat" --no-pause
    if errorlevel 1 (
        echo ERROR: Tool setup failed.
        if "%NO_PAUSE%"=="0" pause
        exit /b 1
    )
) else (
    echo [1/4] Tools already present.
)

REM --- Fresh output folder ---
echo [2/4] Cleaning ship\%RID% ...
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"

REM --- Publish single-file exe (tools + extension are copied beside it by the csproj) ---
echo [3/4] Publishing self-contained single-file exe...
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
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)

if exist "%OUTPUT%\tools\ffmpeg\.gitkeep" del /f /q "%OUTPUT%\tools\ffmpeg\.gitkeep"
copy /Y "%ROOT%\LICENSE" "%OUTPUT%\" >nul 2>nul
copy /Y "%ROOT%\THIRD_PARTY_LICENSES.md" "%OUTPUT%\" >nul 2>nul

REM --- Optional command-line tool: published to a scratch folder, then only the exe is copied
REM     next to the app so none of the app's files can be overwritten ---
if not exist "%CLI_PROJECT%" (
    echo [4/4] No command-line project in this checkout - skipped.
    goto cli_done
)
echo [4/4] Publishing downloadmaster-cli.exe ...
if exist "%CLI_TMP%" rmdir /s /q "%CLI_TMP%"
dotnet publish "%CLI_PROJECT%" -c Release -r %RID% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None ^
  -o "%CLI_TMP%"
if errorlevel 1 (
    echo.
    echo ERROR: Publishing the command-line tool failed.
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)
if not exist "%CLI_TMP%\downloadmaster-cli.exe" (
    echo ERROR: downloadmaster-cli.exe was not produced.
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)
copy /Y "%CLI_TMP%\downloadmaster-cli.exe" "%OUTPUT%\" >nul
rmdir /s /q "%CLI_TMP%"
:cli_done

if "%PORTABLE%"=="1" (
    > "%OUTPUT%\DownloadMaster.portable" echo DownloadMaster portable mode: settings and download state are kept in the "data" folder next to DownloadMaster.exe. Delete this file to use the user profile instead.
)

echo.
echo  ============================================
echo   Ready: %OUTPUT%
echo  ============================================
echo   DownloadMaster.exe      run this (nothing else to install)
echo   tools\                  yt-dlp + FFmpeg
echo   browser-extension\      tray menu ^> Install browser extension
if exist "%OUTPUT%\downloadmaster-cli.exe" echo   downloadmaster-cli.exe  command-line tool
if "%PORTABLE%"=="1" echo   DownloadMaster.portable portable mode: data is kept in %OUTPUT%\data
echo.
if "%NO_PAUSE%"=="0" pause
exit /b 0
