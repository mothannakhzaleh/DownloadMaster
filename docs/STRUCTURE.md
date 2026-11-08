# DownloadMaster — project structure

Cross-platform (Windows / macOS / Linux) Avalonia app on .NET 10, a Chrome/Edge extension, regression tests,
and scripts. Long-term plan (native C++ rewrite): [plans/upgrade-plan.md](plans/upgrade-plan.md).

## Repository tree

```
VideoDownloader/
├── DownloadMaster.sln / .slnx       # App + test projects (Debug/Release, Any CPU)
├── Directory.Build.props            # Defaults every project to Release
├── README.md · LICENSE · THIRD_PARTY_LICENSES.md
├── .github/workflows/
│   ├── ci.yml                       # Build + tests on Windows / Linux / macOS, extension check
│   └── release.yml                  # Tag v* → GitHub Release; push to master → "nightly" pre-release
├── src/
│   ├── DownloadMaster/              # The app (see below)
│   └── browser-extension/           # Chrome / Edge / Brave / Opera extension (Manifest V3)
│       ├── manifest.json · background.js · content.js · overlay.css
│       ├── popup/                   # Extension popup (status, toggles, streams in this tab)
│       └── icons/
├── tests/
│   ├── ImageConversion.Regression/  # 39 image conversion checks
│   └── DownloadEngine.Regression/   # Multi-connection engine checks against a local range server
├── scripts/                         # .bat (Windows) and .sh (Linux / macOS)
│   ├── setup-tools    build    run    test    publish    clean.bat    release-github.bat
│   └── screenshots.ps1              # Regenerates docs/images from the ship build
├── docs/
│   ├── images/                      # README screenshots
│   ├── plans/upgrade-plan.md
│   ├── STRUCTURE.md · TODO.md · release-notes-template.md
├── ship/                            # Ready-to-run builds from scripts/publish (gitignored)
└── test-output/                     # Test runs and sandboxes (gitignored)
```

## The app — `src/DownloadMaster/`

```
src/DownloadMaster/
├── Program.cs                 # Entry point; hands off to an already running instance
├── App.axaml(.cs)             # Theme + styles, tray icon and menu, local bridge startup
├── MainWindow.axaml(.cs)      # Video / Instagram / Files / Convert tabs and the download queue
├── Windows/                   # Dialogs
│   ├── DownloadCaughtWindow   # "New download caught" popup (from the extension, clipboard, …)
│   ├── ExtensionInstallWindow # 3-step browser extension installer
│   ├── BatchDownloadWindow    # part_[01-20].rar style batch links
│   ├── SettingsWindow · AboutWindow · InstagramCookiesWindow · FinishCountdownWindow
├── Views/                     # Convert sub-tabs: Video, Image, Audio, Speech
├── Controls/                  # SegmentedProgressBar (per-connection progress)
├── Models/                    # DownloadItem, AppSettings, conversion settings, catalogs
├── Services/
│   ├── Downloads/             # DownloadManager (queue, retry), HttpFileDownloadService (multi-connection
│   │                          # engine, dynamic splitting, resume), YtDlpService, SpeedLimiter, checksums
│   ├── Bridge/                # LocalBridgeServer (127.0.0.1:45780), SingleInstance, request models
│   ├── Queue/                 # Scheduler, finish actions, categories, batch patterns, clipboard watcher
│   ├── Convert/               # FFmpeg video/audio, Magick.NET images, previews
│   ├── Speech/                # Windows voices, macOS say, Linux espeak-ng / spd-say
│   ├── Instagram/             # Profile browsing, cookies, direct media download
│   ├── Ui/                    # Dialogs, file pickers, open file/folder/URL (cross-platform)
│   ├── Localization/          # Feature string tables (English + Arabic)
│   └── AppPaths · SettingsService · ToolLocator · ThemeService · AutoStartService · BrowserExtensionInstaller
├── Styles/Controls.axaml      # Shared look: Button.primary/secondary, RadioButton.chip, Border.card, tabs
├── Themes/Theme.axaml         # Dark and Light palettes
├── Assets/                    # Icons
└── tools/                     # yt-dlp + FFmpeg (binaries gitignored; scripts/setup-tools)
```

## How the parts talk

```
 Browser ──extension──▶ 127.0.0.1:45780 (LocalBridgeServer) ──▶ DownloadCaughtWindow ──▶ DownloadManager
                                                                                          ├─ HttpFileDownloadService (files)
 MainWindow tabs / clipboard / batch / tray ───────────────────────────────────────────▶ └─ YtDlpService (videos, pages)
                                                                                               │
                                                                    tools/yt-dlp, tools/ffmpeg ◀┘ (ToolLocator: bundled → PATH → Homebrew)
```

- **Browser downloads**: the extension cancels the browser download and POSTs `/api/v1/download` with cookies,
  referrer and user agent. The app shows the caught popup; the item carries those headers on every request.
- **Videos**: the extension's video button posts `/api/v1/media-stream` (a sniffed HLS / DASH / MP4 stream, or the
  page itself) → yt-dlp with `--add-header`, or the file engine for plain MP4.
- **Files engine**: probe (`Range: bytes=0-0`) → N segments → finished connections split the largest remaining
  segment → state saved about every second to `file-downloads/<id>.json` → `.part` renamed when complete → SHA-256.
- **Expired links**: HTTP 403/410 keeps the `.part`; catching the same file again offers to continue it.

## Data and settings

`AppPaths.DataDir` = `%AppData%\DownloadMaster` (Windows) or `~/.config/DownloadMaster` (macOS / Linux), or
`$DOWNLOADMASTER_DATA_DIR` when set (tests and `scripts\run.bat --sandbox` use `test-output/`).

| File | Purpose |
|------|---------|
| `settings.json` | Save folder, theme, language, connections, tray, scheduler, interception, categories |
| `file-downloads/*.json` | Resume state per direct-file download |
| `instagram-cookies.txt` | Saved Instagram session (never committed) |
| `browser-extension/` | Copy of the extension that the browser loads ("Load unpacked") |

## External programs

| Program | Role |
|---------|------|
| `tools/yt-dlp(.exe)` | Video metadata and downloads, browser cookie export |
| `tools/ffmpeg/ffmpeg(.exe)`, `ffprobe(.exe)` | Merging, Convert tabs, probing |
| `say` / `espeak-ng` / `spd-say` | Speech tab on macOS / Linux |

## Scripts

| Script | Steps |
|--------|-------|
| `scripts/setup-tools` | Download yt-dlp and FFmpeg into `src/DownloadMaster/tools/` |
| `scripts/build` | `dotnet build -c Release` |
| `scripts/run` | Build, then start (`--sandbox` = data in `test-output/sandbox`) |
| `scripts/test` | `dotnet run` every project in `tests/` with a sandbox data folder |
| `scripts/publish` | Self-contained single-file publish → `ship/<rid>/` with `tools/` and `browser-extension/` |
| `scripts/clean.bat` | Delete `bin/`, `obj/`, `test-output/`, `ship/` |
| `scripts/release-github.bat` | Local win-x64 release upload (fallback; prefer tagging) |
| `scripts/screenshots.ps1` | Run the ship build sandboxed and capture `docs/images/*.png` |

## Release checklist

- [ ] `scripts\test.bat` (or `scripts/test.sh`) passes
- [ ] Update **What's new** in `docs/release-notes-template.md`
- [ ] `git tag vX.Y.Z && git push origin vX.Y.Z` — GitHub Actions builds every platform and publishes
- [ ] Never commit `bin/`, `obj/`, `ship/`, `test-output/`, or tool binaries
