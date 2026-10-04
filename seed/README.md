# Seeded device database

A Sintro 300 database built from invented data, so development, the integration tests and CI
do not depend on a real export (which holds real shooters' names and can never be committed).

```
seed/schema.sql        the six tables the viewer reads, as the device creates them
seed/definitions.mjs   WHAT the range looks like: clubs, shooters, programs, sessions
seed/generate.mjs      HOW the device would have written it: one pass → rows in Programs,
                       Targetinformation, Shots (incl. the 9999 end marker), Lanes
scripts/db-seed.sh     builds the database SintroSeed (or any name you pass) in the dev SQL Server
```

```bash
scripts/db-seed.sh                      # (re)create SintroSeed from the definitions
SINTRO_DB=SintroSeed scripts/test.sh    # run the suite against it
SINTRO_DB=SintroSeed scripts/run.sh     # view it on http://localhost:8080
```

## What the draft contains

- 3 clubs, 3 shooters each, with a skill level that drives their shot values.
- 8 programs in the operator's own notation (`A10-EF6-SF4`, `Feldschiessen`, …), each a list of
  stages: sighting (Probe), precision (EF), rapid fire (SF), with target letter and valuation.
  One program changes valuation mid-pass so the API's "no grand total" rule has data.
- 2 sessions: a training evening a week ago (no match code) and an event day on the reference
  date `2026-07-08` (match code 12), with anonymous passes, an abandoned pass (marker only) and a
  pass still active on a lane.

## Device conventions reproduced

From `docs/device-database.md`; where that file says "unverified", so is the generator.

- `StartTime` as `dd.MM.yyyy-HH:mm:ss`, `ShotTime` as `HH:mm:ss.ff`, `TimeSinceNewYear` as
  centiseconds since 1 January.
- One `Targetinformation` row per `(program, ShotGroup)`; stages are groups in order.
- Sighting shots: `ShotType 0`, `BreakMode 1`, `FireMethod 0`; counting shots `ShotType 1`.
- `TotalType`: `1` on the last shot of a series, `7` on the last real shot **and** on the marker.
- Marker row: `ShotNr 9999`, `HitPosition 255`, zero values.
- `HitPosition` 1–8 clockwise from twelve o'clock, `0` centre; `X`/`Y` placed on that sector.
- `ExternalNumber` is the session's match code on every shot, `0` on training days.
- `Shots.StartNr` is always `0`, `GunType 1`, `ShotPosition 9`, `LogType 3`, `InTime 1`,
  `InsDel 0`, as in the exports examined.

## Where real-world logic goes

Everything that decides *what* is shot belongs in `definitions.mjs`: real program structures
(series lengths, time limits are not modelled), which programs a club shoots on which day, how
many anonymous passes, duplicate licences, a shooter in two clubs. Everything that decides *how
the device writes it* belongs in `generate.mjs`. Keep the two apart: the tests assert the
device's rules, and those must not drift with the scenario.

Open questions the draft guesses at:

- Shot time spacing (20 s precision, 2.5 s rapid fire) and the 45 s pause before the first shot.
- Score distribution: a Gaussian on the fine value per shooter. Real misses and real spreads per
  position (liegend, kniend) are not modelled.
- Whether the device numbers sighting groups first or last when a program has no sighting stage.
