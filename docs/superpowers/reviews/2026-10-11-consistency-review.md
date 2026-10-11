# Consistency review, 2026-10-11

Scope: `docs/superpowers/review-prompt-2026-10-11.md`, nine checks over `main` at `cff61ab`.
Read-only; nothing edited. Paths are relative to the repository; `A/` is
`src/Sintro.ResultViewer.Api/`, `W/` is `A/wwwroot/`, `T/` is `tests/Sintro.ResultViewer.Tests/`.

**Headline.** The regression class the owner feared is gone: all three browse row builders produce
the identical 13-field row, and nothing in the viewer reads a field the API no longer sends. Two
bugs were found elsewhere, one of them a security regression introduced by the CORS commit. The
rest is documentation drift and test gaps.

## Findings, by severity

### Bugs

**B1 — The session token is readable cross-origin from any website.** Severity: bug (security).
`A/Program.cs:48` applies `UseCors(AllowAnyOrigin…)` to the whole pipeline, so `/`, `/browse`,
`/docs` and `/fullscreen/*` answer a cross-origin `fetch` with `Access-Control-Allow-Origin: *`.
Those pages embed the session token verbatim (`W/index.html:107`, `W/browse.html:204`,
`W/docs.html:49`, substituted by `A/Viewer/ViewerPage.cs`). Before commit `40b4f48` the same-origin
policy kept a foreign page from reading that HTML; now a script on any site a LAN browser visits can
fetch `/`, extract `window.SINTRO_TOKEN`, and call `/api/v2/shooters` with it. The gate passes
(the browser is on the allowed network), the token passes (it is genuine), the origin is `*`.
Chrome's Private Network Access blocks some of this; Firefox and Safari do not. The comment at
`Program.cs:46-47` ("the origin was never one") is true for `/api`, which the token protects, but
not for the token-bearing viewer pages. `docs/api.md:24-25` already promises the narrower
behaviour: "every `/api` response carries `Access-Control-Allow-Origin: *`".
Should: scope the policy to `/api` (`UseWhen` on the path, or `RequireCors` on the v2 route group
with no default policy). Guarding test to add: `GET /` with an `Origin` header must carry no
`Access-Control-Allow-Origin`; today `SecurityTests.aCrossOriginReadCarriesTheAllowOriginHeader`
checks only `/api/v2/health`, and nothing fails when the viewer page carries the header.

**B2 — Export throws on very large shot-grouped result sets.** Severity: bug (low probability).
`W/core/browse.js:190` `Math.max(1, ...rows.map(count))` spreads one argument per row;
`exportText` (`:204`) computes the width for all three shot layouts on every export, whether or not
a shots column is selected. V8 overflows the call stack somewhere above 130 000 arguments
(probe: 120k fine, 200k `RangeError`). Trigger: grouping = Schuss, a wide date range (`allPages` in
`W/api.js:78-90` has no cap), then copy or download. Should be a `reduce` with `Math.max` per row.
No test.

**B3 — A failed reload leaves the previous rows exportable and the count on "Lade…".**
Severity: bug (minor, DOM). `W/browse.js:114-127`: `load()` writes the loading text into
`#result-count`, and on error sets `programs = []` and the error row but never calls `render()`.
So the count still reads the loading text beside the error, and `currentRows` (`:21`, `:105`) still
holds the rows of the previous successful query, which the export dialog will happily export under
filters the page no longer shows. Trigger: any API error after a successful load (device offline,
server restarted), then export. Should clear `currentRows` and the count, or call `render()` after
resetting `programs`. No test.

### Inconsistencies

**I1 — Same ring scale on different targets is merged by the viewer, never by the server.**
`W/core/format.js:68-74` keys `resultTotals` on `scaleFamily(valuation)` only and collects the
target letters into the label; `activeTotal` (`:90-97`) likewise. The server
(`A/Data/Sintro300/ScoreCalculator.cs:104-111`) keys on the `targetType` string, so an `A10 87`
plus `B10 50` pass has two `totals` entries, while the viewer shows a single bare `137`: with one
group the label is dropped (`format.js:83`, `markup.js:9`), so even the `A10/B10` hint vanishes.
Browse sorts and ranges on 137, the ticker reads 137. `docs/architecture.md:132-134` promises "4er
and 5er series add up to one number, any other mix shows every scale labelled". Either this merge
is intended (same ring count is comparable) and needs documenting, or `scaleFamily` should return
`4/5` for valuations 4 and 5 and the full `targetType` otherwise. No test pins either behaviour:
`W/tests/format.test.js:167-175` covers `A5+B4` and different valuations only, and
`ScoreCalculatorTests` has no two-letter, one-valuation case. Low frequency on a real range.

