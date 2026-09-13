# The Sintro device database

A working reference for contributors: what the Sintro 300 hit-target display stores, what the
columns actually mean, and which of them lie. **Read this before writing or changing a query** —
almost every rule in `Data/SintroRepository.cs` and `Data/ScoreCalculator.cs` exists because of
something on this page.

The schema is the device manufacturer's, not ours. It is undocumented, so everything here was
derived by reading exported databases from real installations. Where a meaning is inferred rather
than confirmed, it says so.

> **Scope.** Content varies per installation: the number of lines, which programs a club shoots,
> which targets it owns, how many shooters are registered. Nothing here should be read as "the data
> looks like X" — only as "the schema means X". Where a claim rests on observation, it is marked.

## How to look for yourself

```bash
scripts/db-restore.sh                  # restore .bak exports placed in .db/ into a dev SQL Server
scripts/sql.sh "SELECT TOP 10 * FROM Programs ORDER BY ProgramID DESC"
```

`.db/` is gitignored and must stay that way: device exports contain shooters' real names and
licence numbers.

## Glossary

The implementation is written in English, the UI in German and French, and the device uses its own
third set of names. This table is the mapping. Use the **English** column in code, and never invent
a synonym for something already listed.

| English (code & API) | German (UI) | In the device DB | Notes |
|---|---|---|---|
| program | Passe | `Programs` (one row) | One shooter shooting one program on one line, once |
| program number | Programmnummer | `Programs.Number` | Operator-assigned; not a stable identifier |
| line | Linie | `Lanes.Number`, `Programs.LaneNr` | A firing point. Count is site-specific |
| shooter | Schütze | `Shooters` | Optional — most passes have none |
| club | Verein | `Club` | Swiss club register, numbered `1.01.0.01.005` |
| licence number | Lizenznummer | `Shooters.StartNr` | **Not** a start number, despite the column name |
| series | Serie | `Shots.ShotGroup` | A block of shots that gets its own subtotal |
| sighting shot | Probeschuss | `Shots.ShotType = 0` | Never counts towards a total |
| counting shot | Wertungsschuss | `Shots.ShotType = 1` | |
| precision stage | Einzelfeuer (EF) | `Shots.FireMethod = 1` | See *Fire method*, unverified |
| rapid-fire stage | Serienfeuer (SF) | `Shots.FireMethod = 2` | See *Fire method*, unverified |
| shot value | Trefferwert | `Shots.PrimaryResult` | Ring value in the active valuation |
| fine value | Zehntelwert | `Shots.SecondaryResult` | 0–100, the decimal ring |
| mouche | Mouche | `Shots.Mouche` | Centre hit |
| hit sector | Trefferlage | `Shots.HitPosition` | Clock direction of the hit |
| valuation | Wertung | `Targetinformation.TargetValuation` | Ring scale: 4, 5, 10 or 100 |
| target | Scheibe | `Targetinformation.TargetType` | A, B or Sau silhouette |
| target code | — | derived | `A10`, `B4`, `S10`: target letter + valuation |
| total | Total / Resultat | derived | Sum of the counting shots |
| subtotal | Zwischentotal | derived | Sum of one series |

## The two databases

| Database | Holds | Used by the viewer |
|---|---|---|
| `DBSINTRO300` | Shooters, clubs, passes, shots, target settings | **Yes — everything** |
| `ANLAGE` | Hardware statistics and a service log for the target frames | No |

`ANLAGE` has appeared empty in every export examined — counters at zero, service rows blank. It is
presumably written by a maintenance function nobody uses. Left alone.

Neither database contains stored procedures, views or functions. All behaviour lives in the device
application, which is why the tables are shaped the way they are.

## Tables

| Table | Meaning |
|---|---|
| `Club` | The Swiss club register, shipped with the device |
| `Shooters` | Shooters registered on this installation |
| `Programs` | **One row per pass** — the central table |
| `Shots` | Individual shots, plus some rows that are not shots (see below) |
| `Targetinformation` | Per (pass, series): which target and which ring scale applied |
| `Lanes` | The installation's firing points, and what is on each right now |
| `AnnualProg`, `JungschuetzenProg` | Per-club annual / junior program configuration. Unused by the viewer, and empty in the exports examined |

```
Club ──< Shooters ──< Programs ──< Shots
                          │
                          ├──< Targetinformation   (ProgramID + ShotGroup)
                          └──< Lanes               (the pass currently on a line)
Club ──< AnnualProg, JungschuetzenProg
```

All of these relationships are enforced by foreign keys.

## Reading a pass

This is the part that matters. Each rule below is implemented in `Data/ScoreCalculator.cs` or
`Data/SintroRepository.cs`; changing one without reading its justification will produce results
that look plausible and are wrong.

