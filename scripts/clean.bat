@echo off
REM DownloadMaster - remove build output (bin, obj), test-output and ship.
REM Bundled tools in src\DownloadMaster\tools are kept.
REM   clean.bat --yes   skip the question
setlocal EnableDelayedExpansion
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
cd /d "%ROOT%"

echo This deletes the bin\ and obj\ folders, test-output\ and ship\ in %ROOT%
if /I not "%~1"=="--yes" (
    set /p CONFIRM=Continue? [Y/N]:
    if /I not "!CONFIRM!"=="Y" exit /b 0
)

for %%P in (src\DownloadMaster tests\ImageConversion.Regression tests\DownloadEngine.Regression) do (
    if exist "%ROOT%\%%P\bin" rmdir /s /q "%ROOT%\%%P\bin"
    if exist "%ROOT%\%%P\obj" rmdir /s /q "%ROOT%\%%P\obj"
)
if exist "%ROOT%\test-output" rmdir /s /q "%ROOT%\test-output"
if exist "%ROOT%\ship" rmdir /s /q "%ROOT%\ship"
echo Clean.
exit /b 0
