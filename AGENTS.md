# AGENTS.md

Blueprint for coding agents. Keep in sync; max 10 000 chars, aim for 8 000.

## Rules

1. **Read a file before editing it**, never edit from memory.
2. **No real personal data anywhere.** `.db/` is gitignored and stays so; device exports hold real
   names and licences. Fixtures, tests and docs use invented ones (`Hans Muster`, `012345`).
3. **This API is read-only.** No endpoint or query may write to the device database.
4. **Never weaken the security defaults** (private-only CIDR allowlist, mandatory token, no
   trusted proxy) to make something work. Fix the cause.

## What this is

A read-only HTTP API and plain-HTML viewer over the **Sintro 300 Hit Target Display** (300m
targets, MSSQL Express), hiding its schema from event software.

**Read `docs/device-database.md` before touching a query**; `docs/architecture.md` orients.
`docs/api.md` is the consumer guide; changing `Api/V2/` or `Domain/` updates it.

## Toolchain: all in Docker

No .NET, SQL Server or Node on the host. Everything is under `scripts/`:

| Command | Does |
|---|---|
| `db-up.sh` | Dev SQL Server (`localhost:11433`, `sa` / `Sintro_Dev_2026!`) |
| `db-restore.sh` | Restore `.db/` `.bak` exports |
| `db-seed.sh` | Invented database from `seed/` (`SINTRO_DB=SintroSeed` to test against it) |
| `sql.sh "SELECT …"` | Ad-hoc query |
| `test.sh` / `test-web.sh` | Full suite / viewer only |
| `run.sh` | Run on <http://localhost:8080> |
| `dotnet.sh <args>` | Any `dotnet` command |
| `publish-win.sh` | `win-x64` exe → `bin/` |

`.container-home/` is HOME and NuGet cache. Solution: `.slnx` (.NET 10). Dev "today":
`Sintro__ReferenceDate`.

## Architecture

```
src/Sintro.ResultViewer.Api/
  Program.cs          composition root; middleware order matters (see Security)
  OperatorSettings.cs loads appsettings.jsonc    StartupChecks.cs  refuses weak config
  Domain/             the device-derived model   Api/ApiError.cs   the one error shape
  Api/V2/             VERSION-SPECIFIC: V2Endpoints (routes, tags), V2Descriptions, V2Query, V2Contracts
  Data/               ISintroRepository, filters, Cursor, LicenseNumber, SintroClock
    Sintro300/        EVERYTHING that knows the device schema, one partial file per entity.
                      Another device: sibling folder implementing ISintroRepository
  Security/           NetworkGate (CIDR) → TokenAuth (bearer) → SessionToken; ClientAddress, IpRange
  Live/               LaneWatcher (polls) → LiveHub (WebSocket fan-out); LanesFrame
  Viewer/             ViewerPage (injects session token), ViewerEndpoints
  wwwroot/core/       PURE: i18n, format, markup, sectors, lanes, viewmode, display, boards,
                      browse, openapi, reconnect, http.   wwwroot/tests/ runs under node
  wwwroot/            DOM: app.js, boards-dialog.js, browse.js, docs.js, dom.js, api.js
tests/Sintro.ResultViewer.Tests/
```

**Layering:** whatever is testable without a browser belongs in `wwwroot/core/`; only the DOM
layer touches the DOM. **Versioning:** only `Api/V2/` is version-specific; v3 is a new folder
plus `app.MapV3()`.

## Single sources of truth

- **SQL** → `Data/Sintro300/SintroRepository*.cs`. **Scoring** → `ScoreCalculator.cs`.
  **Target letters** → `TargetKind.cs` (`0=A`, `1=B`, `3=S`). **Hit sectors** → `core/sectors.js`.
- **Translations** → `core/i18n.js` (`de` default, `fr`; `en` only for `/docs`).
  `data-i18n[-title|-aria-label]` in HTML, `t('key', {params})` in JS; tests assert `de`/`fr`
  keys are identical, every key used, every used key defined.
