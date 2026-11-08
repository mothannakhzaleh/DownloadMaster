# Third-Party Licenses

DownloadMaster bundles and/or depends on the components below. This file summarizes their licenses for public redistribution. Full license texts are available from each project’s official repository or website.

## Application dependencies (NuGet)

| Component | Version | License | Source |
|-----------|---------|---------|--------|
| [.NET 10](https://dotnet.microsoft.com/) runtime | 10.0 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | Microsoft |
| [Avalonia UI](https://avaloniaui.net/) (Avalonia, Desktop, Fluent theme, Inter font, DataGrid) | 11.3 | [MIT](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) | AvaloniaUI |
| [Inter font](https://rsms.me/inter/) (via Avalonia.Fonts.Inter) | bundled | [SIL Open Font License 1.1](https://github.com/rsms/inter/blob/master/LICENSE.txt) | Rasmus Andersson |
| [SkiaSharp](https://github.com/mono/SkiaSharp) / HarfBuzzSharp (pulled by Avalonia) | bundled | [MIT](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md) | Microsoft / Mono |
| [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore) | 5.1.0 | [MIT](https://github.com/rosenbjerg/FFMpegCore/blob/main/LICENSE) | rosenbjerg |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) (Q16, AnyCPU) | 14.17.2 | [Apache-2.0](https://github.com/dlemstra/Magick.NET/blob/main/License.txt) | Dirk Lemstra; includes [ImageMagick](https://imagemagick.org/script/license.php) |
| [System.Speech](https://www.nuget.org/packages/System.Speech) (Windows voices only) | 8.0.0 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | Microsoft |

## Bundled external tools (not included in Git)

These binaries are **not committed** to the repository because of size and licensing redistribution rules. Release archives built by GitHub Actions include them; for local builds run `scripts\setup-tools.bat` (Windows) or `scripts/setup-tools.sh` (macOS/Linux).

| Tool | License | Official source | Notes |
|------|---------|-----------------|-------|
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | [The Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE) | https://github.com/yt-dlp/yt-dlp/releases | `tools/yt-dlp(.exe)` |
| [FFmpeg](https://ffmpeg.org/) (Windows, Linux) | [GPL-3.0-or-later](https://www.gnu.org/licenses/gpl-3.0.html) — "gpl" builds include libx264/libx265 | https://github.com/BtbN/FFmpeg-Builds | `tools/ffmpeg/ffmpeg(.exe)` and `ffprobe(.exe)` |
| [FFmpeg](https://ffmpeg.org/) (macOS, best effort) | [GPL-3.0-or-later](https://www.gnu.org/licenses/gpl-3.0.html) | https://ffmpeg.martin-riedl.de/ | Falls back to Homebrew (`brew install ffmpeg`) |
| espeak-ng (Linux speech, user-installed) | [GPL-3.0-or-later](https://github.com/espeak-ng/espeak-ng/blob/master/COPYING) | https://github.com/espeak-ng/espeak-ng | Not bundled; installed by the user |

### FFmpeg redistribution notice

The FFmpeg binaries shipped in release archives are **GPL** builds (needed for H.264/H.265 encoding via libx264/libx265). FFmpeg runs as a separate program; DownloadMaster does not link to it. When you redistribute those binaries you must comply with the GPL, including:

- Provide the corresponding FFmpeg source code, or a written offer for it (the build providers above publish their sources and build scripts)
- Document that FFmpeg is used and under which license
- Do not imply FFmpeg is your original work

DownloadMaster itself (the Avalonia application source code) is licensed under the MIT License — see [LICENSE](LICENSE).

## Trademarks

YouTube, Instagram, X/Twitter, and other platform names are trademarks of their respective owners. DownloadMaster is an independent tool and is not affiliated with or endorsed by those services.
