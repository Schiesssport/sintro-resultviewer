#!/usr/bin/env bash
# Restore the Sintro device backups into the dev SQL Server.
#
# Expects the two .bak exports in .db/ (mounted read-only at /backups in the
# container). They are deliberately not committed — they hold real shooters'
# names, licence numbers and RFID ids. Keep a copy outside the repo, otherwise
# the db-data volume becomes the only remaining copy of the test data.
set -euo pipefail
cd "$(dirname "$0")/.."

PASSWORD="${MSSQL_SA_PASSWORD:-Sintro_Dev_2026!}"

sintro_bak="$(ls .db/*dbsintro300*.bak 2>/dev/null | head -1 || true)"
anlage_bak="$(ls .db/*anlage*.bak      2>/dev/null | head -1 || true)"

if [[ -z "$sintro_bak" ]]; then
    echo "No *dbsintro300*.bak found in .db/ — nothing to restore." >&2
    exit 1
fi

./scripts/db-up.sh

sql() {
    docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$PASSWORD" -C -Q "$1"
}

restore() {
    local db="$1" file="/backups/$(basename "$2")"
    echo "restoring $db from $file"
    sql "RESTORE DATABASE [$db] FROM DISK='$file' WITH REPLACE,
           MOVE '$db'     TO '/var/opt/mssql/data/$db.mdf',
           MOVE '${db}_log' TO '/var/opt/mssql/data/${db}_log.ldf'" >/dev/null
}

restore DBSINTRO300 "$sintro_bak"
[[ -n "$anlage_bak" ]] && restore ANLAGE "$anlage_bak"

echo
sql "SELECT DB_NAME() ,(SELECT COUNT(*) FROM DBSINTRO300.dbo.Programs) AS programs,
     (SELECT COUNT(*) FROM DBSINTRO300.dbo.Shots) AS shots,
     (SELECT COUNT(*) FROM DBSINTRO300.dbo.Shooters) AS shooters"
