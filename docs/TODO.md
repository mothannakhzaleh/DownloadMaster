# DownloadMaster — future work

What is still open from [plans/upgrade-plan.md](plans/upgrade-plan.md) and other ideas.

## From the upgrade plan

### Native C++ rewrite (plan Phases 1 and 6)
- **Status:** Not started — the current app is C# / Avalonia; the C++ version will replace it later.
- Dear ImGui (event-driven, near-zero idle GPU/CPU), multi-viewport popups.
- Native HLS / DASH parsing with AES-128 decryption and in-process muxing (libavformat) instead of yt-dlp / ffmpeg programs.
- Note: keeping yt-dlp is still recommended for site extraction (it is updated every few weeks as sites change).

### Smaller downloads
- Custom FFmpeg build with only the codecs DownloadMaster uses (~20–40 MB instead of ~400 MB).
- Try .NET trimming / Native AOT for the app executable.

### Extension
- Firefox build (Manifest V3 differences: `background.scripts`, `browser_specific_settings`).
- Publish to the Chrome Web Store / Edge Add-ons so installing doesn't need Developer mode.

### Queue
- Multiple named queues (e.g. Main and Media) with their own limits and schedules.
- Windows Defender / antivirus scan hook after download.
- One-time "DownloadMaster is still running in the tray" notification.

## Other

### MEGA.nz download support
- **Status:** Not started
- **Why:** MEGA uses encrypted API downloads, not plain HTTP. The `#key` in sharing links is required client-side for decryption.
- **Approach:** Integrate [MegaApiClient](https://www.nuget.org/packages/MegaApiClient/) (or similar) as a separate `DownloadKind.Mega` path in the download queue — not an extension of the Files tab HTTP engine.
- **Notes:** Free MEGA accounts have ~5 GB/day download limits; the library is in passive maintenance.

### macOS signing
- Sign and notarize the `.app` with an Apple Developer ID so Gatekeeper doesn't block the first launch.