### Not every row in `Shots` is a shot

The device writes synthetic marker rows to close a pass: `ShotNr = 9999`, `TotalType = 7`,
`HitPosition = 255`, all values zero. **Filter them out before scoring.** A pass can consist of
nothing but a marker — started and abandoned — and must then report no result rather than a zero.
**`ShotNr = 9999` is the only test.** `TotalType = 7` is not: the last real shot of a pass carries
it as well (measured in one export: 645 real shots with `TotalType 7` and ring values up to 10,
against 438 markers). Filtering on the flag would drop every final shot.

The marker is also the only place the finishing time is recorded, so read `ShotTime` from it
*before* discarding it.

### Sighting shots are identified by `ShotType`, never by `ShotGroup`

`ShotType = 0` is a sighting shot (Probe), `1` is a counting shot. That flag is the authority.

It is tempting to assume series 0 is "the sighting series" — it often is — but exports contain both
counting shots inside `ShotGroup = 0` and sighting shots in higher groups. A program may have no
sighting stage at all, in which case the first counting series is `ShotGroup 0`. Grouping on the
group number would silently drop real results or count practice shots towards a total.

### The ring scale is per series and can change inside one pass

`PrimaryResult` is meaningless on its own: it is the ring value *in whatever valuation was active*,
which is 1–4, 1–5, 1–10 or 1–100 depending on the target setting. That setting lives in
`Targetinformation`, keyed by `(ProgramID, ShotGroup)`.

It can differ between series of the same pass — the device supports switching target and valuation
mid-pass, and programs exist that do exactly that. Therefore:

- compute a **subtotal per series**, always;
- compute a **grand total only when every counting series shares one valuation**. Adding a 5er
  series to a 10er series produces a confident-looking wrong number. The API returns `total: null`
  with a `totalUnavailable` reason instead.

`Targetinformation` may hold several rows for the same `(ProgramID, ShotGroup)`. They almost always
agree; where they do not, **the highest `TargeinformationID` is current**.

### Dates are text, in day-first format

`Programs.StartTime` is a `varchar` shaped `dd.MM.yyyy-HH:mm:ss`. It sorts meaninglessly as text and
must be parsed. `Shots.ShotTime` is a bare time of day with no date, so a shot's real timestamp is
the pass's date plus that time — and a pass that runs past midnight needs the rollover handled.

`Shots.TimeSinceNewYear` holds centiseconds since 1 January and is a usable cross-check.

The device stores local wall-clock time with no zone. The API attaches the configured range
timezone (`Sintro:TimeZone`) and emits ISO 8601.

### A pass usually has no shooter

Identifying yourself is optional, and a barcode can be misread, so `Programs.ShooterID` is null for
the large majority of passes. This is normal operation, not corrupt data. A result must remain
usable regardless: line number and start time always identify a pass.

`Shots.StartNr` looks like it would help and does not — see the column notes.

## Fire method — the shooting stage

Swiss programs are built from stages, and their names say so: `A10-EF6-SF4` is six shots
*Einzelfeuer* (precision, one shot at a time) followed by four *Serienfeuer* (rapid fire, a burst
within a time limit). Which stage a shot belongs to changes how a result should be read and shown.

`Shots.FireMethod` is almost certainly that stage. The **proposed** mapping is:

| Value | Stage | German |
|---|---|---|
| `0` | sighting stage | Probe |
| `1` | precision stage | Einzelfeuer (EF) |
| `2` | rapid-fire stage | Serienfeuer (SF) |

**This is unverified.** A good check is to correlate `FireMethod` against programs whose names
declare their stages, and against `ShotType` for the sighting case. Until someone does, the viewer
does not display the stage. See *Open questions*.

Not to be confused with `BreakMode`, which is about *when* shooting is allowed rather than how:
`BreakMode = 1` means open fire — free shooting outside any match, typically while sighting in.
Irrelevant to results, and unused here.

## Column notes

### `Programs`

| Column | Notes |
|---|---|
| `Number`, `Name` | The operator can rename a program freely, so the same number appears under several names and the same name under several numbers. Treat `(Number, Name)` as free text, never as a key |
| `StartTime` | `varchar`, `dd.MM.yyyy-HH:mm:ss` |
| `LaneNr` | The line the pass was shot on |
| `ShooterID` | Null for most passes |
| `ContestShooterName` | Free text. Empty in every export examined — **TODO:** the device has a *ContestMode*; check whether that is what fills this field, and whether the viewer should prefer it when set |

### `Shots`

