#!/usr/bin/env bash
# Build and run the API against the dev database. Viewer on http://localhost:8080
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose up -d --wait db
docker compose up --build api