**I2 — The seed's "abandoned" pass is `finished` to the API; a true abandoned pass is never
seeded.** `seed/definitions.mjs:74,116` and `seed/generate.mjs:137,158-161`: the `'abandoned'`
state suppresses the shots but still writes the `ShotNr 9999 / TotalType 7` marker. The repository
(`A/Data/Sintro300/SintroRepository.Programs.cs:20,244-246`) then sees a non-null `EndShotId` and
reports `finished` with no result. The API's `abandoned` (no marker, lane not pointing at it) has
no seeded instance, so `theThreeStateFiltersPartitionTheList` passes with an empty partition and
the `Abandoned` branch is covered by unit tests only. `seed/README.md:43` and
`docs/device-database.md:104` use the API's word for what the API calls finished. Rename the seed
state and add a pass with shots, no marker, and a lane that moved on.

**I3 — `docs/architecture.md:99`** says every list is `{items, nextCursor, hasMore}`, "the program
catalog included". The catalog is `Collection<T>` = `{items}` only (`A/Api/V2/V2Contracts.cs`,
`V2Descriptions.cs`, `docs/api.md:154`). Should read: every paged list; the catalog is `{items}`.

**I4 — "Passe" used for a program in consumer-facing text,** against the AGENTS.md vocabulary
(program = Stich, series = Passe): `A/Api/V2/V2Descriptions.cs:38` ("— a "Passe""),
`A/Api/V2/V2Endpoints.cs:45` (`Programs (Passen)`), `docs/api.md:32`. All three ship into
`/openapi/v2.json` or `/docs`. The viewer's own strings are right (`W/core/i18n.js`).

**I5 — The seed docs say the API "withholds its grand total"** for program 91
(`seed/README.md:37`, `seed/definitions.mjs:48`). Since the totals rework the API answers two
`totals` entries instead. Update both.

**I6 — `README.md:120,122,141`** omit the `dates` parameter on `/programs` and `/shooters`, and
"Lists default to today" is true for `/programs` only (`V2Endpoints.cs`: `/shooters`, `/clubs`,
`/program-catalog` have no default day).

**I7 — `docs/device-database.md:54`** calls `A10` / `B4` the "target code"; in the API that is
`targetType`, and `targetCode` is the program number (row 37 of the same table). Rename the row.

**I8 — `/docs` is not English apart from its intro.** The endpoint group headings are the OpenAPI
tags `A/Api/V2/V2Endpoints.cs:17-20` ("2 · Resultate", "3 · Stammdaten", "4 · Betrieb"), rendered
at `W/docs.js:423` on a page whose summaries and descriptions are English.

**I9 — Not every non-2xx body is `ApiError`.** `A/Program.cs` has no exception handler or
status-code pages and `V2Endpoints` has no fallback route, so with a valid token
`GET /api/v2/does-not-exist` is an empty 404, `POST /api/v2/programs` an empty 405, and an unhandled
exception on `/programs` (as opposed to `/health`, which maps it to 503) a bare 500 in production or
the developer page in Development. Tests run in Development and never see the production shape.
`docs/api.md:174` and AGENTS.md promise one body for every non-2xx. The viewer tolerates it
(`W/api.js:57-65` falls back to `HTTP <status>`). Either add a fallback under `/api` plus an
exception handler emitting `ApiError`, or narrow the claim. No test covers unknown-route 404, 405 or
500. The presented token is never echoed in any of these bodies.

**I10 — Seed marker rows carry the match code** (`seed/generate.mjs:106-109`), while
`docs/device-database.md` records the 9999 marker "seen with 0". Harmless: both the filter and the
score calculator drop markers. `seed/README.md:59` ("on every shot") hides the difference.

**I11 — Fixtures with shapes the API never sends.** `W/tests/format.test.js:271` passes
`sighting` as an object; the wire is a list, and `W/core/lanes.js:14` spreads it, so that fixture
would throw in `lastActivityAt`. The test passes only because `shotGroups` ignores `sighting`.
`W/tests/format.test.js:188` overrides `name`, not a wire field; the assertion holds only because
the base fixture already has the same `targetTitle`.

**I12 — `describeFailure` and `prettyJson` live in `W/api.js`** with no test, though both are pure.
Per the layering rule they belong in `core/`. The 401 reload decision in `api.js:128-150` is
likewise untested; `tokenRejected()` is already runnable under node with `fetch` stubbed.

**I13 — `AGENTS.md` is 10 144 characters** (`wc -m`), over the 10 000 it sets for itself.

### Notes

