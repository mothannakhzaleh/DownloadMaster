# DownloadMaster

Native Windows video download manager — **one app, no Python/Node/CMD setup**.

Built with **.NET 8 WPF**, powered by bundled **yt-dlp** and **FFmpeg**.

---

## Author

**Copyright © 2026 [mothannakh](https://satisfy.live/)**

- Website: [https://satisfy.live/](https://satisfy.live/)
- YouTube: [@ToPSourceDevelopment](https://www.youtube.com/@ToPSourceDevelopment)

---

## License

| What | License |
|------|---------|
| DownloadMaster source code | [MIT License](LICENSE) |
| yt-dlp, FFmpeg, .NET, SQLite, etc. | See [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) |

This project is intended for **public open-source release**. Bundled tool binaries (yt-dlp, FFmpeg) are **not** included in Git — run `setup-tools.bat` locally before building.

---

## Features

- Download from YouTube, X/Twitter, and 1000+ sites via yt-dlp
- Fetch preview with auto-detected quality and format
- Download queue with progress, speed, and ETA
- Playlist support with selectable video list
- Per-download save folder picker
- **Open folder** and **Play** when a download completes
- Dark / Light themes
- English + Arabic (RTL) UI
- Standalone publish folder (single `.exe`, no .NET install required)

---

## Requirements

| Scenario | Requirement |
|----------|-------------|
| Run from source (`start.bat`) | Windows 10+, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Run published build | Windows 10+ only (self-contained) |

---

## Quick start

### 1. Clone the repository

```bash
git clone https://github.com/mothannakhzaleh/DownloadMaster.git
cd DownloadMaster
```

### 2. Install bundled tools (required once)

```bat
setup-tools.bat
```

This downloads **yt-dlp** and guides **FFmpeg** placement into:

```
DownloadMaster\tools\
├── yt-dlp.exe
└── ffmpeg\
    ├── ffmpeg.exe
    └── ffprobe.exe
```

### 3. Run

```bat
start.bat
```

Builds **Release x64** and launches the app.

---

## Build & publish

| Script | Purpose |
|--------|---------|
| `start.bat` | Clean Debug → build Release x64 → run |
| `build.bat` | Build Release x64 only |
| `publish-standalone.bat` | Self-contained single-file publish + copy tools |
| `setup-tools.bat` | Download yt-dlp / verify FFmpeg |
| `release-github.bat` | Publish, zip, and upload GitHub release tag |

### GitHub release

Requires [GitHub CLI](https://cli.github.com/) (`gh auth login`).

```bat
release-github.bat
```

Uses `Version` from `DownloadMaster\DownloadMaster.csproj` (e.g. `1.0.0` → tag `v1.0.0`). Optional override:

```bat
release-github.bat 1.0.1
```

Creates `artifacts\DownloadMaster-v1.0.0-win-x64.zip`, uploads it to a new GitHub release, then deletes local zip, notes, `publish\`, and `artifacts\` folders.

### Publish output

```bat
publish-standalone.bat
```

```
publish\
├── DownloadMaster.exe      ← ~225 MB self-contained
└── tools\
    ├── yt-dlp.exe
    └── ffmpeg\
        ├── ffmpeg.exe
        └── ffprobe.exe
```

Copy the entire `publish\` folder to any Windows PC and run `DownloadMaster.exe`.

### Visual Studio

Open `DownloadMaster.sln` — default configuration: **Release | x64**.

---

## Settings

Stored at:

```
%AppData%\DownloadMaster\settings.json
```

Includes default save folder, theme, language, and max concurrent downloads.

---

## Project documentation

- [docs/STRUCTURE.md](docs/STRUCTURE.md) — full repository layout and architecture
- [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) — licenses for all dependencies and bundled tools

---

## Repository layout (summary)

```
VideoDownloader/
├── DownloadMaster.sln
├── Directory.Build.props
├── LICENSE
├── THIRD_PARTY_LICENSES.md
├── README.md
├── docs/
│   └── STRUCTURE.md
├── DownloadMaster/           # WPF application
│   ├── Assets/
│   ├── Models/
│   ├── Services/
│   ├── Styles/
│   ├── Themes/
│   ├── tools/                # yt-dlp + FFmpeg (gitignored binaries)
│   └── ...
├── setup-tools.bat
├── start.bat
├── build.bat
└── publish-standalone.bat
```

---

## Contributing

Pull requests are welcome. By contributing, you agree that your contributions will be licensed under the same [MIT License](LICENSE) as the project.

---

## Disclaimer

Use DownloadMaster responsibly and only for content you have the right to download. The author is not responsible for misuse of this software or for third-party site terms of service.
