@echo off
REM ==========================================================================
REM  NOTE: GitHub Actions is now the main release path.
REM  Tag and push to build Windows, Linux and macOS packages in the cloud:
REM      git tag vX.Y.Z
REM      git push origin vX.Y.Z
REM  (see .github/workflows/release.yml). This script is a local, Windows-only
REM  fallback that uploads a single win-x64 zip.
REM ==========================================================================
setlocal EnableDelayedExpansion
title DownloadMaster - GitHub Release
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
cd /d "%ROOT%"

set "ARTIFACTS=artifacts"
set "PUBLISH=ship\win-x64"
set "CSPROJ=DownloadMaster\DownloadMaster.csproj"
set "NOTES_TEMPLATE=docs\release-notes-template.md"
set "AUTO_YES=0"
set "VERSION_OVERRIDE="

:parse_args
if "%~1"=="" goto args_done
if /I "%~1"=="--yes" (
    set "AUTO_YES=1"
    shift
    goto parse_args
)
if /I "%~1"=="--no-pause" (
    set "NO_PAUSE=1"
    shift
    goto parse_args
)
set "VERSION_OVERRIDE=%~1"
shift
goto parse_args

:args_done

echo.
echo  ============================================
echo   DownloadMaster - GitHub Release Upload
echo  ============================================
echo.
echo  NOTE: the main release path is GitHub Actions - push a tag vX.Y.Z
echo        to build Windows, Linux and macOS packages in the cloud.
echo        This script uploads a local win-x64 build only.
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
    echo ERROR: .NET SDK not found. Install the .NET 10 SDK.
    pause
    exit /b 1
)

if not exist "%CSPROJ%" (
    echo ERROR: Project file not found: %CSPROJ%
    pause
    exit /b 1
)

if defined VERSION_OVERRIDE (
    set "VERSION=%VERSION_OVERRIDE%"
    echo Using manual version override: %VERSION%
) else (
    echo Detecting version from %CSPROJ% ...
    for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command ^
        "$csproj = '%CSPROJ%';" ^
        "$xml = [xml](Get-Content -LiteralPath $csproj);" ^
        "$v = $xml.Project.PropertyGroup.Version;" ^
        "if ([string]::IsNullOrWhiteSpace($v)) { $v = '1.0.0' };" ^
        "function Test-Tag([string]$ver) {" ^
        "  $tag = 'v' + $ver;" ^
        "  & gh release view $tag 2>$null | Out-Null;" ^
        "  return ($LASTEXITCODE -eq 0)" ^
        "};" ^
        "while (Test-Tag $v) {" ^
        "  Write-Host ('Tag v' + $v + ' already exists on GitHub — bumping patch...') -ForegroundColor Yellow;" ^
        "  $p = $v.Split('.');" ^
        "  if ($p.Length -lt 3) { $p += @('0','0') };" ^
        "  $p[2] = [string]([int]$p[2] + 1);" ^
        "  $v = ($p[0..2] -join '.');" ^
        "};" ^
        "$xml.Project.PropertyGroup.Version = $v;" ^
        "$xml.Save((Resolve-Path -LiteralPath $csproj).Path);" ^
        "Write-Output $v"`) do set "VERSION=%%V"
)

if not defined VERSION set "VERSION=1.0.0"

set "TAG=v%VERSION%"
set "ZIP=%ARTIFACTS%\DownloadMaster-%TAG%-win-x64.zip"
set "NOTES=%ARTIFACTS%\release-notes-%TAG%.md"

echo Version : %VERSION%
echo Tag     : %TAG%
echo Output  : %ZIP%
echo.

if "%AUTO_YES%"=="0" (
    set /p CONFIRM=Continue with publish and GitHub release? [Y/N]:
    if /I not "!CONFIRM!"=="Y" (
        echo Cancelled.
        exit /b 0
    )
)

echo.
echo [1/5] Publishing standalone build...
call "%~dp0publish.bat" --no-pause
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
echo [2/5] Creating release zip...
if not exist "%ARTIFACTS%" mkdir "%ARTIFACTS%"
if exist "%ZIP%" del /f /q "%ZIP%"

powershell -NoProfile -Command "Compress-Archive -Path 'ship\win-x64\*' -DestinationPath '%ZIP%' -Force"
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
echo [3/5] Verifying GitHub release tag %TAG%...
gh release view "%TAG%" >nul 2>&1
if not errorlevel 1 (
    echo ERROR: Release %TAG% already exists. Bump Version in DownloadMaster.csproj or pass a new version:
    echo   scripts\release-github.bat 2.0.1
    pause
    exit /b 1
)

echo.
echo [4/5] Writing release notes...
if not exist "%NOTES_TEMPLATE%" (
    echo ERROR: Release notes template not found: %NOTES_TEMPLATE%
    pause
    exit /b 1
)

powershell -NoProfile -Command ^
    "$template = Get-Content -LiteralPath '%NOTES_TEMPLATE%' -Raw -Encoding UTF8;" ^
    "$date = Get-Date -Format 'MMMM d, yyyy';" ^
    "$notes = $template.Replace('{{DATE}}', $date).Replace('{{VERSION}}', 'v%VERSION%');" ^
    "$utf8 = New-Object System.Text.UTF8Encoding $false;" ^
    "[System.IO.File]::WriteAllText('%NOTES%', $notes, $utf8)"

echo.
echo [5/5] Creating GitHub release %TAG%...
gh release create "%TAG%" "%ZIP%" --title "DownloadMaster %TAG%" --notes-file "%NOTES%"
if errorlevel 1 (
    echo ERROR: GitHub release failed.
    pause
    exit /b 1
)

echo.
echo Cleaning local release artifacts...
if exist "%ZIP%" del /f /q "%ZIP%"
if exist "%NOTES%" del /f /q "%NOTES%"
if exist "%ARTIFACTS%" rmdir /s /q "%ARTIFACTS%" 2>nul

echo.
echo  ============================================
echo   Release uploaded successfully!
echo  ============================================
echo   Version : %VERSION%
echo   Tag     : %TAG%
echo   Cleaned : zip, release notes ^(ship\win-x64 kept^)
echo.
gh release view "%TAG%" --web 2>nul
echo.

if /I "%NO_PAUSE%"=="1" exit /b 0
if /I "%AUTO_YES%"=="1" exit /b 0
pause
