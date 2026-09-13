# AGENTS.md

Operational blueprint for coding agents. Keep in sync as the code evolves (≤10 000 chars).

## Rules

Non-negotiable; they override anything else here.

1. **Always read a file before editing it** — never edit from memory.
2. **No real personal data anywhere.** `.db/` is gitignored and stays so; device exports hold real
   shooters' names and licences. Fixtures, tests and docs use invented ones (`Hans Muster`).
3. **This API is read-only.** No endpoint or query may write to the device database.
4. **Never weaken the security defaults** (private-only CIDR allowlist, mandatory token,
   `TrustProxy: false`) to make something work. Fix the cause.

## What this is

A read-only HTTP API plus a plain-HTML viewer over the **Sintro 300 Hit Target Display**, an
electronic 300m target system. Its MSSQL Express schema leaks internals; the API hides that so
event software can extract results reliably.

**Read `docs/device-database.md` before touching a query**; `docs/architecture.md` orients you in the code.

## Toolchain — everything runs in Docker

No .NET, SQL Server or Node on the host. **Docker is the development setup only** — a range runs
the published executable against its own SQL Server.

| Command | Does |
|---|---|
| `db-up.sh` | Start dev SQL Server (`localhost:11433`, `sa` / `Sintro_Dev_2026!`) |
| `db-restore.sh` | Restore the `.bak` exports from `.db/` |
| `db-demo-data.sh` | DEV ONLY: adds tens and mouches the export lacks |
| `sql.sh "SELECT …"` | Ad-hoc query — fastest way to check a rule |
| `test.sh` | Full suite: .NET (unit + integration) and viewer |
| `test-web.sh` | Only `wwwroot/core` (`node --test`) |
| `run.sh` | Build and run; viewer on <http://localhost:8080> |
| `dotnet.sh <args>` | Any `dotnet` command in the SDK container |
| `publish-win.sh` | Self-contained `win-x64` exe → `bin/win-x64/` (what a range installs) |

(all under `scripts/`)

`.container-home/` is the container's writable `HOME` and NuGet cache; it runs as the host UID so
generated files stay editable. **The solution file is `.slnx`** (.NET 10). Dev "today" is pinned with
`Sintro__ReferenceDate=2026-07-08`, the backup's last shooting day — without it the today-only
default returns nothing.

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

**Layering, as in OpenRangeOffice:** anything testable without a browser belongs in `wwwroot/core/`
— `app.js` may touch the DOM, `core/` may not. Same server-side: `ScoreCalculator`, `LicenseNumber`,
`SintroTime`, `TargetKind`, `Cursor` are pure and carry the test weight.

**API versioning.** `Api/V2/` owns everything version-specific: routes, tags, descriptions, wire
envelopes. Everything else is shared, so adding v3 is a new folder plus one `app.MapV3()` line —
nothing moves. **v1 is the legacy Grapevine service**, hence starting at v2.

## Single sources of truth

- **SQL** → `Data/SintroRepository.cs`. A query anywhere else is a bug.
- **Scoring** → `Data/ScoreCalculator.cs`. **Hit sectors** → `wwwroot/core/sectors.js`.
- **Translations** → `wwwroot/core/i18n.js` (`de` default, `fr`). `data-i18n` in HTML,
  `t('key', {params})` in JS. A test asserts both carry identical keys and placeholders.
