# DownloadMaster — future work

Open items, roughly in priority order. Long-term plan: [plans/upgrade-plan.md](plans/upgrade-plan.md).
Done items are removed; see the git history and README for what already ships.

## Next up

### Verify on real systems
- **Status:** CI builds and tests pass on Windows, Linux and macOS; nobody has run the packages by hand yet.
- Launch the macOS `.dmg` / `.app` (Apple Silicon and Intel) and the Linux `.deb` / `.tar.gz` on a desktop.
- Install the Windows setup `.exe` (per-user), check the PATH option for `downloadmaster-cli`, uninstall.
- Load the extension in Chrome / Edge with **Load unpacked** and catch a real download and a video.
- Open the remote-control page on a phone over Wi-Fi (Windows asks to allow the app through the firewall).

### Remote control
- **HTTPS** for the remote page (self-signed certificate + fingerprint shown next to the pairing link), so the
  token never travels in plain text on the home network.
- QR code for the pairing link in Settings (scan instead of typing).
- Docker image for `downloadmaster-cli serve` (yt-dlp + FFmpeg inside; data and downloads as volumes).
- Keep the headless server's queue across restarts (today it resumes `.part` files only when re-added).

### Command line
- `get` resumes a previous partial download of the same URL and folder (the engine supports it; the CLI starts a
  new item each run).
- `downloadmaster-cli pause|resume|cancel <id>` against a running server (the API already exists).

### Queue
- Multiple named queues (e.g. Main and Media) with their own limits and schedules.
- Per-download speed limit (the global limit exists).
- Speed limit only while the computer is in use (idle detection).

### Extension
- Firefox build (Manifest V3 differences: `background.scripts`, `browser_specific_settings`).
- Publish to the Chrome Web Store / Edge Add-ons so installing doesn't need Developer mode.
- Native messaging host as an alternative to the localhost bridge.

## Later

### Native C++ rewrite (plan Phases 1 and 6)
- **Status:** Not started — the current app is C# / Avalonia; the C++ version will replace it later.
- Dear ImGui (event-driven, near-zero idle GPU/CPU), multi-viewport popups.
- Native HLS / DASH parsing with AES-128 decryption and in-process muxing (libavformat) instead of yt-dlp / ffmpeg programs.
- Keeping yt-dlp is still recommended for site extraction (it is updated every few weeks as sites change).

### Smaller downloads
- Custom FFmpeg build with only the codecs DownloadMaster uses (~20–40 MB instead of ~400 MB).
- Try .NET trimming / Native AOT for the app and the command-line tool.

### More sources
- **MEGA.nz:** encrypted API downloads (the `#key` in links decrypts client-side). Integrate a MEGA client library
  as its own `DownloadKind`, not in the HTTP engine. Free accounts have ~5 GB/day limits.
- **Torrents / magnet links** with file selection — needs a BitTorrent library; large feature.
- **Image galleries** (gallery-dl as an optional bundled tool).

### Distribution
- Sign and notarize the macOS app (Apple Developer ID) so Gatekeeper doesn't block the first launch.
- Sign the Windows installer and executables (SmartScreen warning today).
- `winget`, Homebrew cask, AppImage and `.rpm` packages.
