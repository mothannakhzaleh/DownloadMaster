@echo off
setlocal EnableDelayedExpansion
title DownloadMaster - GitHub Release
cd /d "%~dp0"

set "ARTIFACTS=artifacts"
set "PUBLISH=publish"

echo.
echo  ============================================
echo   DownloadMaster - GitHub Release Upload
echo  ============================================
echo.

where gh >nul 2>&1
if errorlevel 1 (
    echo ERROR: GitHub CLI ^(gh^) not found.
    echo Install from https://cli.github.com/ and run: gh auth login
    pause
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install .NET 8 SDK.
    pause
    exit /b 1
)

for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "(Select-Xml -Path 'DownloadMaster\DownloadMaster.csproj' -XPath '//Version').Node.InnerText"`) do set "VERSION=%%V"
if not defined VERSION set "VERSION=1.0.0"

if not "%~1"=="" set "VERSION=%~1"

set "TAG=v%VERSION%"
set "ZIP=%ARTIFACTS%\DownloadMaster-%TAG%-win-x64.zip"
set "NOTES=%ARTIFACTS%\release-notes-%TAG%.md"

echo Version : %VERSION%
echo Tag     : %TAG%
echo Output  : %ZIP%
echo.

set /p CONFIRM=Continue with publish and GitHub release? [Y/N]:
if /I not "%CONFIRM%"=="Y" (
    echo Cancelled.
    exit /b 0
)

echo.
echo [1/4] Publishing standalone build...
call publish-standalone.bat --no-pause
if errorlevel 1 (
    echo ERROR: Publish failed.
    pause
    exit /b 1
)

if not exist "%PUBLISH%\DownloadMaster.exe" (
    echo ERROR: Publish output not found: %PUBLISH%\DownloadMaster.exe
    pause
    exit /b 1
)

echo.
echo [2/4] Creating release zip...
if not exist "%ARTIFACTS%" mkdir "%ARTIFACTS%"
if exist "%ZIP%" del /f /q "%ZIP%"

powershell -NoProfile -Command "Compress-Archive -Path 'publish\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
    echo ERROR: Failed to create zip.
    pause
    exit /b 1
)

if not exist "%ZIP%" (
    echo ERROR: Zip was not created: %ZIP%
    pause
    exit /b 1
)

echo.
echo [3/4] Creating GitHub release %TAG%...
gh release view "%TAG%" >nul 2>&1
if not errorlevel 1 (
    echo ERROR: Release %TAG% already exists. Bump Version in DownloadMaster.csproj or pass a new version:
    echo   release-github.bat 1.0.1
    pause
    exit /b 1
)

(
echo Windows x64 standalone portable build.
echo.
echo Extract the zip and run DownloadMaster.exe. No .NET runtime required.
echo.
echo Includes bundled yt-dlp and FFmpeg in the tools folder.
) > "%NOTES%"

gh release create "%TAG%" "%ZIP%" --title "DownloadMaster %TAG%" --notes-file "%NOTES%"
if errorlevel 1 (
    echo ERROR: GitHub release failed.
    pause
    exit /b 1
)

echo.
echo [4/4] Cleaning local release artifacts...
if exist "%ZIP%" del /f /q "%ZIP%"
if exist "%NOTES%" del /f /q "%NOTES%"
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
if exist "%ARTIFACTS%" rmdir /s /q "%ARTIFACTS%" 2>nul

echo.
echo  ============================================
echo   Release uploaded successfully!
echo  ============================================
echo   Cleaned: zip, release notes, publish folder
echo.
gh release view "%TAG%" --web 2>nul
echo.
pause
