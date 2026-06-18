@echo off
setlocal EnableDelayedExpansion
title DownloadMaster - GitHub Release
cd /d "%~dp0"

set "ARTIFACTS=artifacts"
set "PUBLISH=publish"
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
echo [2/5] Creating release zip...
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
echo [3/5] Verifying GitHub release tag %TAG%...
gh release view "%TAG%" >nul 2>&1
if not errorlevel 1 (
    echo ERROR: Release %TAG% already exists. Bump Version in DownloadMaster.csproj or pass a new version:
    echo   release-github.bat 1.1.1
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
    "$template = Get-Content -LiteralPath '%NOTES_TEMPLATE%' -Raw;" ^
    "$date = Get-Date -Format 'MMMM d, yyyy';" ^
    "$notes = $template.Replace('{{DATE}}', $date).Replace('{{VERSION}}', 'v%VERSION%');" ^
    "[System.IO.File]::WriteAllText('%NOTES%', $notes, (New-Object System.Text.UTF8Encoding $false))"

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
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
if exist "%ARTIFACTS%" rmdir /s /q "%ARTIFACTS%" 2>nul

echo.
echo  ============================================
echo   Release uploaded successfully!
echo  ============================================
echo   Version : %VERSION%
echo   Tag     : %TAG%
echo   Cleaned : zip, release notes, publish folder
echo.
gh release view "%TAG%" --web 2>nul
echo.

if /I "%NO_PAUSE%"=="1" exit /b 0
if /I "%AUTO_YES%"=="1" exit /b 0
pause