- **Colours** → `wwwroot/tokens.css` from the
  [design system](https://github.com/Schiesssport/design-system) plus a marked local block.
  No hex in `styles.css`; tints via `color-mix()`.
- **JSON options** → `SintroJson.Options`. **Display limits** → `core/display.js`.
- **Config** → `SintroOptions.cs` + `appsettings.jsonc` (every setting explained inline; loaded
  by `OperatorSettings.cs` so environment variables still win).
- **Docs ordering** → numbered English OpenAPI tags in `V2Endpoints.cs`, sorted numerically.

## Vocabulary

Code is **English**; UI strings **German** (default) and French; `/docs` is English apart from
its intro. A row of `dbo.Programs` is one shooter's pass at the target: a **`program`** in code,
API and docs, **"Stich"** in German UI (French "cible"); a **series** is a **"Passe"**. The C#
type is `ShootingProgram`. **Never call it a `match`** and never call a program a Passe.

## Mapping rules you must not undo

The integration tests assert these.

| Rule | Why |
|---|---|
| Marker rows are `ShotNr 9999` only, never `TotalType 7` | The last real shot carries `TotalType 7` too |
| Sighting = `ShotType = 0`, **never** `ShotGroup = 0`; `sighting` is a list per group | Counting shots occur in group 0 |
| Valuation per `(ProgramID, ShotGroup)`, highest `TargeinformationID` wins | Duplicate rows disagree |
| `totals` has one entry per target and scale, never one sum across scales | 5er + 10er is meaningless |
| Never join on `Shots.StartNr`; use `ProgramID → Programs.ShooterID` | Almost always zero |
| `Shooters.StartNr` **is the licence**, no length cap | Six digits, zero-padded; 7-9 planned |
| `shooter: null` is normal | Only a logged-in shooter is recorded |
| Parse `StartTime` as `dd.MM.yyyy-HH:mm:ss`, emit ISO 8601 | Day-first text |
| `targetCode`/`targetTitle` = `Programs.Number`/`Name`, free text | Operators rename programs |
| `matchCode` = `Shots.ExternalNumber`, `0` → `null` | The event Stich, per shot |
| `hitSector` 1 is twelve o'clock, **clockwise** in 45° steps; 0 = no direction | Mean `atan2` per sector |
| Nulls are serialized, never omitted | `shooter`/`currentProgram` null are documented states |
| Cursor paging, never offset, no `total`; `state=finished` keyed by finishing order | A late-ending pass must arrive after a sync cursor |
| No date filter lists every day; `dates=today` is resolved by the server | Consumers never compute the date |
| A pass is `Active`, `Finished` **or `Abandoned`** | Off the line with no end total is neither |
| Reject an unknown filter value or cursor (`400`), never ignore it | A typo must not widen the answer |
| Every non-2xx body is `ApiError {error, detail}` | The viewer has one error parser |
| Broadcast by enqueueing, never awaiting a socket | One stalled display must not block the rest |

Viewer rules that fail silently if broken (details in `docs/architecture.md`): the shooter
fallback chain name → licence → `contestShooterName` → `Linie N · HH:mm` lives in
`core/format.js`; the shown result is derived from `series[]`, never `totals[]` (4er + 5er and
same-scale targets add up on purpose); asset URLs are absolute (`/app.js`); column widths sit on
`<colgroup>`; the ticker DOM is rebuilt only when `tickerContentKey` changed; `/browse` filters
are API queries, only row shape, sort and result range live in the page.

## Security model

`NetworkGate` (CIDR, **before auth**) → CORS (**only under `/api`**) → `UseWebSockets` →
`TokenAuth` (bearer, SHA-256 digests, constant-time) → `SessionToken` (per start, in memory,
injected into the viewer pages). Each bullet has a test that fails if it is broken:

- **Tokens are arrays with scopes** (`ApiReadTokens`/`ApiWriteTokens`, write implies read); none
  configured is valid. `Authorization: Bearer` is the only header; `?token=` only on a genuine
  upgrade to `/live` (`LiveFeedTests`). Startup refuses short, non-ASCII or whitespace tokens.
- **`TrustedProxies` is a list**: `X-Forwarded-For` is unwound only through listed hops; a hop that
  does not parse resolves to *no* client and is refused (`ClientAddressTests`).
- **CORS never covers the viewer pages**: they embed the session token, and the same-origin
  policy keeps it there (`SecurityTests`).
- **`/openapi/v2.json` is token-free**; no real licence or name in a description (tested).
- **A null remote address is loopback.** Non-private ranges are allowed but warned about at
  startup and in `health.publicExposure`. **Allowlists have no code default**; empty is refused.

## After any change

1. `scripts/test.sh` stays green, also with `SINTRO_DB=SintroSeed SINTRO_REFERENCE_DATE=`.
2. Touched a query or mapping rule? Check with `scripts/sql.sh`. Tests assert **invariants, not
   counts**: never hard-code a total, name or id from one export.
3. Touched `wwwroot/`? `scripts/run.sh`, load `/`, each `/fullscreen/*`, `/browse`, `/docs`.
4. New translation key? Both `de` and `fr`. Learned schema facts? `docs/device-database.md`.

## Style

Simplicity first, also over small optimisations. Comments are short to non-existent: names
explain *what*, a comment is one line for a *why* that cannot be inferred. Functions stay about
25 lines; split rather than comment sections. YAGNI. No emojis.
