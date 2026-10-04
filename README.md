# Sintro Resultviewer

Live results and a documented API for the **Sintro 300** electronic target system.

The Sintro software records every shot but keeps the results to itself. This adds two things:
a display anyone at the range can look at, and an interface event software can read from — so
results reach a scoreboard and a ranking without being typed twice.

Free and open source, for Swiss shooting clubs.

---

## Für Anwender:innen

**Sintro Resultviewer** zeigt die Resultate der Trefferanzeige im Browser: oben alle Linien mit
des Stichs, der gerade geschossen wird, darunter die zuletzt beendeten – mit Schütze, Resultat und
allen Einzelschüssen. Bei den laufenden Stichen zeigt ein Ring um jeden Schuss, in welchem der acht
Sektoren er sitzt.

Für Fernseher und Beamer gibt es eigene Adressen ohne Bedienelemente, als Lesezeichen speicherbar:

| Adresse | Zeigt |
|---|---|
| `/fullscreen/live` | nur die Linien |
| `/fullscreen/results` | nur die letzten Resultate |
| `/fullscreen/live+results` | beides |

Weil auf einem Fernseher nicht gescrollt werden kann, laufen die letzten Resultate zusätzlich als
Laufschrift durch. Im Vollbild-Dialog lassen sich beliebig viele Anzeigen einrichten – Ansicht,
Anzahl Resultate, wie viele die Laufschrift überspringt, Lesezeit pro Eintrag, Laufschrift ein oder
aus – und benennen; die Liste bleibt im Browser gespeichert, und jede Adresse enthält ihre
Einstellungen.

Unter **Resultauswertung** (`/browse`) lassen sich die Resultate eines Zeitraums nach Programmnummer,
Wettkampfnummer und Lizenz filtern, pro Stich, Passe oder Schuss anzeigen, sortieren und als CSV
oder in die Zwischenablage exportieren.

Eine Linie gilt wieder als frei, wenn der Stich abgeschlossen ist oder eine Weile nicht mehr
geschossen wurde. Wird nach einer Pause weitergeschossen, erscheint der Stich mit allen bisherigen
Schüssen wieder.

Die Ansicht liest ausschliesslich – sie kann in der Anlage nichts verändern. Sprachen: **Deutsch**
und **Französisch**.

## Installation on the range PC

Download the latest `sintro-resultviewer-*-win-x64.zip` from [Releases](../../releases) and unzip
it next to the Sintro installation. It is self-contained — there is no .NET runtime to install.

1. Open `appsettings.jsonc` and set:
   - **`ConnectionStrings:Sintro`** — the device database. Use a **read-only** SQL login
     (`db_datareader`). The service never writes, and the credentials should not permit it either.
   - **`Sintro:ApiReadTokens`** — only if something other than the bundled displays should read
     the API (event software, a scoreboard). Leave it empty otherwise; the viewer authenticates
     itself. 16 characters minimum, 128 recommended, one per consumer so each can be revoked
     alone.
2. Run `Sintro.ResultViewer.Api.exe` and open the address it prints.
3. To keep it running, register it as a Windows service (`sc.exe create`) or as a scheduled task
   at startup.

`appsettings.jsonc` ships allowing the private network ranges — a range LAN — and nothing else.
The list is right there to edit; it is not a hidden default. Shooter names are personal data:
on the range LAN that is fine, exposing the service to the internet is a deliberate act — which is
why non-private ranges are accepted but warned about at startup and flagged red in the viewer.

### Configuration

`appsettings.jsonc`, or `Sintro__*` environment variables (which override the file).

