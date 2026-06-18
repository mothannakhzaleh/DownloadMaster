# Third-Party Licenses

DownloadMaster bundles and/or depends on the components below. This file summarizes their licenses for public redistribution. Full license texts are available from each project’s official repository or website.

## Application dependencies (NuGet)

| Component | Version | License | Source |
|-----------|---------|---------|--------|
| [.NET 8](https://dotnet.microsoft.com/) (WPF runtime) | 8.0 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | Microsoft |
| [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite) | 8.0.11 | [MIT](https://github.com/dotnet/efcore/blob/main/LICENSE.txt) | Microsoft |
| [SQLite native](https://www.sqlite.org/) (pulled by Microsoft.Data.Sqlite) | bundled | [Public Domain](https://www.sqlite.org/copyright.html) | SQLite Consortium |

## Bundled external tools (not included in Git)

These binaries are **not committed** to the repository because of size and licensing redistribution rules. Run `setup-tools.bat` before building or publishing.

| Tool | Typical license | Official source | Notes |
|------|-----------------|-----------------|-------|
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | [The Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE) | https://github.com/yt-dlp/yt-dlp/releases | Downloaded by `setup-tools.bat` |
| [FFmpeg](https://ffmpeg.org/) | [LGPL-2.1-or-later](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html) (most Windows builds) | https://www.gyan.dev/ffmpeg/builds/ or https://github.com/BtbN/FFmpeg-Builds | You must place `ffmpeg.exe` and `ffprobe.exe` in `DownloadMaster/tools/ffmpeg/` |

### FFmpeg redistribution notice

If you publish or ship FFmpeg binaries with DownloadMaster, you are responsible for complying with FFmpeg’s license (typically LGPL). Common requirements include:

- Provide source code or a written offer for FFmpeg source
- Document that FFmpeg is used and under which license
- Do not imply FFmpeg is your original work

DownloadMaster itself (the WPF application source code) is licensed under the MIT License — see [LICENSE](LICENSE).

## Trademarks

YouTube, X/Twitter, and other platform names are trademarks of their respective owners. DownloadMaster is an independent tool and is not affiliated with or endorsed by those services.