- **N1** The i18n test's dynamic-key list (`W/tests/i18n.test.js:35-38`) is hand-written. A fourth
  socket state in `api.js` or a fourth `FULLSCREEN_MODES` entry would ship a raw key and the test
  stays green. Its comment at line 34 names `display.reasonKey` and `fullscreen.mode.${target}`,
  neither of which exists; the real keys are `live.${state}` (`app.js:245`) and
  `fullscreen.mode.${board.mode}` (`boards-dialog.js:485-500`).
- **N2** `W/core/i18n.js:139` holds two entries on one line; a line-based grep misreads it.
- **N3** `W/browse.html:170` static text "Stichnummer" vs the dictionary's "Wettkampfnummer";
  `W/docs.html:337` `aria-label="Sprache"` on a `lang="en"` page; `docs.js` never sets
  `documentElement.lang`. All overwritten on load; a flash at most.
- **N4** `W/core/browse.js:93` `total: shot.value` lacks the `?? null` its siblings have. Unreachable
  from the wire (`Shot.Value` is `int`); make it symmetric.
- **N5** The browse range filter judges a mixed-scale pass by its first scale only
  (`core/browse.js:59-63`), documented in a comment but not in the UI label.
- **N6** Two series with the same `targetType` but valuations `10` and `null` yield two totals with
  the same label (`A10 0 · A10 9`), indistinguishable to the reader. Intended per the test "a series
  whose scale the device never recorded is its own total".
- **N7** `totals[].fineValues` is ordered `(ShotGroup, ShotID)`, not `ShotID` alone
  (`ScoreCalculator.cs:66-79,110`). Same assumption `series[]` already rests on.
- **N8** The reload-on-401 has no delay. If an intermediary ever cached a page with a stale token, it
  would loop. Not reachable in the one-process deployment.
- **N9** `theOpenApiDocumentExposesNoShooterData` (`T/SecurityTests.cs:208-224`) is only as strong
  as the first 50 shooters of the loaded database. A static regex for 6- to 9-digit numbers in
  `V2Descriptions.cs` (allowing the documented `123456`/`012345` examples) would be deterministic.
- **N10** `TokenAuth` enforces no scope: "write implies read" holds because nothing checks. Must
  change before the first write endpoint. The digest comparison has no direct test either.
- **N11** The presented token is trimmed (`TokenAuth.cs:44`), the configured one is not; a padded
  configured token can never match. Fails closed.
- **N12** Seed coverage gaps against `docs/device-database.md`: no duplicate `Targetinformation`
  rows (highest-id rule unexercised), no sighting shot in a group above 0, `Lanes.ProgramID` null
  for every finished pass (the "lane still points at a finished pass" guard unexercised), the active
  pass has fired every shot. The doc's "the seed reproduces every rule on this page" overclaims.
- **N13** Seed geometry and mouche threshold measured on A10 are applied to A5, B4, A100 and S;
  the doc states them for A10 only. Nothing to contradict, just unverified.
- **N14** `seed/generate.mjs:46-50` mixes local noon with a UTC date; wrong only at UTC+13/+14.
- **N15** Doc nits: `docs/api.md:74-75` example `value: 31` does not match its own `fineValues`
  under the seed's ring rule (33); `README.md:16` "mit des Stichs"; `docs/api.md:13` "allowed by
  default" vs README/AGENTS "no hidden default"; `V2Descriptions.cs` Shooters never mentions the
  `club` parameter; the AGENTS.md and architecture trees omit `OperatorConsole.cs`, `SintroJson.cs`,
  `IpRange.cs`; `W/core/format.js:36` still claims "most passes are anonymous", which commit
  `5ccb944` removed from the docs; `docs/superpowers/plans/2026-10-04-…md` still shows the old
  `type: "lanes"` frame (historical plan, fine as a record).

## Checks that came back clean

1. **Removed and renamed fields.** No code reads `total`, `totalUnavailable`, `shotValues`,
   `shotCount`, series `index`, `bestFineValue`, series `fineValues`, `mouche`, `targetProgram`,
   `programCount` from the wire. Every hit is the viewer's own row/group field, a loop variable,
   the device column, or prose. No `type` tag anywhere in `Live/`, `api.js`, `app.js`, tests.
   `LiveFeedTests` asserts `{"lanes":[]}` exactly. Every serialized property is in `docs/api.md`;
   no documented field is absent from the code. `V2Descriptions.cs` prose omits some property
   names, but the generated schema carries them.
