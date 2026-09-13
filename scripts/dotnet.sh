#!/usr/bin/env bash
# Run any dotnet command inside the SDK container. No host .NET install needed.
#   scripts/dotnet.sh build
#   scripts/dotnet.sh format
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker compose run --rm --no-deps sdk "$@"
