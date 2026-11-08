#!/usr/bin/env bash
# Build DownloadMaster (Release) on Linux or macOS.
# Output: src/DownloadMaster/bin/Release/net10.0/
# Extra arguments are passed to "dotnet build".
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/DownloadMaster/DownloadMaster.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: .NET SDK not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
    exit 1
fi

echo "Building Release..."
dotnet build "$PROJECT" -c Release "$@"

echo
echo "Build OK: src/DownloadMaster/bin/Release/net10.0/"