2. **Browse row kinds.** All three builders set the identical 13 fields; every read in
   `browseRowHtml`, all seven sort comparators, `filterByTotal` and both export layouts is
   supplied. Unknown grouping degrades to program rows, empty result gives a message row. A probe
   pushed anonymous, null-`targetCode`, null-`startedAt`, empty-series and null-valuation programs
   through every kind, sort key, range and full-column export: no throw, no `undefined`. Test gaps
   (series/shot rows are never sorted, ranged or exported in a test; the cross-kind markup test
   asserts only `col-total`, so a missing string field would print `undefined` and pass; `at`,
   `targetCode`, `matchCode`, `license` sorts untested) are listed above as the owner asked.
3. **Result derivations.** For `A4 30 / A5 40 / A10 90 / ?? 12` the server answers four totals, the
   viewer shows `A4/A5 70 · A10 90 · ?? 12`, the live lane shows `12`: the only difference is the
   documented 4/5 merge. Null-valuation-only programs agree exactly (`??` key both sides).
   `value` equals the sum of its series' `subtotal`; sighting is excluded on both sides; no numeric
   drift, only the grouping differences in I1.
4. **Nullable and empty inputs.** `series: []`, `totals: []`, `shots: []`, `fineValue` null,
   `hitSector` null/0, `valuation` null, `shooter` null, `currentProgram` null, a lane program with
   no series: every path guarded (`??`, `?.`, `Number.isFinite`, length checks before `[0]`,
   `.at(-1)`, `Math.max(...)`); every `.reduce` has a seed; no `toFixed`; no `undefined` or `NaN`
   reaches output. The viewer never reads `totals[]`.
5. **Fixtures versus wire.** Envelopes `lanes`, `items/nextCursor/hasMore`, `today/publicExposure`,
   `error/detail` match the C# contracts; nulls are present, never omitted; enum casing matches.
   Only the two fixture shapes in I11 deviate.
6. **i18n.** `de` and `fr`: 101 identical keys; every key used, every used key defined; `en`
   equals exactly the five keys `docs.html` and `docs.js` use; all dynamic keys resolve for every
   value the code can produce.
7. **Seed.** Marker rows `ShotNr 9999 / HitPosition 255 / TotalType 7` and the last real shot also
   `TotalType 7`; `ShotNr` counts counting shots across series and sighting separately; sighting
   is `ShotType 0`; one valuation row per `(ProgramID, ShotGroup)`; sectors clockwise from twelve
   with the doc's angles; `Mouche 1` paired with `HitPosition 0` for fine 96 and better, zero
   mismatches; geometry bands match the doc's numbers; `StartTime` day-first text; `Shots.StartNr`
   always 0; licences six-digit zero-padded; `ExternalNumber` 0 on training days; `schema.sql`
   types agree with `Rows.cs`. Suite green against the seed (see commands).
8. **Documentation.** Endpoint paths, `/programs` query parameters, error codes, `innerTen` and
   `hitSector` semantics, totals and series shapes, `targetTitle`, `timesShot`, `{items}` catalog,
   `{lanes}` frame all agree across code, `docs/api.md`, `docs/architecture.md`,
   `docs/device-database.md`, README and tests, apart from I3 to I8 and N15. Script names in
   AGENTS.md match `scripts/` exactly.
9. **Security.** Gate before CORS before WebSockets before auth; constant-time digest compare;
   session token per start, never logged; bearer-only header; `?token=` only on a genuine upgrade
   to `/live`; forwarded chain unwound only through trusted hops, garbled hop refused; null peer is
   loopback; no allowlist default, empty list refused at startup; weak token (short, non-ASCII, same
   in both scopes) refused; `/openapi` token-free; no `AllowCredentials`, no exposed headers; the
   401 and 403 bodies never echo the presented token. Each bullet is guarded by a named test
   (`NetworkGateTests`, `SecurityTests`, `ClientAddressTests`, `TrustedProxyTests`, `LiveFeedTests`,
   `StartupCheckTests`, `IpRangeTests`). Only the CORS scope (B1) and the error-body claim (I9)
   fall short.

No real shooter name or licence number was used anywhere in this review; fixtures and examples are
the invented `Hans Muster` / `012345`.

## Commands run

| Command | Result |
|---|---|
| `scripts/test.sh` (dev export, pinned reference date) | .NET 254 passed, 0 failed; viewer 207 passed, 0 failed |
| `scripts/db-seed.sh` | exit 0 |
| `SINTRO_DB=SintroSeed SINTRO_REFERENCE_DATE= scripts/test.sh` | .NET 254 passed, 0 failed; viewer 207 passed, 0 failed |
| `wc -m AGENTS.md` | 10144 |
| `node --test` in `wwwroot/` (host node, by the reviewers) | 207 passed |
| `node seed/generate.mjs` into a scratch directory (inspection only) | 63 programs, 260 valuation rows, 1162 shots |

Docker was up; nothing was skipped.