| Column | Notes |
|---|---|
| `ShotType` | `0` sighting, `1` counting. **The authority for that distinction** |
| `ShotGroup` | Series index. See the rule above — not a reliable sighting marker |
| `ShotNr` | Sequential within the pass. `9999` marks a synthetic row |
| `PrimaryResult` | Ring value in the active valuation; `0` is a miss |
| `SecondaryResult` | Fine value 0–100, bracketed by `PrimaryResult` |
| `Mouche` | `1` = centre hit, always paired with `HitPosition = 0` |
| `HitPosition` | Hit sector: `1`–`8` clockwise from twelve o'clock in 45° steps, `0` centre, `255` none reported. Derived from `X`/`Y`, whose mean angle per sector lands on 90°, 45°, 0°, −45°, −90°, −135°, 180°, 135° |
| `X`, `Y` | Hit coordinates in 1/100 mm from the centre. `Y` positive is up |
| `TotalType` | `0` ordinary shot, `1` last shot of a series, `7` end of pass — set on the last real shot **and** on the marker row that follows it |
| `FireMethod` | The shooting stage. **Proposed, unverified:** `0` sighting stage, `1` precision stage (Einzelfeuer), `2` rapid-fire stage (Serienfeuer). This matters for display — program names encode it as `EF`/`SF` — so it is worth confirming. **TODO** |
| `BreakMode` | `1` means open fire: free shooting outside any match, e.g. while sighting in. Irrelevant to results and not used by the viewer |
| `ShotTime` | Time of day only, `HH:mm:ss.ff` |
| `TimeSinceNewYear` | Centiseconds since 1 January |
| `StartNr` | **Do not join on this.** Nominally the shooter's number, but it is zero on almost every row and rarely matches the pass's shooter. Always go `Shots.ProgramID → Programs.ShooterID`. **TODO:** possibly populated only in *ContestMode* — worth checking before assuming it is useless |
| `TargetType` | A `varchar`, unrelated to `Targetinformation.TargetType`, and not used |
| `GunType`, `ShotPosition`, `InsDel`, `InTime`, `LogEvent`, `LogType`, `ExternalNumber` | Device internals. Not interpreted |

### `Targetinformation`

| Column | Notes |
|---|---|
| `TargetValuation` | Ring scale: `4`, `5`, `10` or `100` |
| `TargetType` | The target: `0` = A, `1` = B, `3` = Sau silhouette. Confirmed by a clean correlation with the `A…`/`B…` prefixes operators put in program names. Mapped in `Data/TargetKind.cs`, the single place to extend |

Target and valuation combine into the notation the sport already uses — `A10`, `B4`, `A100`,
`S10` — which is what the API exposes as `targetCode`.

### `Shooters` / `Club`

| Column | Notes |
|---|---|
| `StartNr` | **The SSV licence number**, not a start number: six digits, zero-padded, non-sequential. A 7–9 digit format is planned, so never assume six. Carries no unique constraint, so a collision is possible and the API reports duplicates rather than guessing |
| `RFID` | **Deprecated.** Card identification never saw real use; installations share a single all-zero placeholder across many shooters. Registration happens by barcode on the licence number. The viewer surfaces the field but never keys on it, and nothing should be built on it |
| `Club.ClubID` | The club number as an integer: `102104133` ↔ `1.02.1.04.133` |

### `Lanes`

One row per firing point. `ProgramID` is the pass currently loaded on it, or null. The row persists
after a pass ends, so "on a line" does not mean "being shot" — see how the viewer derives
availability in `wwwroot/core/lanes.js`.

## Consequences for the API

1. **The pass is the primary entity**, not the shooter. Most passes are anonymous, so a
   shooter-keyed model would show almost nothing.
2. **The device database is a rolling window.** `ProgramID` keeps climbing while old rows are
   pruned, so historical features need their own store — this API cannot promise yesterday's data
   still exists. `ProgramID` is nevertheless a sound paging key: it is an identity column and
   advances in step with `StartTime`.
3. **The API is read-only.** The device owns this database; nothing here may write to it.

## Open questions

Contributions very welcome — each of these needs someone with device access to check.

- **ContestMode.** What does it change? Does it fill `Programs.ContestShooterName`, and does it
  populate `Shots.StartNr`? Both would be useful if reliable.
- **`FireMethod`.** Confirm `0` sighting / `1` precision / `2` rapid-fire, ideally against a program
  whose name states its stages (`A10-EF6-SF4` = six precision, four rapid-fire). Once confirmed, the
  viewer should show the stage.
- **`TotalType`.** `0`, `1` and `7` are understood. Are there other values on installations that use
  program types we have not seen?
- **`GunType`.** Mostly one value with occasional outliers. Rifle class, or something else?
