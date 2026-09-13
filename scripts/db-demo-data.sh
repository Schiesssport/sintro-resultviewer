#!/usr/bin/env bash
# DEV ONLY — patches the restored dev database so the viewer shows cases the real
# backup happens not to contain.
#
# The 8 July session was shot on 5er and 10er targets with no maximum scores, so no
# shot in the export is a 10 and none is a mouche. Without at least a few, the centre
# ring and the best-fine-value trailer cannot be seen at all.
#
# Turns selected misses into centre tens. Re-run scripts/db-restore.sh to undo.
# Never point this at the range PC.
set -euo pipefail
cd "$(dirname "$0")/.."

PASSWORD="${MSSQL_SA_PASSWORD:-Sintro_Dev_2026!}"

sql() {
    docker compose exec -T db /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$PASSWORD" -C -d DBSINTRO300 -W -s'|' -Q "$1"
}

# Chosen deliberately: all sit in 10er series, and none belongs to a program whose
# score the integration tests assert (2000, 1978).
sql "
UPDATE dbo.Shots
   SET PrimaryResult = 10, SecondaryResult = 99, Mouche = 1, HitPosition = 0
 WHERE ShotID IN (10574, 10543, 10516, 10507, 10472);

UPDATE dbo.Shots
   SET PrimaryResult = 10, SecondaryResult = 96, Mouche = 0, HitPosition = 2
 WHERE ShotID IN (10575, 10544, 10517);
"

echo
sql "SELECT ShotID, ProgramID, ShotNr, PrimaryResult, SecondaryResult, Mouche
     FROM dbo.Shots WHERE ShotID IN (10574,10543,10516,10507,10472,10575,10544,10517)
     ORDER BY ShotID"
echo
echo "demo data applied — run scripts/db-restore.sh to revert"
