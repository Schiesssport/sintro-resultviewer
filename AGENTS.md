# AGENTS.md

Blueprint for coding agents. Keep in sync; max 10 000 chars.

## Rules

Non-negotiable; they override everything else.

1. **Always read a file before editing it** — never edit from memory.
2. **No real personal data anywhere.** `.db/` is gitignored and stays so; device exports hold real
   shooters' names and licences. Fixtures, tests and docs use invented ones (`Hans Muster`).
3. **This API is read-only.** No endpoint or query may write to the device database.
4. **Never weaken the security defaults** (private-only CIDR allowlist, mandatory token,
   `TrustProxy: false`) to make something work. Fix the cause.

## What this is

A read-only HTTP API plus a plain-HTML viewer over the **Sintro 300 Hit Target Display**, an
electronic 300m target system whose MSSQL Express schema the API hides from event software.

**Read `docs/device-database.md` before touching a query**; `docs/architecture.md` orients you.
`docs/api.md` is the consumer guide; a change to `Api/V2/` or `Domain/` updates it in the same commit.

## Toolchain — all in Docker

No .NET, SQL Server or Node on the host. Docker is the dev setup only; a range runs the published exe.

| Command | Does |
|---|---|
| `db-up.sh` | Dev SQL Server (`localhost:11433`, `sa` / `Sintro_Dev_2026!`) |
| `db-restore.sh` | Restore the `.bak` exports from `.db/` |
| `db-demo-data.sh` | DEV ONLY: adds tens and mouches |
| `sql.sh "SELECT …"` | Ad-hoc query |
| `test.sh` | Full suite: .NET (unit + integration) and viewer |
| `test-web.sh` | Viewer only (`node --test`) |
| `run.sh` | Build and run on <http://localhost:8080> |
| `dotnet.sh <args>` | Any `dotnet` command in the SDK container |
| `publish-win.sh` | Self-contained `win-x64` exe → `bin/win-x64/` |

(all under `scripts/`)

`.container-home/` is the container's `HOME` and NuGet cache. **Solution file: `.slnx`** (.NET 10).
Dev "today" is pinned with `Sintro__ReferenceDate=2026-07-08`, the backup's last shooting day.

## Architecture

```
src/Sintro.ResultViewer.Api/
  Program.cs        DI, middleware order, endpoint mapping
  StartupChecks.cs  refuses to start on a weak token; warns on public exposure
  Domain/           the device-derived model
  Data/             SintroRepository (ALL SQL), ScoreCalculator, SintroTime, SintroClock,
                    LicenseNumber, TargetKind, Cursor, ProgramFilter
  Security/         NetworkGate (CIDR) → TokenAuth (bearer) → SessionToken
  Live/             LaneWatcher (polls) → LiveHub (WebSocket fan-out)
  Api/V2/           VERSION-SPECIFIC: routes, tags, wire envelopes
  Viewer/           injects the session token into the HTML at serve time
  wwwroot/          core/ = PURE logic (i18n, format, sectors, lanes, viewmode, ticker);
                    app.js, docs.js = DOM; tests/
tests/Sintro.ResultViewer.Tests/
```

**Layering:** anything testable without a browser belongs in `wwwroot/core/` — `app.js` may touch
the DOM, `core/` may not. Server-side the pure `Data/` classes carry the test weight.

**API versioning.** `Api/V2/` owns routes, tags, descriptions and wire envelopes; everything else is
shared, so v3 is a new folder plus one `app.MapV3()` line. **v1 is the legacy Grapevine service.**

## Single sources of truth

- **SQL** → `Data/SintroRepository.cs`. A query anywhere else is a bug.
- **Scoring** → `Data/ScoreCalculator.cs`. **Hit sectors** → `wwwroot/core/sectors.js`.
- **Translations** → `wwwroot/core/i18n.js` (`de` default, `fr`). `data-i18n[-title|-aria-label]` in
  HTML, `t('key', {params})` in JS. Tests assert identical keys in both languages and that every key
  is used and every used key exists.
- **Colours** → `wwwroot/tokens.css`, from the
  [design system](https://github.com/Schiesssport/design-system) plus a marked local block at its
  end. Never hard-code a hex in `styles.css`; derive tints with `color-mix()` on a token.
- **JSON options** → `SintroJson.Options`, shared by the endpoints, the live hub and the tests.
- **Config** → `SintroOptions.cs` + `appsettings.jsonc` (operator-facing, every setting explained
  inline; registered by hand in `Program.cs`, ordered so environment variables still win).
- **Target letters** → `Data/TargetKind.cs` (`0=A`, `1=B`, `3=S`).
- **Docs ordering** → numbered OpenAPI tags in `Api/V2/V2Endpoints.cs`; the docs page sorts numerically.

## Vocabulary

Code is **English**; UI strings are **German** (default) and French.

A row of `dbo.Programs` is one shooter's pass at the target: a **`program`** in code and API (1:1
with the table), a **"Passe"** in the UI; the C# type is `ShootingProgram` only because `Program` is
the entry point. **Never call it a `match`**: in OpenRangeOffice that is a *Stich* in the
registration sense, and the two systems exchange data.

## Mapping rules you must not undo

