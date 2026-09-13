# How the result viewer is put together

Orientation for someone about to change something. Pairs with
[`device-database.md`](device-database.md), which explains the device schema this all sits on, and with
[`AGENTS.md`](../AGENTS.md) in the repository root, which is the short operational contract.

## The shape of it

```
Sintro device ──► MSSQL Express ──► [ read-only API ] ──► viewer in a browser
  (writes)         (its schema)      (this repo)          (this repo)
                                          └─────────────► event software
```

The device owns its database and writes to it continuously. We only ever read. Everything that
makes the device schema awkward — text dates, marker rows that are not shots, ring values whose
meaning lives in another table — is absorbed by the API, so no client ever has to know about it.

Two audiences, both first-class:

- **event software**, which pulls results over a documented, versioned JSON API;
- **people at the range**, who look at a browser: an office dashboard and several wall displays.

## Layers

```
src/Sintro.ResultViewer.Api/
  Program.cs        composition root: DI, middleware order, routes
  StartupChecks.cs  refuses to start on a weak token; warns about public exposure
  SintroOptions.cs  every configurable value
  Domain/           the model derived from the device data
  Data/             the only place that talks to SQL, plus the pure logic around it
  Security/         NetworkGate (CIDR) → TokenAuth (bearer) → SessionToken
  Live/             LaneWatcher polls the device; LiveHub fans out over WebSocket
  Api/V2/           everything version-specific: routes, tags, wire envelopes
  Viewer/           serves the viewer HTML with a per-process token substituted in
  wwwroot/          the viewer itself
tests/              the .NET suite
```

