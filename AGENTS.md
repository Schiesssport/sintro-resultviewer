# AGENTS.md

Blueprint for coding agents. Keep in sync; max 10 000 chars.

## Rules

Non-negotiable.

1. **Always read a file before editing it** — never edit from memory.
2. **No real personal data anywhere.** `.db/` is gitignored and stays so; device exports hold real
   shooters' names and licences. Fixtures, tests and docs use invented ones (`Hans Muster`).
3. **This API is read-only.** No endpoint or query may write to the device database.
4. **Never weaken the security defaults** (private-only CIDR allowlist, mandatory token,
   `TrustProxy: false`) to make something work. Fix the cause.

## What this is

A read-only HTTP API and plain-HTML viewer over the **Sintro 300 Hit Target Display** (300m
targets, MSSQL Express), hiding its schema from event software.

**Read `docs/device-database.md` before touching a query**; `docs/architecture.md` orients.
`docs/api.md` is the consumer guide; changing `Api/V2/` or `Domain/` updates it.

## Toolchain: all in Docker

No .NET, SQL Server or Node on the host; Docker is dev only.

| Command | Does |
|---|---|
| `db-up.sh` | Dev SQL Server (`localhost:11433`, `sa` / `Sintro_Dev_2026!`) |
| `db-restore.sh` | Restore `.db/` `.bak` exports |
| `db-seed.sh` | Invented database from `seed/` |
| `sql.sh "SELECT …"` | Ad-hoc query |
| `test.sh` | Full suite |
| `test-web.sh` | Viewer only |
| `run.sh` | Run on <http://localhost:8080> |
| `dotnet.sh <args>` | Any `dotnet` command |
| `publish-win.sh` | `win-x64` exe → `bin/` |

All under `scripts/`. `.container-home/` is HOME and NuGet cache. **Solution:
`.slnx`** (.NET 10). Dev "today": `Sintro__ReferenceDate`.

## Architecture

```
src/Sintro.ResultViewer.Api/
  Program.cs        composition root
  OperatorSettings.cs  loads appsettings.jsonc
  StartupChecks.cs  refuses a weak token; warns on public exposure
  StartupBanner.cs  display URLs
  Domain/           the device-derived model
  Api/ApiError.cs   the one error shape
  Api/V2/           VERSION-SPECIFIC: V2Endpoints (routes, tags), V2Descriptions, V2Query,
                    V2Contracts (wire envelopes)
  Data/             ISintroRepository, filters, Cursor, LicenseNumber, SintroClock
    Sintro300/      EVERYTHING that knows the device schema; one partial file per entity.
                    Another device: sibling folder implementing ISintroRepository
  Security/         NetworkGate (CIDR) → TokenAuth (bearer) → SessionToken; QueryTokenOnUpgrade
  Live/             LaneWatcher (polls) → LiveHub (WebSocket fan-out); LanesFrame
  Viewer/           ViewerPage (injects session token), ViewerEndpoints
  wwwroot/          core/ = PURE: i18n, format, markup, sectors, lanes, viewmode, display,
                    boards, browse, openapi, reconnect. DOM: app.js, boards-dialog.js,
                    browse.js, docs.js, dom.js, api.js; tests/
tests/Sintro.ResultViewer.Tests/
```

**Layering:** whatever is testable without a browser belongs in `wwwroot/core/`; only the DOM
layer touches the DOM.

**API versioning.** Only `Api/V2/` is version-specific: v3 is a new folder plus `app.MapV3()`.

## Single sources of truth

- **SQL** → `Data/Sintro300/SintroRepository*.cs`; a query elsewhere is a bug.
- **Scoring** → `Data/Sintro300/ScoreCalculator.cs`. **Hit sectors** → `wwwroot/core/sectors.js`.
- **Translations** → `wwwroot/core/i18n.js` (`de` default, `fr`, `en` for the docs page
  only). `data-i18n[-title|-aria-label]` in HTML, `t('key', {params})` in JS. Tests assert identical
  `de`/`fr` keys, every key used, every used key existing.
