#!/usr/bin/env bash
# Publish a self-contained single-file DownloadMaster build.
#
# Usage: scripts/publish.sh [--portable] [RID]
#   RID defaults to the current OS/arch: linux-x64, linux-arm64, osx-arm64, osx-x64.
#   Other examples: win-x64 (cross-publish from Linux/macOS works too).
#   --portable  also write the DownloadMaster.portable marker: the build then keeps its
#               settings and data in ship/<RID>/data instead of the user profile.
#
# Output: ship/<RID>/  (DownloadMaster + tools/ copied from src/DownloadMaster/tools,
#         plus downloadmaster-cli when src/DownloadMaster.Cli exists)
# macOS .app bundles and release archives are built by .github/workflows/release.yml.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/DownloadMaster/DownloadMaster.csproj"
TOOLS="$ROOT/src/DownloadMaster/tools"
CLI_PROJECT="$ROOT/src/DownloadMaster.Cli/DownloadMaster.Cli.csproj"

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

PORTABLE=0
RID=""
for arg in "$@"; do
    case "$arg" in
        --portable) PORTABLE=1 ;;
        -h | --help) sed -n '2,12p' "$0"; exit 0 ;;
        -*) echo "ERROR: unknown option $arg" >&2; exit 1 ;;
        *)
            if [ -n "$RID" ]; then
                echo "ERROR: more than one RID given ($RID, $arg)" >&2
                exit 1
            fi
            RID="$arg"
            ;;
    esac
done
if [ -z "$RID" ]; then
    RID="$(detect_rid)"
fi
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

# Optional command-line companion: published into a scratch folder, then only its executable is
# copied next to the app so none of the app's files can be overwritten.
if [ -f "$CLI_PROJECT" ]; then
    case "$RID" in win-*) cli_exe=downloadmaster-cli.exe ;; *) cli_exe=downloadmaster-cli ;; esac
    cli_tmp="$ROOT/ship/.cli-$RID"
    echo "Publishing $cli_exe ..."
    rm -rf "$cli_tmp"
    dotnet publish "$CLI_PROJECT" -c Release -r "$RID" --self-contained true         -p:PublishSingleFile=true         -p:IncludeNativeLibrariesForSelfExtract=true         -p:EnableCompressionInSingleFile=true         -p:DebugType=None         -o "$cli_tmp"
    if [ ! -f "$cli_tmp/$cli_exe" ]; then
        echo "ERROR: $cli_exe was not produced by $CLI_PROJECT" >&2
        exit 1
    fi
    cp "$cli_tmp/$cli_exe" "$OUTPUT/$cli_exe"
    case "$RID" in win-*) ;; *) chmod +x "$OUTPUT/$cli_exe" ;; esac
    rm -rf "$cli_tmp"
fi

if [ "$PORTABLE" = 1 ]; then
    printf '%s
'         'DownloadMaster portable mode.'         'While this file is next to the DownloadMaster executable, settings and download state are'         'kept in the "data" folder here instead of the user profile. Delete it to use the profile.'         > "$OUTPUT/DownloadMaster.portable"
fi

echo
echo "Publish complete: $OUTPUT"
if [ "$PORTABLE" = 1 ]; then
    echo "Portable: data is kept in $OUTPUT/data"
fi
if [ -n "$missing" ]; then
    echo "Not bundled (missing in src/DownloadMaster/tools):$missing"
    echo "The app falls back to tools on PATH / Homebrew. Run scripts/setup-tools.sh to bundle them."
fi
