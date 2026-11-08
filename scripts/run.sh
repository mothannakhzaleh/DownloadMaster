#!/usr/bin/env bash
# Build DownloadMaster (Release) and run it on Linux or macOS.
# Arguments are passed to the app.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/DownloadMaster/DownloadMaster.csproj"
OUT="$ROOT/src/DownloadMaster/bin/Release/net10.0"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: .NET SDK not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
    exit 1
fi

if [ ! -e "$ROOT/src/DownloadMaster/tools/yt-dlp" ] && ! command -v yt-dlp >/dev/null 2>&1; then
    echo "NOTE: yt-dlp not found. Run scripts/setup-tools.sh (or install yt-dlp with your package manager)."
fi

echo "Building Release..."
dotnet build "$PROJECT" -c Release

echo "Starting DownloadMaster..."
if [ -x "$OUT/DownloadMaster" ]; then
    exec "$OUT/DownloadMaster" "$@"
elif [ -f "$OUT/DownloadMaster.dll" ]; then
    exec dotnet "$OUT/DownloadMaster.dll" "$@"
else
    echo "ERROR: Built app not found in $OUT" >&2
    exit 1
fi
