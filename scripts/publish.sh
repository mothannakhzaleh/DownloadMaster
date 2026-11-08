#!/usr/bin/env bash
# Publish a self-contained single-file DownloadMaster build.
#
# Usage: scripts/publish.sh [RID]
#   RID defaults to the current OS/arch: linux-x64, linux-arm64, osx-arm64, osx-x64.
#   Other examples: win-x64 (cross-publish from Linux/macOS works too).
#
# Output: ship/<RID>/  (DownloadMaster + tools/ copied from src/DownloadMaster/tools)
# macOS .app bundles and release archives are built by .github/workflows/release.yml.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/DownloadMaster/DownloadMaster.csproj"
TOOLS="$ROOT/src/DownloadMaster/tools"

detect_rid() {
    local os arch
    case "$(uname -s)" in
        Darwin) os=osx ;;
        Linux) os=linux ;;
        MINGW* | MSYS* | CYGWIN*) os=win ;;
        *) echo "ERROR: unsupported OS $(uname -s); pass a RID explicitly." >&2; return 1 ;;
    esac
    case "$(uname -m)" in
        x86_64 | amd64) arch=x64 ;;
        arm64 | aarch64) arch=arm64 ;;
        *) echo "ERROR: unsupported CPU $(uname -m); pass a RID explicitly." >&2; return 1 ;;
    esac
    echo "$os-$arch"
}

RID="${1:-$(detect_rid)}"
OUTPUT="$ROOT/ship/$RID"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: .NET SDK not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
    exit 1
fi

echo "Publishing DownloadMaster for $RID -> ship/$RID"
rm -rf "$OUTPUT"

dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=None \
    -o "$OUTPUT"

# Bundled tools (setup-tools.sh / setup-tools.bat put them in src/DownloadMaster/tools).
mkdir -p "$OUTPUT/tools/ffmpeg"
rm -f "$OUTPUT/tools/ffmpeg/.gitkeep"
case "$RID" in
    win-*) names="yt-dlp.exe ffmpeg/ffmpeg.exe ffmpeg/ffprobe.exe" ;;
    *) names="yt-dlp ffmpeg/ffmpeg ffmpeg/ffprobe" ;;
esac
missing=""
for name in $names; do
    if [ -f "$TOOLS/$name" ]; then
        cp "$TOOLS/$name" "$OUTPUT/tools/$name"
        case "$RID" in win-*) ;; *) chmod +x "$OUTPUT/tools/$name" ;; esac
    else
        missing="$missing $name"
    fi
done

case "$RID" in win-*) ;; *) chmod +x "$OUTPUT/DownloadMaster" ;; esac

echo
echo "Publish complete: $OUTPUT"
if [ -n "$missing" ]; then
    echo "Not bundled (missing in src/DownloadMaster/tools):$missing"
    echo "The app falls back to tools on PATH / Homebrew. Run scripts/setup-tools.sh to bundle them."
fi
