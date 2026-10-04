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
scripts/db-seed.sh                                            # (re)create SintroSeed, dated up to today
SINTRO_DB=SintroSeed SINTRO_REFERENCE_DATE= scripts/test.sh   # run the suite against it
SINTRO_DB=SintroSeed SINTRO_REFERENCE_DATE= scripts/run.sh    # view it on http://localhost:8080
```

The sessions are dated relative to the day the seed runs (the last seven days, the event day
being today), so the viewer shows live-looking data. The export workflow pins "today" to the
export's last shooting day with `Sintro__ReferenceDate`; an empty `SINTRO_REFERENCE_DATE` turns
that off so the API and the tests use the real date. `SEED_TODAY=YYYY-MM-DD` dates a seed
elsewhere.

## What the draft contains

- 3 clubs, 3 shooters each.
- Shot values from a real shooter's distribution per 100 shots: 15 at 96 or better, 25 at 91–95,
  40 at 81–90, 16 at 71–80, one 0 (wrong target), 3 anywhere from 1 to 70. Every shooter shoots
  the same distribution for now.
- The 7 programs below and no others, in the operator's notation, one token per stage
  (`A5P2 A5E2 A5S3 A5S3` = two sighting shots, two precision, two series of three), parsed by
  `parseStages`. Numbered by family so they are easy to remember: 01x A5, 02x A10, 03x A100,
  07x B4, 09x special. 91 changes target and valuation mid-pass, so the API withholds its grand
  total; the eight-series A100 program (31) is stopped after 4, 5 or all 8 series (`stopAfter`
  on the pass).
- 5 sessions in the last seven days, about 60 Stiche in all, roughly one in ten anonymous,
  every program shot by at least three different named shooters: four training evenings and an
  event day today. Event Stiche carry the program family with a leading 1 as match
  code (111, 121, 122, 131, 171, 191). The event day also holds an abandoned pass (marker only)
  and a pass still active on a lane. Lanes are assigned by the generator, next free lane first.

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

- Sighting shots. They are marked by `ShotType 0` and nothing else, which is also what the API
  keys on. Whether the device puts them in their own `ShotGroup` and how it numbers the groups
  of a program without a sighting stage is unverified; a test on the device is planned.
- Shot time spacing (20 s precision, 2.5 s rapid fire) and the 45 s pause before the first shot
  look right but are not measured.
- The ring value is `ceil(fine / (100 / valuation))`: 91–100 is a 10 on an A10 target, 81–100 a
  5 on an A5. On a 100er target the fine value is the ring value.
