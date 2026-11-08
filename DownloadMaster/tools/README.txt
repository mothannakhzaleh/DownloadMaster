DownloadMaster bundled tools
==========================

Copyright (c) 2026 mothannakh · https://satisfy.live/

This folder is copied next to the DownloadMaster executable on build and publish.
If a tool is missing here, DownloadMaster looks on PATH and in the usual install
folders (Homebrew /opt/homebrew/bin and /usr/local/bin, /usr/bin, /snap/bin, ~/.local/bin).

Files per OS:

  Windows                    Linux                          macOS
  tools\yt-dlp.exe           tools/yt-dlp                   tools/yt-dlp
                             (or yt-dlp_linux,              (or yt-dlp_macos)
                              yt-dlp_linux_aarch64)
  tools\ffmpeg\ffmpeg.exe    tools/ffmpeg/ffmpeg            tools/ffmpeg/ffmpeg
  tools\ffmpeg\ffprobe.exe   tools/ffmpeg/ffprobe           tools/ffmpeg/ffprobe

On Linux / macOS the files must be executable (chmod +x); the app also tries to
set the bit itself.

Setup (from repository root):
  Windows:        setup-tools.bat
  Linux / macOS:  scripts/setup-tools.sh
  macOS (alt.):   brew install ffmpeg yt-dlp
  Linux (alt.):   sudo apt install ffmpeg yt-dlp   (or your distro's package manager)

Manual downloads:
  yt-dlp:  https://github.com/yt-dlp/yt-dlp/releases/latest
           (yt-dlp.exe / yt-dlp_linux / yt-dlp_linux_aarch64 / yt-dlp_macos)
  FFmpeg:  Windows / Linux  https://github.com/BtbN/FFmpeg-Builds/releases
           Windows          https://www.gyan.dev/ffmpeg/builds/
           macOS            https://ffmpeg.martin-riedl.de/

Release packages built by GitHub Actions already include these tools
(macOS FFmpeg is best effort; fall back to Homebrew if it is missing).

Licenses:
  See THIRD_PARTY_LICENSES.md in the repository root.
