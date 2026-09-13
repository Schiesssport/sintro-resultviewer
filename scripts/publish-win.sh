#!/usr/bin/env bash
# Build the Windows deployment: one self-contained executable plus its assets.
#
# This is the real artefact. Docker is only the local development convenience — a range
# installs this next to the Sintro software and its SQL Server, with no .NET runtime to install.
#
# Output: bin/win-x64/
set -euo pipefail
cd "$(dirname "$0")/.."

OUTPUT="${1:-bin/win-x64}"

rm -rf "$OUTPUT"

docker compose run --rm --no-deps sdk publish src/Sintro.ResultViewer.Api/Sintro.ResultViewer.Api.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none \
    -o "$OUTPUT"

echo
echo "published to $OUTPUT/"
ls -la "$OUTPUT" | head
