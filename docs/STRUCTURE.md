# Project Structure

Complete layout and architecture reference for **DownloadMaster**.

---

## Repository tree

```
VideoDownloader/
│
├── DownloadMaster.sln          # Visual Studio solution (Release | x64)
├── DownloadMaster.slnx         # Solution filter (optional)
├── Directory.Build.props       # Shared MSBuild: Release, x64, win-x64 RID
│
├── LICENSE                     # MIT — Copyright (c) 2026 mothannakh
├── THIRD_PARTY_LICENSES.md     # yt-dlp, FFmpeg, .NET, NuGet licenses
├── README.md                   # Project overview and quick start
├── .gitignore                  # Git ignore rules (binaries, build output)
│
├── docs/
│   ├── img1.png                # README screenshot (video tab)
│   ├── img2.png                # README screenshot (Instagram tab)
│   ├── release-notes-template.md
│   └── STRUCTURE.md            # This file
│
├── start.bat                   # Build Release x64 + run app
├── build.bat                   # Build Release x64 only
├── publish-standalone.bat      # Self-contained publish → publish/
├── release-github.bat          # Publish + zip + GitHub release
├── setup-tools.bat             # Download yt-dlp, verify FFmpeg in tools/
│
└── DownloadMaster/             # Main WPF project (.NET 8)
    ├── DownloadMaster.csproj
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / MainWindow.xaml.cs
    ├── SettingsWindow.xaml / SettingsWindow.xaml.cs
    │
    ├── Assets/
    │   ├── app.ico             # Window + exe icon
    │   └── app-icon.png        # Header logo
    │
    ├── Models/
    │   ├── AppSettings.cs      # User settings + VideoInfo DTOs
    │   ├── DownloadItem.cs     # Queue item (INotifyPropertyChanged)
    │   └── Enums.cs            # DownloadStatus, AppTheme, AppLanguage
    │
    ├── Services/
    │   ├── YtDlpService.cs     # yt-dlp process: fetch info, download, progress
    │   ├── DownloadManager.cs  # Queue, concurrency, retries
    │   ├── ToolLocator.cs      # Resolves bundled tools next to exe
    │   ├── SettingsService.cs  # Load/save %AppData%\DownloadMaster\settings.json
    │   ├── ThemeService.cs     # Dark / Light theme switching
    │   ├── LocalizationService.cs  # EN / AR strings
    │   ├── VideoFormatAnalyzer.cs  # Parse yt-dlp JSON → quality/format lists
    │   └── FormatHelpers.cs    # Duration, bytes, filename sanitization
    │
    ├── Styles/
    │   └── Controls.xaml         # Buttons, TextBox, ComboBox templates
    │
    ├── Themes/
    │   ├── DarkTheme.xaml
    │   └── LightTheme.xaml
    │
    └── tools/                    # Bundled external tools (copied to output)
        ├── README.txt
        ├── yt-dlp.exe            # Gitignored — run setup-tools.bat
        └── ffmpeg/
            ├── ffmpeg.exe        # Gitignored
            └── ffprobe.exe       # Gitignored
```

---

## Build output (gitignored)

```
DownloadMaster/bin/x64/Release/net8.0-windows/win-x64/
├── DownloadMaster.exe
├── DownloadMaster.dll
└── tools/                      # Copied from project tools/

publish/                          # Created by publish-standalone.bat
├── DownloadMaster.exe            # Single-file self-contained
└── tools/
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      MainWindow (WPF UI)                     │
│  URL input · Fetch · Download · Queue · Settings · Theme     │
└───────────────┬─────────────────────────────┬───────────────┘
                │                             │
        ┌───────▼────────┐            ┌───────▼────────┐
        │ DownloadManager │            │  YtDlpService  │
        │  queue · retry  │───────────▶│  subprocess    │
        └───────┬────────┘            └───────┬────────┘
                │                             │
        ┌───────▼────────┐            ┌───────▼────────┐
        │ DownloadItem   │            │   ToolLocator  │
        │  progress UI   │            │ tools\yt-dlp   │
        └────────────────┘            │ tools\ffmpeg   │
                                        └────────────────┘
```

### Data flow

1. User pastes URL → **Fetch** calls `YtDlpService.FetchInfoAsync` (`--dump-single-json`).
2. **VideoFormatAnalyzer** reads formats → sets quality/format dropdowns.
3. **Download** enqueues `DownloadItem` → **DownloadManager** runs yt-dlp with progress parsing.
4. On complete → **Open folder** / **Play** use `OutputPath` on the item.

### Settings persistence

| Path | Purpose |
|------|---------|
| `%AppData%\DownloadMaster\settings.json` | Theme, language, save path, concurrency |

### External processes

| Binary | Role |
|--------|------|
| `tools/yt-dlp.exe` | Metadata fetch + download |
| `tools/ffmpeg/ffmpeg.exe` | Merge/remux (via yt-dlp `--ffmpeg-location`) |
| `tools/ffmpeg/ffprobe.exe` | Probing (required alongside ffmpeg) |

---

## Key source files

| File | Responsibility |
|------|----------------|
| `YtDlpService.cs` | Builds yt-dlp CLI args, parses JSON and `[download]` progress |
| `DownloadManager.cs` | Semaphore for max concurrent downloads, retry loop |
| `ToolLocator.cs` | Resolves `AppContext.BaseDirectory/tools/` only |
| `MainWindow.xaml.cs` | Fetch/download handlers, playlist UI, open/play actions |
| `LocalizationService.cs` | String table for EN / AR |
| `Directory.Build.props` | Forces Release + x64 + win-x64 for all projects |

---

## Scripts reference

| Script | Steps |
|--------|-------|
| `setup-tools.bat` | Download yt-dlp; check FFmpeg in `tools/ffmpeg/` |
| `start.bat` | Clean Debug → `dotnet build -c Release` → launch exe |
| `build.bat` | Clean Debug → `dotnet build -c Release` |
| `publish-standalone.bat` | Clean → `dotnet publish` single-file → copy tools → `publish/` |

---

## GitHub upload checklist

- [ ] Run `setup-tools.bat` locally (tools stay gitignored)
- [ ] Confirm `LICENSE` and `THIRD_PARTY_LICENSES.md` are committed
- [ ] Do **not** commit `bin/`, `obj/`, `publish/`, `.vs/`, or large `.exe` files
- [ ] Update clone URL in `README.md` with your GitHub username
- [ ] Optional: add GitHub release with `publish/` zip for end users

---

## Author & links

**Copyright © 2026 mothannakh**

- [https://satisfy.live/](https://satisfy.live/)
- [https://www.youtube.com/@ToPSourceDevelopment](https://www.youtube.com/@ToPSourceDevelopment)
