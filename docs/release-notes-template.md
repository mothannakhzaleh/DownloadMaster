**Last updated:** {{DATE}}

## DownloadMaster {{VERSION}}

### What's new

#### Fix — Satisfy / PKO 8-bit BMP load

- **invalid colormap index** when converting game BMPs that declare 255 palette slots but pixels use index 255
- BMP palette is normalized in memory before ImageMagick reads the file (works with or without PNG optimize)
- Same fix as MediaConvertor for Image Convert

### Install

Windows 10+ x64. Extract the zip and run `DownloadMaster.exe`. No .NET runtime required.

Bundled **yt-dlp** and **FFmpeg** are included under `tools\`.
