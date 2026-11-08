# Refreshes the README screenshots (docs\images\*.png) from the ship build.
# Runs the app sandboxed in test-output\screens, so your real settings and Downloads are untouched.
# Usage (PowerShell 7, Windows):  scripts\screenshots.ps1      (run scripts\publish.bat first)
param([string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class DmWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
}
"@

$images = Join-Path $Root "docs\images"
$sandbox = Join-Path $Root "test-output\screens"
# Fresh sandbox every run (it only ever holds this script's own test data).
if (Test-Path $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
New-Item -ItemType Directory -Force -Path $sandbox, $images | Out-Null
$env:DOWNLOADMASTER_DATA_DIR = $sandbox
$exe = Join-Path $Root "ship\win-x64\DownloadMaster.exe"
if (-not (Test-Path $exe)) { throw "Build not found: $exe (run scripts\publish.bat)" }
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 9

$UIA = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
function Find-Window([string]$name, [int]$tries = 20) {
  for ($i = 0; $i -lt $tries; $i++) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($UIA::ProcessIdProperty, $proc.Id)
    $w = $UIA::RootElement.FindAll($Tree::Children, $cond) | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1
    if ($w) { return $w }
    Start-Sleep -Milliseconds 500
  }
  throw "window '$name' not found"
}
function Find-Named($parent, [string]$name, $type) {
  $c1 = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $name)
  $c2 = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, $type)
  $parent.FindFirst($Tree::Descendants, (New-Object System.Windows.Automation.AndCondition($c1, $c2)))
}
function Select-Tab($win, [string]$name) {
  $tab = Find-Named $win $name ([System.Windows.Automation.ControlType]::TabItem)
  if (-not $tab) { throw "tab $name not found" }
  $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 900
}
function Click($win, [string]$name) {
  $b = Find-Named $win $name ([System.Windows.Automation.ControlType]::Button)
  if (-not $b) { throw "button $name not found" }
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 1200
}
function Capture($win, [string]$file) {
  # PrintWindow renders only this window's own pixels, so other apps on screen never end up in the image.
  $h = [IntPtr]$win.Current.NativeWindowHandle
  [DmWin]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 700
  $outer = New-Object DmWin+RECT
  [DmWin]::GetWindowRect($h, [ref]$outer) | Out-Null
  $frame = New-Object DmWin+RECT
  [DmWin]::DwmGetWindowAttribute($h, 9, [ref]$frame, 16) | Out-Null   # visible bounds without the shadow
  $full = New-Object System.Drawing.Bitmap ($outer.Right - $outer.Left), ($outer.Bottom - $outer.Top)
  $g = [System.Drawing.Graphics]::FromImage($full)
  $hdc = $g.GetHdc()
  [DmWin]::PrintWindow($h, $hdc, 2) | Out-Null   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($hdc); $g.Dispose()
  $crop = New-Object System.Drawing.Rectangle ($frame.Left - $outer.Left), ($frame.Top - $outer.Top), ($frame.Right - $frame.Left), ($frame.Bottom - $frame.Top)
  $bmp = $full.Clone($crop, $full.PixelFormat)
  $bmp.Save((Join-Path $images $file), [System.Drawing.Imaging.ImageFormat]::Png)
  $full.Dispose(); $bmp.Dispose()
  Write-Output "saved $file ($($crop.Width) x $($crop.Height))"
}

# Modal dialogs (Settings) are owned by the main window, so also look among its descendants.
function Find-Dialog($owner, [string]$name) {
  for ($i = 0; $i -lt 20; $i++) {
    try { return Find-Window $name 1 } catch { }
    $d = Find-Named $owner $name ([System.Windows.Automation.ControlType]::Window)
    if ($d) { return $d }
    Start-Sleep -Milliseconds 500
  }
  throw "dialog '$name' not found"
}

try {
  $main = Find-Window "DownloadMaster"
  [DmWin]::SetWindowPos([IntPtr]$main.Current.NativeWindowHandle, [IntPtr]::Zero, 40, 30, 1440, 960, 0x0040) | Out-Null
  Start-Sleep -Milliseconds 800

  Select-Tab $main "Video";      Capture $main "main-video.png"
  Select-Tab $main "Instagram";  Capture $main "main-instagram.png"
  Select-Tab $main "Files";      Capture $main "main-files.png"
  Select-Tab $main "Convert"
  Select-Tab $main "Video Convert"; Capture $main "convert-video.png"
  Select-Tab $main "Image Convert"; Capture $main "convert-image.png"
  Select-Tab $main "Audio Convert"; Capture $main "convert-audio.png"
  Select-Tab $main "Speech";        Capture $main "convert-speech.png"

  # Browser hand-off: the "New download caught" popup, then the queue with per-connection progress.
  $body = Join-Path $sandbox "caught.json"
  '{"url":"http://ipv4.download.thinkbroadband.com/100MB.zip","filename":"100MB.zip","fileSize":104857600,"referrer":"https://www.thinkbroadband.com/download","cookies":"session=demo","userAgent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"}' |
    Set-Content -NoNewline -Encoding utf8NoBOM $body
  curl.exe -s -X POST -H "Origin: chrome-extension://screenshot" -H "Content-Type: application/json" --data-binary "@$body" "http://127.0.0.1:45780/api/v1/download" | Out-Null
  $caught = Find-Window "New download caught"
  Capture $caught "browser-download-caught.png"
  Click $caught "Download"
  Select-Tab $main "Files"
  Start-Sleep -Seconds 3    # a few seconds in, so the per-connection bar shows partial progress
  Capture $main "download-queue.png"

  Click $main "Settings"
  $settings = Find-Dialog $main "Settings"
  Capture $settings "settings.png"
  Click $settings "Browser extension…"
  $ext = Find-Dialog $settings "Browser extension"
  Capture $ext "extension-installer.png"
  $ext.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  $settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  Start-Sleep -Milliseconds 800

  Click $main "Theme"
  Select-Tab $main "Video"
  Capture $main "main-video-light.png"
  Click $main "Theme"
}
finally {
  Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue
}
