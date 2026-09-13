#!/usr/bin/env bash
# Ad-hoc query against the dev database — the fastest way to check a mapping rule.
#   scripts/sql.sh "SELECT TOP 5 ProgramID, Name FROM Programs ORDER BY ProgramID DESC"
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "${MSSQL_SA_PASSWORD:-Sintro_Dev_2026!}" -C \
    -d "${SINTRO_DB:-DBSINTRO300}" -W -s'|' -Q "$1"