- **Colours** → `wwwroot/tokens.css`, from the
  [design system](https://github.com/Schiesssport/design-system) plus a marked local
  block at the end. Never hard-code a hex in `styles.css`; derive tints via `color-mix()`.
- **JSON options** → `SintroJson.Options`, shared by endpoints, live hub and tests.
- **Config** → `SintroOptions.cs` + `appsettings.jsonc` (operator-facing, every setting explained
  inline; registered by hand in `OperatorSettings.cs`, inserted among the JSON sources so
  environment variables still win).
- **Target letters** → `Data/Sintro300/TargetKind.cs` (`0=A`, `1=B`, `3=S`).
- **Display settings and limits** → `wwwroot/core/display.js`.
- **Docs ordering** → numbered OpenAPI tags in `Api/V2/V2Endpoints.cs`, sorted numerically.

## Vocabulary

Code is **English**; UI strings **German** (default) and French.

A row of `dbo.Programs` is one shooter's pass at the target: a **`program`** in code and API,
**"Stich"** in German UI (French "cible"); a **series** is a **"Passe"**. The C# type is
`ShootingProgram` (`Program` is the entry point). **Never call it a `match`.**

## Mapping rules you must not undo

The integration tests assert these rules.

| Rule | Why |
|---|---|
| Marker rows are `ShotNr 9999` only, never `TotalType 7` | The last real shot carries `TotalType 7` too; filtering on it drops every final shot |
| Sighting = `ShotType = 0`, **never** `ShotGroup = 0`; `sighting` is a list per group | Counting shots occur in group 0; merged groups would add 5er to 10er |
| Valuation per `(ProgramID, ShotGroup)`, highest `TargeinformationID` wins | Duplicate rows exist and can disagree |
| `Totals` has one entry per target/scale, never one sum across scales | 5er + 10er is meaningless |
| Never join on `Shots.StartNr` — use `ProgramID → Programs.ShooterID` | Almost always zero |
| `Shooters.StartNr` **is the licence number**, no length cap | Six digits, zero-padded; 7-9 digits planned |
| `shooter: null` is normal | Only a logged-in shooter is recorded |
| Parse `StartTime` as `dd.MM.yyyy-HH:mm:ss`, emit ISO 8601 | Day-first text; its order is meaningless |
| `targetCode`/`targetTitle` = `Programs.Number`/`Name`, free text | Operators rename programs |
| `matchCode` = `Shots.ExternalNumber`, `0` → `null` | The event Stich, per shot |
| `TargetType` is the Scheibe: `0`=A, `1`=B, `3`=S (Sau) | Matches the A/B prefixes in program names |
| `hitSector` 1 is twelve o'clock, **clockwise** in 45° steps | Derived from the mean `atan2(y,x)` per sector |
| Nulls are serialized, never omitted | `shooter`/`currentProgram` null are documented states |
| Cursor paging, never offset, no `total`; `state=finished` keyed by finishing order | The device inserts and prunes while a client reads; a late-ending pass must arrive after a sync cursor |
| A pass is `Active`, `Finished` **or `Abandoned`** | Off the line with no end total is neither; `state` must agree with `?state=` |
| Reject an unknown filter value or cursor (`400`), never ignore it | `?state=finishd` returning everything is the opposite of the request |
| Every non-2xx body is `ApiError {error, detail}` | The viewer has one error parser; middlewares included |
| Broadcast by enqueueing, never awaiting a socket | One stalled display would block every other client and the watcher |

The viewer's shooter fallback chain — name → licence → `contestShooterName` → `Linie N · HH:mm` —
is in `core/format.js`; keep it. Viewer rules that fail silently if broken (see
`docs/architecture.md`):

- **Asset URLs must be absolute** (`/app.js`): a relative one resolves under `/fullscreen/`, unserved.
- **Column widths belong on `<colgroup>`**: `table-layout: fixed` reads the first row, often a
  colspan message row.
- **Never rebuild the ticker DOM unless `tickerContentKey` changed**: a rebuild restarts the
  marquee, and results reload on every live message.
- **`/browse` filters are API queries**; only row shape, sort and result range live in the page.
  Displays never scroll or guess capacity: the table clips, the ticker always runs.

## Security model

`NetworkGate` (CIDR allowlist, **before auth**) → `TokenAuth` (bearer, constant-time) →
`SessionToken` (per start, in memory, for the viewer):

- **Tokens are arrays with scopes** (`ApiReadTokens` / `ApiWriteTokens`, write implies read).
  None configured is valid; the session token covers the viewer. `Authorization: Bearer` is the
  only accepted header. Tokens are compared as SHA-256 digests.
- **`TrustedProxies` is a list, not a switch**: `X-Forwarded-For` is unwound only through hops in
  it, stopping at the first stranger (`Security/ClientAddress.cs`).
- **`TokenAuth` reads endpoint metadata:** `/health` carries `AllowAnonymous`, `/live` carries
  `QueryTokenOnUpgrade`. `UseWebSockets()` must stay before `UseTokenAuth()`: `?token=` is accepted
  only on a genuine upgrade to `/live`; a plain GET with `?token=` is always 401 (`LiveFeedTests`
  guards both).
- **A forwarded hop that does not parse resolves to *no* client**, and the gate refuses it. Falling
  back to the proxy would admit anyone behind a public proxy (`ClientAddressTests`).
- **`/openapi/v2.json` is token-free** (schema, not data). Never put a real licence number or
  shooter name in an endpoint description; a test asserts neither appears.
- **A null remote address counts as loopback** (in-process; TCP cannot forge it). Non-private
  ranges are allowed, warned about at startup and in `health.publicExposure`.
- **The allowlists have no code default**; an empty list is refused at startup.
  `NetworkOptions.PrivateSpace` defines "private" for the warning, never an allowlist.

## After any change

1. `scripts/test.sh` stays fully green.
2. Touched a query or mapping rule? Check with `scripts/sql.sh`. Integration tests assert
   **invariants, not counts**: never hard-code a total, name or id from one export, nor cite one as a
   schema fact.
3. Touched `wwwroot/`? `scripts/run.sh`, load `/`, each `/fullscreen/*`, `/browse`, `/docs`.
4. New translation key? Add it to **both** `de` and `fr`.
5. Learned something about the schema? Record it in `docs/device-database.md`.

## Style

Simplicity first, also over small optimisations. Comments are short to non-existent: names explain
*what*, a comment is one line for a *why* that cannot be inferred. Functions stay
about 25 lines; split rather than comment sections. YAGNI: no speculative abstractions or helpers
for one caller. No emojis.
