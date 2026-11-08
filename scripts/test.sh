#!/usr/bin/env bash
# Run every regression test project in tests/.
# Test files are written to test-output/ (never your Downloads or Videos folders).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOWNLOADMASTER_DATA_DIR="$ROOT/test-output/sandbox"

failed=0
for project in "$ROOT"/tests/*/; do
    if compgen -G "$project*.csproj" > /dev/null; then
        echo
        echo "=== $(basename "$project") ==="
        dotnet run --project "$project" -c Release || failed=1
    fi
done

echo
if [ "$failed" -ne 0 ]; then
    echo "Some tests FAILED."
    exit 1
fi
echo "All tests passed."
