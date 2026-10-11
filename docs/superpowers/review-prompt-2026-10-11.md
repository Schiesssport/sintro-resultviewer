# Review prompt: consistency and bug hunt after the October API and viewer changes

---

Read `AGENTS.md` first and follow it; it is the project contract. Then review this repository
for bugs and inconsistencies. You are a reviewer, not an implementer: do not edit code, do not
commit. Deliver a written report at `docs/superpowers/reviews/2026-10-11-consistency-review.md`
and nothing else.

## Why this review

The last two weeks changed the API's result shape, the viewer that renders it, the seeded test
database and the documentation, in many small steps. One regression slipped through: the result
browser switched its shared row markup to read a new viewer-side field, `totals`, but only one of
the three row kinds supplied it, so grouping by series or by shot threw on the first row. The
default view worked and no test rendered the other row kinds, so nothing caught it until the
build ran on a range. The owner wants independent confirmation that nothing of that kind is left.

## What changed (all on `main`, see `git log --since=2026-10-01`)

API, `Api/V2/` and `Domain/`:

- A program carries `totals[]`: one entry per target and ring scale, each with `targetType`,
  `valuation` (nullable), `value` and `fineValues` in firing order. `total`, `totalUnavailable`
  and `shotValues` are gone.
- A series is exactly `targetType`, `valuation`, `subtotal`, `shots`. `index`, `shotCount`,
  `bestFineValue` and `fineValues` on a series are gone.
- A shot's centre-hit flag is `innerTen` (was `mouche`). `hitSector` 0 means "no direction".
- `targetProgram` is now `targetTitle`; the catalog's `programCount` is `timesShot`;
  `/program-catalog` answers `{items}`; `/live` answers `{lanes}` on GET and pushes the same
  frame on the WebSocket, with no type tag.

Viewer, `wwwroot/`:

- The shown result is derived from `series[]` in `core/format.js` (`resultTotals`,
  `resultText`, `totalsText`, `activeTotal`), never read from `totals[]`. Scales 4 and 5 add up
  to one number; any other mix shows every scale labelled. `core/markup.js` renders each pair as
  a `.total-part` span.
- The dial in `core/sectors.js` turns black on `innerTen` only; a bare sector 0 stays blank.
- `api.js` reloads the page when the live socket's session token is rejected (401 on a probe GET).
- The browse page (`browse.js` over `core/browse.js`) has three row kinds: program, series, shot.
- `/docs` is English apart from its intro; `en` in `core/i18n.js` holds only the keys that page uses.

Seed, `seed/`: X/Y are mm at the distance the fine value implies, `HitPosition` is read off the
coordinates, `Mouche` and sector 0 for fine 96 and better.

## What to check

Work through each item and record what you looked at, even when you find nothing.

1. **Every consumer of every removed or renamed field.** Grep the whole tree, viewer and tests
   included, for `total\b`, `totalUnavailable`, `shotValues`, `shotCount`, `index`, `bestFineValue`,
   `fineValues`, `mouche`, `targetProgram`, `programCount`, and for a `type` tag on live frames.
   Report any read of a field the API no longer sends, and any field the API sends that nothing
   documents in `docs/api.md` or the OpenAPI descriptions in `Api/V2/V2Descriptions.cs`.
2. **Every code path through every row kind in the browser.** For each of program, series and
   shot rows: the row builder in `core/browse.js`, the row markup in `core/markup.js`, sorting,
   the result range filter, and both export layouts. Each field the markup or export reads must be
   set by all three builders. Say which test proves it for each row kind; name the gaps.
3. **The three result derivations agree.** `resultTotals`/`resultText` (tables and ticker),
   `activeTotal` (live lane) and the server's `ScoreCalculator.BuildTotals` must group the same
   series the same way, except for the viewer's deliberate 4/5 merge. Construct a program with
   a 4er, a 5er, a 10er and a null-valuation series and trace all three.
4. **Nullable and empty inputs.** A series with no shots, a program with no series, a shot
   without `fineValue` or `hitSector`, `valuation: null`, `shooter: null`, a lane with
   `currentProgram: null`. Follow each through markup, format, sectors, lanes and browse. Look
   for `.length`, `Math.max(...[])`, `.at(-1)` and string interpolation of `undefined`.
5. **Viewer tests versus the live wire.** Compare the fixtures in `wwwroot/tests/*.test.js` with
   the JSON the API actually sends (start it with `scripts/run.sh`, or read `docs/api.md`). A
   fixture carrying a field the API no longer sends can make a test pass while the page fails.
6. **The i18n contract.** `de` and `fr` have identical keys; every key is used; every used key
   exists; `en` equals exactly the keys used by `docs.html` and `docs.js`. Also check dynamic
   keys built by template (`live.${state}`, `fullscreen.mode.${target}`) resolve for every value
   the code can produce.
7. **The seed against `docs/device-database.md`.** Every rule on that page the generator claims
   to reproduce: marker rows, `ShotNr` counting, sighting by `ShotType`, valuation rows per group,
   geometry bands, `Mouche` paired with `HitPosition` 0. Run `scripts/db-seed.sh` and the suite
   against it: `SINTRO_DB=SintroSeed SINTRO_REFERENCE_DATE= scripts/test.sh`.
8. **Documentation drift.** `docs/api.md`, `docs/architecture.md`, `docs/device-database.md`,
   `README.md`, `seed/README.md` and `AGENTS.md` against the code for the fields and behaviours
   listed above. `AGENTS.md` must stay under 10 000 characters (`wc -m`).
9. **Security invariants were not weakened by any of this.** Reread the Security model section
   of `AGENTS.md` and confirm each bullet against `Security/`, `Program.cs` and the tests named
   there. Any CORS, proxy or token change needs a test that would fail if it were wrong.

## How to report

For each finding: file and line, what the code does, what it should do, how to reproduce or
which input triggers it, and a severity: **bug** (wrong output or an exception a user can hit),
**inconsistency** (code, docs and tests disagree but nothing breaks), or **note**. Order by
severity. List the checks that came back clean too, one line each, so the owner knows they were
done. End with the exact commands you ran and their pass/fail summary.

Toolchain: everything runs in Docker through `scripts/*.sh`; if Docker is down, say so rather
than skipping a check. Never put a real shooter's name or licence number in the report; the dev
database may hold real data.
