**Last updated:** {{DATE}}

## DownloadMaster {{VERSION}}

### What's new

#### Cross-platform — Windows, macOS and Linux

- New Avalonia UI runs on Windows 10+, macOS (Apple Silicon and Intel) and Linux x64
- Release packages are built and published automatically by GitHub Actions
- Standalone: one executable plus `tools/` (yt-dlp + FFmpeg) and `browser-extension/` — nothing else to install

#### Browser extension (Chrome, Edge, Brave, Opera)

- Catches browser downloads and hands them to DownloadMaster with your cookies, referrer and user agent
- "New download caught" window, even while the app is in the tray
- Download button on videos: send the page (best quality) or a detected HLS / DASH / MP4 stream
- Install it from the app: tray menu → **Install browser extension…**

#### IDM-style download engine

- Dynamic segment splitting across up to 16 connections, with a per-connection progress bar
- Crash-safe resume, refresh an expired link from the browser, global speed limiter, SHA-256 after download
- Batch downloads (`part_[01-20].rar`), category folders, scheduler, clipboard monitor
- "When all downloads finish": sleep, shut down or quit

#### App

- Closes to the tray / menu bar; single instance; optional start with the OS
- Convert tabs gained every MediaConvertor feature (folder batches, PNG palette tuning, texture keying, ringtone/AMR fixes, cancel)

### Downloads

| Platform | File |
|----------|------|
| Windows 10/11 x64 | `DownloadMaster-{{VERSION}}-win-x64.zip` |
| macOS Apple Silicon (M1+) | `DownloadMaster-{{VERSION}}-osx-arm64.zip` |
| macOS Intel | `DownloadMaster-{{VERSION}}-osx-x64.zip` |
| Linux x64 | `DownloadMaster-{{VERSION}}-linux-x64.tar.gz` |
| Browser extension | `DownloadMaster-{{VERSION}}-browser-extension.zip` (also bundled with the app) |

`SHA256SUMS.txt` lists checksums for every file. No .NET runtime is required.

### Install

- **Windows** — extract the zip and run `DownloadMaster.exe`.
- **macOS** — unzip and drag `DownloadMaster.app` to Applications. The app is not notarized: the first time, right-click → **Open** (macOS 15+: **System Settings → Privacy & Security → Open Anyway**), or run `xattr -dr com.apple.quarantine /Applications/DownloadMaster.app`. If FFmpeg is reported missing: `brew install ffmpeg yt-dlp`.
- **Linux** — `tar -xzf DownloadMaster-{{VERSION}}-linux-x64.tar.gz` and run `./DownloadMaster` inside the extracted folder.

Bundled **yt-dlp** and **FFmpeg** are included under `tools/`.