The integration tests assert the resulting counts.

| Rule | Why |
|---|---|
| Marker rows are `ShotNr 9999` only, never `TotalType 7` | The last real shot carries `TotalType 7` too; filtering on it drops every final shot |
| Sighting = `ShotType = 0`, **never** `ShotGroup = 0`; `sighting` is a list per group | Counting shots occur in group 0; merged groups would add 5er to 10er |
| Valuation per `(ProgramID, ShotGroup)`, highest `TargeinformationID` wins | Duplicate rows exist and can disagree |
| `Total` null on mixed valuations, `TotalUnavailable` says why | 5er + 10er is meaningless |
| Never join on `Shots.StartNr` — use `ProgramID → Programs.ShooterID` | Almost always zero |
| `Shooters.StartNr` **is the licence number**, no length cap | Six digits, zero-padded; 7-9 digits planned |
| `shooter: null` is normal | Most passes are anonymous |
| Parse `StartTime` as `dd.MM.yyyy-HH:mm:ss`, emit ISO 8601 | Day-first text; its order is meaningless |
| `(number, name)` is free text, not a key | Operators rename programs |
| `TargetType` is the Scheibe: `0`=A, `1`=B, `3`=S (Sau) | Matches the A/B prefixes in program names |
| `hitSector` 1 is twelve o'clock, **clockwise** in 45° steps | Derived from the mean `atan2(y,x)` per sector |
| Nulls are serialized, never omitted | `shooter`/`currentProgram` null are documented states |
| Cursor paging, never offset, no `total`; `state=finished` keyed by finishing order | The device inserts and prunes while a client reads; a late-ending pass must arrive after a sync cursor |
| A pass is `Active`, `Finished` **or `Abandoned`** | Off the line with no end total is neither; `state` must agree with `?state=` |
| Reject an unknown filter value or cursor (`400`), never ignore it | `?state=finishd` returning everything is the opposite of the request |
| Every non-2xx body is `ApiError {error, detail}` | The viewer has one error parser; middlewares included |
| Broadcast by enqueueing, never awaiting a socket | One stalled display would block every other client and the watcher |

The viewer's shooter fallback chain — name → licence → `contestShooterName` → `Linie N · HH:mm` —
is in `core/format.js`; keep it. **The viewer** is documented in `docs/architecture.md`. Three rules
that fail silently if broken:

- **Asset URLs must be absolute** (`/app.js`) — a relative one resolves under `/fullscreen/` and is not served.
- **Column widths belong on `<colgroup>`** — `table-layout: fixed` reads the first row, often a
  colspan message row.
- **Never rebuild the ticker DOM unless `tickerContentKey` changed** — a rebuild restarts the
  marquee, and results reload on every live message.

## Security model

`NetworkGate` (CIDR allowlist, **before auth**) → `TokenAuth` (bearer, constant-time) →
`SessionToken` (per process start, memory only, for the viewer). Easy to break by accident:

- **Tokens are arrays with scopes** (`ApiReadTokens` / `ApiWriteTokens`, write implies read).
  Configuring none is valid: the session token still covers the viewer. `Authorization: Bearer` is
  the only accepted header — do not add a second. Tokens are compared as SHA-256 digests.
- **`TrustedProxies` is a list, not a switch** — `X-Forwarded-For` is unwound only through hops in
  it, stopping at the first stranger (`Security/ClientAddress.cs`).
- **`UseWebSockets()` must stay before `UseTokenAuth()`** — `?token=` is accepted only on a genuine
  upgrade (`IsWebSocketRequest`), which that middleware makes meaningful; a plain GET with `?token=`
  is always 401. `LiveFeedTests` guards both.
- **A forwarded hop that does not parse resolves to *no* client**, and the gate refuses it. Falling
  back to the proxy's own address would admit anyone behind a public proxy (`ClientAddressTests`).
- **`/openapi/v2.json` is token-free** (schema, not data), on the *web* surface. Never put a real
  licence number or shooter name in an endpoint description — a test asserts neither appears.
- **A null remote address counts as loopback** (in-process or Unix socket; no TCP client can forge
  it). Non-private ranges are allowed but warned about at startup and via `health.publicExposure`.
- **The allowlists have no code default**; an empty list is refused at startup.
  `NetworkOptions.PrivateSpace` defines what "private" *means* for the warning, never an allowlist.

## After any change

1. `scripts/test.sh` — stays fully green.
2. Touched a query or mapping rule? Verify with `scripts/sql.sh`. Integration tests assert
   **invariants, not counts** — never hard-code a total, name or id from one export.
3. Touched `wwwroot/`? `scripts/run.sh`; load `/`, each `/fullscreen/*`, `/docs`.
4. New translation key? Add it to **both** `de` and `fr`.
5. Learned something about the schema? Record it in `docs/device-database.md`.

## Style

Simplicity first. Comments are short to non-existent: names and structure explain *what*, and a
comment is one line for a *why* that cannot be inferred (a measured device fact, a security
reason). Functions and methods stay readable, about 25 lines; split rather than comment sections.
YAGNI: no speculative abstractions, options or helpers for one caller. Never cite one export's
counts as schema facts. No emojis.
