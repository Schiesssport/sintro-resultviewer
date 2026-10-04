#!/usr/bin/env bash
# Build a seeded device database from seed/ — invented clubs, shooters and passes, no export needed.
#
#   scripts/db-seed.sh              # creates (or replaces) the database SintroSeed
#   scripts/db-seed.sh DBSINTRO300  # replace the restored export with the seed instead
#
# Point the API or the tests at it with the connection string's Database=... (the sdk service
# in docker-compose.yml reads SINTRO_DB). Re-run after editing seed/definitions.mjs.
set -euo pipefail
cd "$(dirname "$0")/.."

DB="${1:-SintroSeed}"
PASSWORD="${MSSQL_SA_PASSWORD:-Sintro_Dev_2026!}"

./scripts/db-up.sh

# -f 65001: the generated file is UTF-8 (umlauts in names), which sqlcmd does not assume.
sql() {
    docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$PASSWORD" -C -d "$1" -b -f 65001 -i /dev/stdin
}

# The sessions are dated relative to today, taken from this machine's clock so the day boundary
# is the range's, not the container's UTC. Override with SEED_TODAY=YYYY-MM-DD.
SEED_TODAY="${SEED_TODAY:-$(date +%F)}"

echo "generating rows from seed/definitions.mjs, today = $SEED_TODAY"
docker run --rm -e SEED_TODAY="$SEED_TODAY" -v "$PWD:/src" -w /src node:24-alpine node seed/generate.mjs > seed/.generated.sql

echo "recreating database $DB"
printf "IF DB_ID('%s') IS NOT NULL BEGIN ALTER DATABASE [%s] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [%s]; END; CREATE DATABASE [%s];" "$DB" "$DB" "$DB" "$DB" | sql master
sql "$DB" < seed/schema.sql
sql "$DB" < seed/.generated.sql

echo
docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PASSWORD" -C -d "$DB" -W -s'|' -Q \
    "SELECT (SELECT COUNT(*) FROM dbo.Club) AS clubs, (SELECT COUNT(*) FROM dbo.Shooters) AS shooters,
            (SELECT COUNT(*) FROM dbo.Programs) AS programs, (SELECT COUNT(*) FROM dbo.Shots) AS shots"
echo "seeded $DB"