Every setting is explained inline in that file — it is written for someone standing at the range
with no documentation to hand, so it is worth reading before this table.

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Sintro` | – | The device database; use a read-only login |
| `Sintro:ApiReadTokens` | none | Tokens that may read. Only needed for consumers other than the bundled viewer |
| `Sintro:ApiWriteTokens` | – | *Planned.* Tokens that may write; a write token also reads. No endpoint writes yet |
| `Sintro:Network:Api` / `:Web` | listed in the file | CIDR allowlists per surface, spelled out rather than defaulted in code. Empty is refused at startup. `0.0.0.0/0` is accepted but warned about |
| `Sintro:TrustedProxies` | none | Reverse proxies whose `X-Forwarded-For` may be believed. Empty means the header is ignored |
| `Sintro:TimeZone` | the host's | Normally omitted — the machine's own timezone is the range's |
| `Sintro:ReferenceDate` | unset | Development only: pins "today" to an old export |

---

## For developers

> Coding agents: read [`AGENTS.md`](AGENTS.md) first — it is the operational contract.
> [`docs/architecture.md`](docs/architecture.md) orients you in the code, and
> [`docs/device-database.md`](docs/device-database.md) explains the device schema, which you should
> read before touching a query.

ASP.NET Core minimal API and Dapper over the device's SQL Server, plus a viewer with no framework
and no build step. Shipped as a single self-contained `win-x64` executable.

**Docker is the development setup only** — it saves installing .NET and SQL Server to work on this.
A real installation is the executable above, against the range's own SQL Server.

```bash
cp <your two .bak exports> .db/     # never committed: real names and licence numbers
scripts/db-restore.sh               # dev SQL Server + restore
scripts/db-seed.sh                  # or: an invented database from seed/, no export needed
scripts/test.sh                     # .NET suite + viewer suite (see seed/README.md to run on the seed)
scripts/run.sh                      # http://localhost:8080
scripts/publish-win.sh              # -> bin/win-x64/
```

The integration tests always run; they need a database, either a restored export or the seeded
one from [`seed/`](seed/README.md). CI builds the seed and runs the full suite against it on
every push, since an export can never be committed.

### API

Read-only, under `/api/v2`, `Authorization: Bearer <token>` on every request.
*(v1 is a legacy Grapevine service that predates this repository.)* Building a consumer? Start
with [`docs/api.md`](docs/api.md): access setup, the results list field by field, paging and sync.

| Endpoint | Returns |
|---|---|
| `GET /live` | Every line and the pass currently on it. The same URL upgrades to a **WebSocket** pushing changes |
| `GET /programs` | Passes, newest first. `state`, `targetCode`, `targetProgram`, `matchCode`, `license`, `lane`, `from`, `to`, `withoutResult`, `order`, `cursor`, `limit` |
| `GET /programs/{id}` | One pass with all series and shots |
| `GET /shooters`, `/shooters/{license}` | Registered shooters, optionally only those with a pass in `from`/`to`; licence lookup with their passes (also cursor-paged) |
| `GET /clubs` | The Swiss club register held by the device |
| `GET /program-catalog` | Distinct `(targetCode, targetProgram)` pairs present, with counts |
| `GET /health` | Database reachability (no token required) |

`/docs` renders the OpenAPI document as a browsable, try-it-here page; `/openapi/v2.json` is the
document itself and needs no token.

**Collections are cursor-paged, never offset-paged** — the device inserts while you read and prunes
from the other end, so an offset would skip or repeat rows. Pass `nextCursor` back as `cursor` while
`hasMore` is true. A cursor the API did not issue is a `400 invalid_cursor`, and every error carries
the same `{"error", "detail"}` body.

**To sync results**, request `state=finished&order=asc` with `from`/`to` set to the event's
shooting days and keep the last `nextCursor`. Passing it again later returns exactly the passes that
finished since — nothing to diff. See [`docs/api.md`](docs/api.md).

A few things worth knowing before building against it:

- **Lists default to today.** Pass `from`/`to` for anything else.
- **`shooter` is `null` for most passes** — identifying yourself is optional — so `lane` and
  `startedAt` are what always identify one.
- **A pass is `active`, `finished` or `abandoned`.** The third is real: started, never ended, and
  no longer on a line.
- **`totals` lists one sum per target and ring scale.** A pass shot on one scale has one entry; a
  pass that changed scale has two, never one number across scales. Each series carries a
  `targetType` (`A10`, `B4`, `S10`) combining target and scale.
- **Timestamps are ISO 8601** with the range's offset.
- **An unknown `state` or `order` value is a 400**, not a silently ignored filter.

## Contributing

Issues and pull requests welcome, especially from clubs running other installations — the device
schema is undocumented and several columns are still informed guesses.
[`docs/device-database.md`](docs/device-database.md) ends with the open questions; confirming any
of them is a genuinely useful contribution.

Two rules that matter more than style:

- **Never commit a device export, and never put real names or licence numbers in tests, fixtures
  or documentation.** Use invented ones (`Hans Muster`). A test enforces this for the published
  OpenAPI document.
- **Integration tests assert invariants, not counts.** Exports differ per installation, so a
  hard-coded total would only ever be true for one club.

## License

Free and open source, for Swiss shooting clubs. See [`LICENSE`](LICENSE).