- **Colours** → `wwwroot/tokens.css`, from the
  [design system](https://github.com/Schiesssport/design-system). Never hard-code a hex.
- **Config** → `SintroOptions.cs` + `appsettings.jsonc` (operator-facing, every setting explained
  inline; registered by hand in `Program.cs`, ordered so environment variables still win).
- **Target letters** → `Data/TargetKind.cs` (`0=A`, `1=B`, `3=S`).
- **Docs ordering** → numbered OpenAPI tags in `Api/V2/V2Endpoints.cs`; the docs page sorts on the
  number, so the order is declared rather than alphabetical.

## Vocabulary

Code and identifiers are **English**; UI strings are **German** (default) and French.

A row of `dbo.Programs` is one shooter's pass at the target: a **`program`** in code and in the API
(1:1 with the table — mapping drift makes debugging against SQL painful), a **"Passe"** in the UI.
The C# type is `ShootingProgram` only because `Program` is the entry-point class.

**Do not call it a `match`.** In OpenRangeOffice `match` is a *Stich / passe* in the **registration**
sense (what a participant buys into); a Sintro program is an **execution**. The two systems will
exchange data, so the words must not blur.

## Mapping rules you must not undo

The integration tests assert the resulting counts.

| Rule | Why |
|---|---|
| Drop rows with `ShotNr = 9999` | Synthetic end markers; a pass can hold nothing else |
| Sighting = `ShotType = 0`, **never** `ShotGroup = 0` | Counting shots occur in group 0, sighting shots above |
| Valuation per `(ProgramID, ShotGroup)`, highest `TargeinformationID` wins | Duplicate rows exist and can disagree |
| `Total` null on mixed valuations, `TotalUnavailable` says why | 5er + 10er is meaningless (program 922) |
| Never join on `Shots.StartNr` — use `ProgramID → Programs.ShooterID` | Almost always zero; rarely matches |
| `Shooters.StartNr` **is the licence number**, no length cap | Six digits, zero-padded, non-sequential; 7-9 digits planned |
| `RFID` is deprecated, never a key | Installations share one all-zero placeholder |
| `shooter: null` is normal | Most passes are anonymous; never lose a result over it |
| Parse `StartTime` as `dd.MM.yyyy-HH:mm:ss`, emit ISO 8601 | Day-first; text order is meaningless |
| `(number, name)` is free text, not a key | Operators rename programs; one number, several names |
| `TargetType` is the Scheibe: `0`=A, `1`=B, `3`=S (Sau) | Correlates cleanly with the A/B prefixes in program names |
| `hitSector` 1 is twelve o'clock, **clockwise** in 45° steps | Mean `atan2(y,x)`: 90°, 43°, −2°, −45°, −89°, −136°, 180°, 137° |
| Nulls are serialized, never omitted | `shooter`/`currentProgram` null are documented states |
| Collections carry no `total` | A second scan per request, growing with the table |
| Cursor paging, never offset | The device inserts while a client reads, and prunes the far end |
| `ProgramID` is the programs keyset key | Identity column, advances in step with `StartTime` |
| A pass is `Active`, `Finished` **or `Abandoned`** | No end total and off the line is neither of the first two; calling it finished made `state` disagree with `?state=finished` |
| Reject an unknown filter value, never ignore it | `?state=finishd` returning everything is the opposite of the request |
| Broadcast by enqueueing, never awaiting a socket | One stalled display would block every other client and the watcher |

The viewer's shooter fallback chain — name → licence → `contestShooterName` → `Linie N · HH:mm` —
is in `core/format.js`. Keep it; it makes an unidentified pass still processable.

**The viewer** is documented in `docs/architecture.md`: view modes and their routes
(`core/viewmode.js`), when a line reads as free (`core/lanes.js`), and the result ticker
(`core/ticker.js`). Three rules that fail silently if broken:

- **Asset URLs must be absolute** (`/app.js`) — a relative one resolves into the SPA catch-all,
  which returns HTML, and the module fails to parse.
- **Column widths belong on `<colgroup>`** — with `table-layout: fixed` the browser reads widths
  from the first row, routinely a colspan message row.
- **Never rebuild the ticker DOM unless `tickerContentKey` changed** — results reload on every live
  message and a rebuild restarts the marquee animation.

## Security model

`NetworkGate` (CIDR allowlist from config, **before auth**) → `TokenAuth` (bearer, constant-time)
→ `SessionToken` (per process start, memory only, for the viewer). Rules easy to break by accident:

- **Tokens are arrays with scopes** (`ApiReadTokens` / `ApiWriteTokens`, write implies read).
  Configuring none is valid: a viewer-only range has nothing external to authenticate and the
  session token still covers the viewer. No endpoint writes yet. `Authorization: Bearer` is the
  only accepted header — do not add a second.
- **`TrustedProxies` is a list, not a switch** — `X-Forwarded-For` is unwound only through hops in
  it, stopping at the first stranger (`Security/ClientAddress.cs`).
- **`UseWebSockets()` must stay before `UseTokenAuth()`** — it installs the feature that makes
  `IsWebSocketRequest` meaningful; without it every handshake 401s. `LiveFeedTests` guards this.
- **`/openapi/v2.json` is token-free** (schema, not data) and counts as the *web* surface. Never put
  a real licence number or shooter name in an endpoint description — a test asserts neither appears.
- **A null remote address counts as loopback** — in-process or Unix-socket only; no TCP client can
  forge it. Non-private ranges are allowed but warned about at startup and banner-flagged via
  `health.publicExposure`.
- **The allowlists have no code default** — who may connect is the operator's call, so it lives in
  `appsettings.jsonc`; an empty list is refused at startup. `NetworkOptions.PrivateSpace` defines
  what "private" *means* for the warning, never an allowlist.

## After any change

1. `scripts/test.sh` — stays fully green.
2. Touched a query or mapping rule? Verify with `scripts/sql.sh`. Integration tests assert
   **invariants, not counts** — exports differ per installation, so never hard-code a total, a
   name or an id from one.
3. Touched `wwwroot/`? Run `scripts/run.sh`; load `/`, each `/fullscreen/*`, `/docs`.
4. Added a translation key? Add it to **both** `de` and `fr`.
5. Learned something about the schema? Record it in `docs/device-database.md`.

## Style

Write for the next developer. Names carry the intent; if a function needs a comment to say *what* it
does, rename or split it. Comment only the non-obvious *why* — usually a measured fact about the
device data, so cite the number. No emojis. No speculative abstractions.
