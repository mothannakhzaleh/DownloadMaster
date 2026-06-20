**Last updated:** {{DATE}}

## DownloadMaster {{VERSION}}

### What's new

#### Fix — indexed BMP batch convert
- **invalid colormap index** errors when converting game-style 8-bit BMP folders with PNG palette optimization
- Same palette expansion fix as MediaConvertor for Image Convert

#### Patch
- ASCII-safe copyright metadata in project file (avoids encoding corruption in release builds)

#### Convert tab (from 1.3.0)
- **Audio Convert** and **Speech** (text-to-speech) sub-tabs
- **Video / Image Convert** with PNG palette optimization, GIF, frames
- Dark theme polish for Convert sub-tabs; English and Arabic localization

#### Files, Instagram, downloads
- Direct HTTP downloads with pause/resume; Google Drive links
- Instagram profile browse; yt-dlp for 1000+ sites

### Install
Windows 10+ x64. Extract the zip and run `DownloadMaster.exe`. No .NET runtime required.

Bundled **yt-dlp** and **FFmpeg** are included under `tools\`.
