#!/usr/bin/env bash
# DownloadMaster - set up bundled tools on Linux / macOS.
# Copyright (c) 2026 mothannakh - https://satisfy.live/
#
# Downloads into src/DownloadMaster/tools:
#   tools/yt-dlp                 (yt-dlp_linux / yt-dlp_linux_aarch64 / yt-dlp_macos)
#   tools/ffmpeg/ffmpeg          (Linux: BtbN static build, macOS: martin-riedl.de static build)
#   tools/ffmpeg/ffprobe
#
# Usage: scripts/setup-tools.sh [--force]
# On Windows use setup-tools.bat instead.
# Alternative on macOS: brew install ffmpeg yt-dlp (the app finds Homebrew tools).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS="$ROOT/src/DownloadMaster/tools"
FFMPEG_DST="$TOOLS/ffmpeg"
YTDLP_BASE="https://github.com/yt-dlp/yt-dlp/releases/latest/download"
BTBN_BASE="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest"
FORCE=0
[ "${1:-}" = "--force" ] && FORCE=1

echo
echo " DownloadMaster - setup bundled tools"
echo " Copyright (c) 2026 mothannakh - https://satisfy.live/"
echo

os="$(uname -s)"
arch="$(uname -m)"
case "$os" in
    Linux | Darwin) ;;
    MINGW* | MSYS* | CYGWIN*)
        echo "On Windows, run setup-tools.bat instead."
        exit 1 ;;
    *)
        echo "ERROR: unsupported OS: $os" >&2
        exit 1 ;;
esac

for cmd in curl unzip tar; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "ERROR: '$cmd' is required. Install it and run again." >&2
        exit 1
    fi
done

mkdir -p "$FFMPEG_DST"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

download() { # url dest
    echo "  -> $1"
    curl -fL --retry 3 --progress-bar -o "$2" "$1"
}

# ---------------------------------------------------------------- yt-dlp
if [ "$os" = "Darwin" ]; then
    ytdlp_asset="yt-dlp_macos"
elif [ "$arch" = "aarch64" ] || [ "$arch" = "arm64" ]; then
    ytdlp_asset="yt-dlp_linux_aarch64"
else
    ytdlp_asset="yt-dlp_linux"
fi

if [ -f "$TOOLS/yt-dlp" ] && [ "$FORCE" -eq 0 ]; then
    echo "yt-dlp already present."
else
    echo "Downloading yt-dlp ($ytdlp_asset)..."
    download "$YTDLP_BASE/$ytdlp_asset" "$TMP/yt-dlp"
    chmod +x "$TMP/yt-dlp"
    mv "$TMP/yt-dlp" "$TOOLS/yt-dlp"
fi

# ---------------------------------------------------------------- FFmpeg
install_pair() { # dir containing ffmpeg + ffprobe somewhere below
    local f p
    f="$(find "$1" -type f -name ffmpeg | head -n 1)"
    p="$(find "$1" -type f -name ffprobe | head -n 1)"
    if [ -z "$f" ] || [ -z "$p" ]; then
        return 1
    fi
    cp "$f" "$FFMPEG_DST/ffmpeg"
    cp "$p" "$FFMPEG_DST/ffprobe"
    chmod +x "$FFMPEG_DST/ffmpeg" "$FFMPEG_DST/ffprobe"
}

if [ -f "$FFMPEG_DST/ffmpeg" ] && [ -f "$FFMPEG_DST/ffprobe" ] && [ "$FORCE" -eq 0 ]; then
    echo "FFmpeg already present in tools/ffmpeg/"
elif [ "$os" = "Linux" ]; then
    case "$arch" in
        x86_64 | amd64) ff_asset="ffmpeg-master-latest-linux64-gpl.tar.xz" ;;
        aarch64 | arm64) ff_asset="ffmpeg-master-latest-linuxarm64-gpl.tar.xz" ;;
        *) echo "ERROR: no FFmpeg build for $arch. Install ffmpeg with your package manager." >&2; exit 1 ;;
    esac
    echo "Downloading FFmpeg ($ff_asset)..."
    download "$BTBN_BASE/$ff_asset" "$TMP/ffmpeg.tar.xz"
    mkdir -p "$TMP/ffmpeg"
    tar -xJf "$TMP/ffmpeg.tar.xz" -C "$TMP/ffmpeg"
    install_pair "$TMP/ffmpeg" || { echo "ERROR: ffmpeg/ffprobe not found in archive." >&2; exit 1; }
else
    case "$arch" in
        arm64) mac_arch=arm64 ;;
        *) mac_arch=amd64 ;;
    esac
    echo "Downloading FFmpeg for macOS ($mac_arch, ffmpeg.martin-riedl.de)..."
    mkdir -p "$TMP/mac"
    if download "https://ffmpeg.martin-riedl.de/redirect/latest/macos/$mac_arch/release/ffmpeg.zip" "$TMP/ffmpeg.zip" \
        && download "https://ffmpeg.martin-riedl.de/redirect/latest/macos/$mac_arch/release/ffprobe.zip" "$TMP/ffprobe.zip" \
        && unzip -o -q "$TMP/ffmpeg.zip" -d "$TMP/mac" \
        && unzip -o -q "$TMP/ffprobe.zip" -d "$TMP/mac" \
        && install_pair "$TMP/mac"; then
        :
    else
        echo
        echo "Could not download FFmpeg. Install it with Homebrew instead:"
        echo "  brew install ffmpeg yt-dlp"
        echo "DownloadMaster finds Homebrew tools in /opt/homebrew/bin and /usr/local/bin."
    fi
fi

echo
echo "Tools folder:"
ls -l "$TOOLS" "$FFMPEG_DST"
if [ "$os" = "Darwin" ]; then
    echo
    echo "Tip: with Homebrew you can also just run: brew install ffmpeg yt-dlp"
fi
echo
