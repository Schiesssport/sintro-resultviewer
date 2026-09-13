#!/usr/bin/env bash
# Start the dev SQL Server and wait until it answers queries.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose up -d --wait db
echo "db ready on localhost:11433 (sa / ${MSSQL_SA_PASSWORD:-Sintro_Dev_2026!})"
