#!/usr/bin/env bash
# Run every test: the .NET suite (unit + integration against the seeded dev
# database) and the viewer's pure-logic tests.
set -euo pipefail
cd "$(dirname "$0")/.."

docker compose up -d --wait db
docker compose run --rm sdk test "$@"

echo
echo "── viewer (wwwroot/core) ──"
./scripts/test-web.sh