**`Data/` is the boundary.** `SintroRepository` holds every SQL statement in the project; a query
written anywhere else is a bug, because the schema's traps are documented and handled in exactly one
place. Around it sit small pure helpers that carry most of the test weight: `ScoreCalculator`
(series, valuations, totals), `SintroTime` (the device's text dates), `LicenseNumber`, `TargetKind`,
`Cursor`.

**`Api/V2/` is the only version-aware folder.** Routes, OpenAPI descriptions and the wire envelopes
live there; `Domain/`, `Data/`, `Security/`, `Live/` and `Viewer/` are shared. Adding a v3 means
adding `Api/V3/` and one `app.MapV3()` line — nothing else moves. The implementation starts at
**v2** because v1 is a legacy Grapevine service that predates this repository.

## A request, end to end

1. **`NetworkGate`** checks the source address against the per-surface CIDR allowlist from
   configuration — there is no code default, because who may reach a range's results is the
   operator's decision and a hidden default is one they cannot review. It runs *before*
   authentication, so a blocked network never gets to guess tokens.
2. **`TokenAuth`** checks the bearer token in constant time — `Authorization: Bearer` and nothing
   else, so there is one documented way in. `/health` is exempt so monitoring works; `/live` also
   accepts the token as a query parameter, because a browser cannot set headers on a WebSocket
   handshake. Tokens are arrays with scopes (`ApiReadTokens` / `ApiWriteTokens`, write implies
   read); no endpoint writes yet.
3. **The endpoint** in `Api/V2/` parses query parameters and calls the repository.
4. **`SintroRepository`** runs the SQL, then hands raw rows to `ScoreCalculator`.
5. The result is serialised. **Nulls are written, never omitted** — `shooter: null` and
   `currentProgram: null` are documented states, and dropping the keys would make clients guess.

## API conventions

The consumer-facing guide is [`api.md`](api.md); this section is the reasoning behind it.

- **Cursor paging, never offset.** The device inserts rows while a client reads and prunes old ones
  from the other end, so an offset would skip or repeat records. Read `nextCursor`, pass it back as
  `cursor`. There is deliberately no `total`: it costs a second scan on every request and grows with
  the table.
- **`state=finished&order=asc` plus a stored cursor is an incremental sync.** Finished passes are
  keyed by finishing order (the end marker's `ShotID`), so a pass that ends late still arrives after
  the cursor; a request with a cursor drops the today-only default; `nextCursor` is always present so
  the last page leaves a position to resume from.
- **Today only by default.** Shooting across midnight is unrealistic, and one consistent rule beats
  special-casing the viewer. `from`/`to` widen the window.
- **A bad filter value is a 400, never an ignored parameter.** Dropping `?state=finishd` silently
  returned everything — the opposite of the request, and invisible to an importer. The same goes
  for a cursor this API did not issue, or one issued for the other sort order: `400 invalid_cursor`
  rather than a silent restart from page one.
- **One error shape.** Every non-2xx answer, from the middlewares as well as the endpoints, is
  `{"error": "<stable code>", "detail": "<text>"}` (`ApiError` in `Api/V2/`). The viewer has exactly
  one error parser.
- **`sighting` is a list**, one series per `ShotGroup` the sighting shots were fired in. Merging
  them would add a 5er group to a 10er one — the sum `total` refuses to make.
- **ISO 8601 everywhere**, with the range's UTC offset attached.
- The OpenAPI document at `/openapi/v2.json` needs no token — it is schema, not data — and `/docs`
  renders it as a browsable, try-it-here page.

## The viewer

No framework and no build step: the browser loads the files as they are in `wwwroot/`. The same
split as [OpenRangeOffice](https://github.com/Schiesssport/OpenRangeOffice):

| | |
|---|---|
| `wwwroot/core/` | **Pure logic.** No DOM, no `fetch`, no globals. Unit-tested under `node --test` |
| `wwwroot/app.js`, `docs.js` | The app layer. Owns the DOM, and only it may |
| `wwwroot/api.js` | `fetch` and WebSocket client |
| `wwwroot/tokens.css` | Vendored from the shared [design system](https://github.com/Schiesssport/design-system), plus a clearly marked block of project-local tokens at the end. Never hard-code a colour in `styles.css`; derived tints use `color-mix()` on a token |

Anything that can be tested without a browser belongs in `core/`. That is where the interesting
parts live: `format.js` (labels, shot rendering, the shooter fallback chain), `sectors.js` (the hit
dial), `lanes.js` (when a line frees up, and the 30-second hold that keeps a finished pass on its line
after the device has already cleared the lane), `ticker.js` (what scrolls, and how fast), `viewmode.js`
(which view a URL means), `openapi.js` (reading the spec for `/docs`, and which URLs the try box may
call with the token), `i18n.js` (German and French — a test asserts every key is used and every
used key exists, so `data-i18n`, `data-i18n-title` and `data-i18n-aria-label` in the HTML count).

### Views

`/` is the office dashboard: lines on top, a scrollable result table below, controls visible.

`/fullscreen/{live,results,leaderboard,live+results}` are wall displays — no controls, sized to be
read across a room. They are real routes so each can be bookmarked and pointed at from a TV; the
server returns the same page for all of them and the client reads `location.pathname`. An unknown
variant falls back to `live+results`, because nobody can fix a typo on a wall-mounted screen.

Two consequences worth knowing before editing the HTML:

- **Asset URLs must be absolute** (`/app.js`, not `app.js`). Under `/fullscreen/live` a relative
  path resolves into the SPA catch-all, which returns HTML, and the module then fails to parse.
- **Table column widths belong on `<colgroup>`**, never on cells. With `table-layout: fixed` the
  browser reads widths from the first row, which is routinely a colspan message row or a free line.

### Live updates

`LaneWatcher` polls the device (lane assignments plus the highest shot id) roughly once a second and
broadcasts only when something changed. Polling rather than change-tracking, because a range PC's
SQL Express has neither Service Broker nor CDC configured, and a handful of lines at 1 Hz is
nothing.

`LiveHub` gives **every client its own bounded queue and pump**, so broadcasting only enqueues. A
display on a half-dead connection therefore cannot hold up the others — or the watcher — while the
OS works its way to a timeout; it loses frames (the queue drops the oldest, since these payloads are
snapshots and a backlog would only show the past more slowly) and is dropped if it stops draining
entirely.

Whether a line reads as *free* is **derived, never latched**: five minutes after the device's end
total, or six minutes after the last shot if it wrote none. That is what makes an interruption work
— a resumed shot moves the last-activity time and the line simply fills again with everything
already shot. There is no expiry to undo.

## Testing

Run everything with `scripts/test.sh`.

**The .NET suite** is unit tests over the pure helpers plus integration tests against a restored
device export. The integration tests assert **invariants, not counts**: exports differ per
installation, so `Assert.Equal(1021, …)` would only ever be true for one club and would tell the
next contributor their code is broken when it is not. Reference values — a licence, a club name, a
line number — are read from the API at run time. Follow that pattern.

**The viewer suite** runs `node --test` over `wwwroot/core/` and needs nothing else.

**Never put real names or licence numbers in fixtures, tests or documentation.** Use `Hans Muster`
and invented numbers. A test asserts that the published OpenAPI document contains no shooter name
or licence from the loaded export.

## Configuration format

The operator-facing file is **`appsettings.jsonc`**, and it explains every setting inline —
the person editing it is standing at a range with no documentation to hand. .NET's JSON reader
skips comments and tolerates trailing commas by design, so it parses this happily; the `.jsonc`
extension is what tells the *operator's editor* the same, which a `.json` file cannot.

It is registered by hand in `Program.cs` (the framework only auto-loads `appsettings.json`), and
deliberately inserted among the framework's own JSON sources rather than appended — appending
would place it after the environment variables and let the file silently beat an explicit
override. `AppSettingsTests` proves the shipped file parses, binds, and is actually loaded by the
running application.

## Deployment

The range PC gets one self-contained `win-x64` executable from `scripts/publish-win.sh`, with no
.NET runtime to install. It reads the device database through a **read-only SQL login**
(`db_datareader`); the API has no code path that writes, and the credentials should not permit one
either.

Shooter names are personal data. On the range LAN that is fine. Exposing the service to the internet
is a deliberate act, which is why non-private CIDR ranges are accepted but logged as a warning at
startup and shown as a red banner in the viewer.
