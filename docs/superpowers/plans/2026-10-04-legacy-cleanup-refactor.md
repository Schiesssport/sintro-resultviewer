# Legacy Cleanup Refactor Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the leftovers of incremental growth (duplicated helpers, layering leaks, oversized files, narrating comments) without changing any API wire shape, any query result or any security default.

**Architecture:** The shape stays as `docs/architecture.md` describes it. Work is in seven independently shippable phases, each ending fully green on `scripts/test.sh`. Server phases make `Security/` and `Live/` independent of `Api/V2/`, split the three oversized files along the seams their own section dividers already mark, and remove one of two start-time parsers. Viewer phases extract the HTML-string builders into testable `core/` modules, share the three copies of the translation applier and the two copies of page-walking, and split `app.js` and `styles.css` along page boundaries. The last phase is the comment pass.

**Tech Stack:** .NET 10 minimal API, Dapper, xunit; plain ES modules under `node --test`; everything via `scripts/*.sh` in Docker.

**Spec:** This document. Section *Review findings* is the spec; section *Open questions* lists what the plan assumes until answered.

## Global Constraints

- Rules 1 to 4 of `AGENTS.md` hold throughout: read before edit, no real personal data, read-only API, never weaken security defaults.
- No wire change: every JSON key, enum name, error code and route stays. `docs/api.md` must need no edit except where this plan says so.
- No query semantics change: the mapping rules table in `AGENTS.md` is untouched, the integration tests assert the same invariants.
- Every URL in `README.md` keeps working, including `/fullscreen/*?resultCount=&tickerSkip=&tickerSeconds=&ticker=off` (see Q2).
- Translation keys: a key added or renamed is added or renamed in both `de` and `fr`.
- Style: functions about 25 lines, a comment is one line for a *why* that cannot be inferred, no section-divider comments (a divider means the file wants splitting).
- `scripts/test.sh` green after every task; `scripts/run.sh` and a manual load of `/`, `/fullscreen/live`, `/fullscreen/results`, `/fullscreen/live+results`, `/browse`, `/docs` after every viewer task.
- One commit per **phase** (Q11), message in the repository's existing style (`Area: what changed`); the per-task "Commit" steps below mean "stage and continue", the phase's last task commits.
- Branch `refactor/legacy-cleanup`. PR A after phase 3 (server), PR B after phase 6 (viewer, comments, docs).

## Review Focus

Behaviour the refactor could break that no existing test pins down. Each line names the task that adds the test.

1. French tooltips on `/browse` (the `data-i18n-title` buttons) must switch language with the selector. Task 1 adds the test.
2. A WebSocket upgrade with `?token=` must still be accepted only on `/api/v2/live` after the metadata change, and a plain GET with `?token=` must stay 401 on every path. `LiveFeedTests` already covers both; Task 4 re-runs them and adds the case for a metadata-free `/api` path.
3. The lane frame pushed on connect and on change must keep the shape `{"type":"lanes","lanes":[...]}` after the anonymous object becomes a record. Task 3 adds the serialisation test.
4. The result table's message row colspan must equal the column count on both `/` and `/browse` once the two pages share one message-row builder. Task 10 adds the test.
5. A program whose `StartTime` SQL cannot parse must still be reachable by id and report the epoch start after the C# fallback parser is removed. Task 6 adds the integration-free unit test on `ToProgram`.

---

## Review findings

What was found, file by file. Findings marked **(bug)** change behaviour; everything else is structure only.

### Server

| # | Where | Finding |
|---|---|---|
| S1 | `Security/NetworkGate.cs`, `Security/TokenAuth.cs` | Both `using Sintro.ResultViewer.Api.V2` for `ApiError`, `RoutePrefix`, and the `/live`, `/health` paths. `AGENTS.md` says v3 is "a new folder plus one `app.MapV3()` line"; today v3 would also need TokenAuth edited. |
| S2 | `Live/LaneWatcher.cs:349`, `Api/V2/V2Endpoints.cs:721` | The lane frame `new { type = "lanes", lanes = … }` is built twice as an anonymous object. |
| S3 | `Program.cs:53-68` | Viewer page routes, their `no-store` header and the route lists live in the composition root. |
| S4 | `StartupChecks.cs` (217 lines) | Two responsibilities: refusing bad configuration, and rendering the address banner (`LogReachableAddresses` down to `Format`, 75 lines). |
| S5 | `Data/SintroRepository.cs` (512 lines) | Section dividers `-- Programs --`, `-- Lanes --`, `-- Shooters & clubs --`, `-- Helpers --`; nested `LicenseIndex` class; `ListShootersAsync` takes 7 positional parameters where programs use `ProgramFilter`; the `TRY_CONVERT(datetime2, REPLACE(…, '-', ' '), 104)` expression is spelled out three times (`ProgramProjection`, `ListShootersAsync`, `ListProgramCatalogAsync`). |
| S6 | `Data/SintroRepository.cs:1120`, `Data/SintroTime.ParseStartTime`, `Data/Rows.cs` `ProgramRow.StartTime` | Start time is parsed twice: SQL `TRY_CONVERT` into `StartedAt`, then C# `ParseStartTime` on the raw text as a fallback. Both accept exactly `dd.MM.yyyy HH:mm:ss`, so the fallback never fires; `ProgramRow.StartTime` exists only to feed it. |
| S7 | `Data/Rows.cs` `ShotRow.ProgramID` | Nullable, then `row.ProgramID!.Value` at the one use. Every shot query filters `WHERE ProgramID IN @programIds`. |
| S8 | `Api/V2/V2Endpoints.cs` (389 lines) | Three things in one file: 140 lines of endpoint description text, the handlers, and the query parsers (`ParseState`, `ParseOrder`, `ParseIntList`, `SplitList`, `ClampLimit`). Dividers `-- Handlers --`, `-- Helpers --`. |
| S9 | `SintroOptions.ApiWriteTokens`, `StartupChecks` (two checks), `appsettings.jsonc`, `README`, three tests | A reserved write scope on an API whose rule 3 is "read-only". Speculative (YAGNI). See **Q1**. |
| S10 | `Live/LaneWatcher.cs:315` | `Math.Max(250, PollMilliseconds)` clamps silently; every other bad setting refuses at startup (`CheckPageSizes`). See **Q8**. |
| S11 | `Domain/Models.cs` | 14 comment lines in 84 restate API semantics that `docs/api.md` already documents (`// Programs.Number and Programs.Name: operator-assigned free text`). No `GenerateDocumentationFile`, so the `///` summaries reach neither OpenAPI nor consumers. |
| S12 | `Live/LiveHub.cs` | Comments narrate control flow (`// Shutting down.`, `// Client vanished.`). |

### Viewer

| # | Where | Finding |
|---|---|---|
| V1 **(bug)** | `browse.js:25-32` | `renderStaticText` applies `data-i18n`, `-placeholder`, `-aria-label` but not `data-i18n-title`. `browse.html` has six `data-i18n-title` buttons (clear, pick shooter, reload, export). They never translate. `docs.js:412` applies only `data-i18n`. `app.js:765` is the complete version. Three copies, one correct. |
| V2 | `api.js:421-447` | `allPrograms` and `allShooters` are the same loop with a different path. |
| V3 | `app.js:1078-1086`, `browse.js:275-282` | "today from `/health`, else local ISO date" is written twice. |
| V4 | `app.js` (625 lines) | Section dividers `-- Shared cells --`, `-- Lines --`, `-- Results --`, `-- Ticker --`, `-- Static text --`, `-- Data --`, `-- Routing --`, `-- Fullscreen picker --`, `-- Wiring --`, `-- Start --`. The *Shared cells* block (`totalCell` … `shotGroupsCell`, 70 lines) and `tickerMarkup` build HTML strings from data and `t` only, never touch `document`, and are therefore `core/` material that is currently untested. The boards picker (`loadBoards` … `exitFullscreen`, 140 lines) is a self-contained dialog. |
| V5 | `app.js:506` `RESULT_LIMIT = 50`, `core/ticker.js:1215` `DEFAULT_RESULT_COUNT = 50`, `app.js:908` `max="500"` vs `core/ticker.js:1217` `MAX_RESULT_COUNT = 500` | The same two numbers defined twice each. |
| V6 | `core/ticker.js` | Owns `results` (how many results a display loads), which is not a ticker setting; `normaliseTickerSettings` accepts `hidden === true` (storage) and `hidden === 'off'` (URL) in one function, mixing parsing and normalising. `core/boards.js` `boardQuery` is an alias of `tickerQuery` with no added behaviour. Naming is left over from when the ticker owned the result count. |
| V7 | `browse.js:67` `colspan="8"`, `app.js:530` `resultColumns()` | One page hard-codes the message row colspan, the other reads it from the `<colgroup>` (the rule `AGENTS.md` states). |
| V8 | `core/format.js:673` `formatTime`, `core/browse.js:489` `formatDateTime` | Two ISO-text readers in two modules; `formatDateTime` belongs beside `formatTime`. `core/format.js` `shotGroups` and `core/browse.js` `seriesGroup` both map a series to `{code, values}`. |
| V9 | `docs.js:296` | `const language = DEFAULT_LANGUAGE` with no selector, while `fr` carries 17 `docs.*` strings that can never show. See **Q3**. |
| V10 | `wwwroot/tests/i18n.test.js:16` | The file list for "every used key exists" is hand-maintained; splitting `app.js` would silently drop keys from the check. |
| V11 | `styles.css` (957 lines) | Duplicate selector blocks: `body.is-fullscreen` (lines 408, 590, 679), `body.is-fullscreen .card`, `body.is-fullscreen #ui-wrapper`, `body.is-fullscreen table.results`, `.browse-actions` (901, 1005), `.col-total` (235, 243). Sections out of order (*Header button* and *Sections* after *Fullscreen*; picker checkbox and `#add-board` appended at the very end). Comments describe removed mechanisms: line 678 "so the row-capacity measurement has a stable box", line 731 "as a table footer row it needed an exact row count" (both predate commit 871c790 "simpler ticker"). `.endpoint-body.hidden`, `.response-status.hidden`, `pre.response.hidden` duplicate the global `.hidden { display: none !important }`. `pre.response` sets `margin-top: 0` then `margin: 12px 0 0`. `/docs` loads 900 lines of lane, ticker and browser CSS it never uses. See **Q4**. |
| V12 | `core/boards.js` `parseBoards` | Normalises whatever shape `localStorage` holds. The user has stated no compatibility with past browser settings is needed; the normalisation stays only because it also supplies defaults. No change, noted for completeness. |

### Docs

| # | Where | Finding |
|---|---|---|
| D1 | `docs/architecture.md:103` | The viewer table lists `app.js, docs.js` as the app layer; `browse.js` is missing. The new file layout from this plan needs the table and `AGENTS.md` updated. |

---

## Decisions (questions answered 2026-10-04)

| # | Question | Decision | Affects |
|---|---|---|---|
| **Q1** | Drop the reserved `ApiWriteTokens` scope? | **Keep.** Writing is planned logic. Task 14 is dropped. | none |
| **Q2** | Keep the display URL parameter names `resultCount`, `tickerSkip`, `tickerSeconds`, `ticker=off`? | **Keep.** They are in use on displays. Module and function names change, the wire does not. | Task 9 |
| **Q3** | `/docs` language? | **`/docs` gets English, German and French; the user-level pages (`/`, `/browse`) stay German and French.** English exists only for the keys the docs page uses. | Task 1 |
| **Q4** | Split `styles.css` per page? | **No.** One file, reusable and unified: reorder and deduplicate only. | Task 13 |
| **Q5** | How to split `SintroRepository`? | **New directory `Data/Sintro300/` holding everything that knows the device schema, split into one file per entity, behind an `ISintroRepository` interface** so another device or schema version can be implemented later. No second implementation now. | Task 7 |
| **Q6** | Remove the C# start-time fallback parser? | **Remove.** | Task 6 |
| **Q7** | `TokenAuth` via endpoint metadata? | **Yes** (left to the plan's judgement). | Task 4 |
| **Q8** | Refuse a poll interval under 250 ms at startup? | **Yes.** | Task 2 |
| **Q9** | Keep `shotValues`? | **Out of scope here.** The user sees a design flaw: `total` assumes one valuation per program, while `total.value` + `shotValues` + `valuation` make a simple import easy. **Follow-up task, separately: rethink `total`, `totalUnavailable`, `shotValues` for consistency.** Nothing in this plan touches them. | none |
| **Q10** | Comment criterion? | **Agreed:** no comments that explain what the code does; annotate only unexpected or figured-out behaviour. | Task 15 |
| **Q11** | PR and commit grouping? | **One branch, one commit per phase (no micro commits), two PRs:** PR A = phases 1 to 3 (server), PR B = phases 0, 4, 5, 6 (viewer, comments, docs). | all |

**Execution:** subagent-driven (`superpowers:subagent-driven-development`).

---

## File structure after the plan

```
src/Sintro.ResultViewer.Api/
  Program.cs                      composition root only (~55 lines)
  OperatorSettings.cs             AddOperatorSettings (the appsettings.jsonc registration)
  StartupChecks.cs                refusals and warnings only
  StartupBanner.cs                reachable-address banner
  Api/ApiError.cs                 shared error envelope (was Api/V2/V2Contracts.cs)
  Api/V2/V2Endpoints.cs           MapV2 + handlers
  Api/V2/V2Descriptions.cs        the OpenAPI summary and description texts
  Api/V2/V2Query.cs               ParseState, ParseOrder, ParseIntList, SplitList, ClampLimit
  Api/V2/V2Contracts.cs           ShooterDetail, HealthReport
  Data/ISintroRepository.cs       the contract every device/schema implementation fulfils
  Data/ProgramFilter.cs, ShooterFilter.cs, Cursor.cs, LicenseNumber.cs, SintroClock.cs   schema-independent
  Data/Sintro300/                 EVERYTHING that knows the Sintro 300 schema
    SintroRepository.cs           : ISintroRepository; connection, QueryPageAsync, ContainsPattern, StartedAtSql
    SintroRepository.Programs.cs  (partial)
    SintroRepository.Lanes.cs
    SintroRepository.Shooters.cs  shooters + clubs
    SintroRepository.Catalog.cs   program catalog + CanReachDatabaseAsync
    Rows.cs, ScoreCalculator.cs, SintroTime.cs, TargetKind.cs, LicenseIndex.cs
  Live/LanesFrame.cs
  Security/QueryTokenOnUpgrade.cs
  Viewer/ViewerEndpoints.cs       MapViewer: the HTML routes with no-store
  wwwroot/
    app.js                        live view: data, routing, wiring, start
    boards-dialog.js              the fullscreen picker (DOM)
    browse.js, docs.js, api.js
    dom.js                        applyTranslations, readToday  (DOM-touching helpers shared by pages)
    core/markup.js                HTML-string builders: cells, chips, dial, message row, ticker run
    core/display.js               display settings (was ticker.js)
    core/boards.js, format.js, lanes.js, sectors.js, viewmode.js, openapi.js, reconnect.js, browse.js, i18n.js
    styles.css                    one file, reordered: base, tables and chips, live view, fullscreen, ticker, picker, browse, docs
    tokens.css
```

---

## Phase 0: the bug and the shared translation applier

### Task 1: One `applyTranslations` for all three pages

**Files:**
- Create: `src/Sintro.ResultViewer.Api/wwwroot/dom.js`
- Modify: `src/Sintro.ResultViewer.Api/wwwroot/app.js:765-784`, `browse.js:25-32`, `docs.js:410-414`, `docs.html` (selector, Q3), `core/i18n.js` (no new keys unless Q3 adds `aria.language` use on docs, which already exists)
- Test: `src/Sintro.ResultViewer.Api/wwwroot/tests/i18n.test.js`

**Interfaces:**
- Produces: `export const applyTranslations = (t, root = document) => void` in `dom.js`. Applies `data-i18n` → `textContent`, `data-i18n-placeholder` → `placeholder`, `data-i18n-title` → `title`, `data-i18n-aria-label` → `aria-label`, and sets `document.documentElement.lang` is **not** its job (the caller sets `lang` and `title`, which differ per page).

- [ ] **Step 1: Write the failing test.** `dom.js` touches the DOM, so the unit test checks the HTML side of the bug instead: every `data-i18n-*` attribute name used in any HTML file is one of the four the applier handles, and every page script imports `applyTranslations`. Add to `i18n.test.js`:

```js
describe('translation attributes', () => {
    const ATTRIBUTES = ['data-i18n', 'data-i18n-placeholder', 'data-i18n-title', 'data-i18n-aria-label'];

    test('every page applies translations through the shared helper', () => {
        for (const file of ['app.js', 'browse.js', 'docs.js']) {
            assert.match(source(file), /import \{[^}]*\bapplyTranslations\b[^}]*\} from '\.\/dom\.js'/, `${file} must use dom.js`);
            assert.doesNotMatch(source(file), /data-i18n/, `${file} must not walk data-i18n itself`);
        }
    });

    test('the markup uses only attributes the helper handles', () => {
        for (const file of ['index.html', 'browse.html', 'docs.html']) {
            for (const [attribute] of source(file).matchAll(/data-i18n(?:-[a-z-]+)?(?==)/g)) {
                assert.ok(ATTRIBUTES.includes(attribute), `${file}: ${attribute}`);
            }
        }
    });
});
```

- [ ] **Step 2: Run it, expect failure.** `scripts/test-web.sh` → the first test fails for all three files (no `dom.js`).

- [ ] **Step 3: Create `dom.js`.**

```js
// DOM helpers shared by the three pages. Nothing here is pure; core/ stays DOM-free.

const ATTRIBUTE_TARGETS = [
    ['i18n', (node, text) => { node.textContent = text; }],
    ['i18nPlaceholder', (node, text) => { node.placeholder = text; }],
    ['i18nTitle', (node, text) => { node.title = text; }],
    ['i18nAriaLabel', (node, text) => node.setAttribute('aria-label', text)],
];

export const applyTranslations = (t, root = document) => {
    for (const [dataKey, apply] of ATTRIBUTE_TARGETS) {
        const attribute = `data-${dataKey.replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}`;
        for (const node of root.querySelectorAll(`[${attribute}]`)) apply(node, t(node.dataset[dataKey]));
    }
};
```

- [ ] **Step 4: Replace the three copies.** In `app.js` `renderStaticText` becomes:

```js
const renderStaticText = () => {
    document.documentElement.lang = language;
    document.title = t('app.title');
    applyTranslations(t);
    liveState(liveStatus);
    showExposureWarning();
};
```

`browse.js` the same with `t('browse.title')`.

**`/docs` in three languages (Q3).** In `core/i18n.js` add `TRANSLATIONS.en` holding **only** the keys the docs page uses: every `docs.*` key plus `app.title`, `footer.note`, `aria.language`. Export `DOCS_LANGUAGES = ['de', 'fr', 'en']`. `docs.html` gets the `<select id="language-select">` from `browse.html` with a third `<option value="en">EN</option>`; `docs.js` makes `language` a `let`, keeps the loaded spec in a module variable, and on `change` re-runs `renderStaticText()` and `render(spec)`. The user-level pages keep their two options.

Adjust `i18n.test.js`: "French covers exactly the same keys as German" stays; add "English covers exactly the keys the docs page uses" (keys collected from `docs.html` and `docs.js` with the same regexes as `keysInUse`, which must therefore be refactored into `keysInFiles(files)`); "placeholders match between languages" iterates over `en` keys too; "no translation is left empty" already loops over every dictionary.

- [ ] **Step 5: Run tests, expect pass.** `scripts/test-web.sh`.

- [ ] **Step 6: Manual check.** `scripts/run.sh`, open `/browse`, switch to FR, hover the clear button: tooltip reads "Effacer".

- [ ] **Step 7: Commit.** `Viewer: one applyTranslations for all pages; browse tooltips translate`

---

## Phase 1: server layering

### Task 2: Poll interval is checked at startup (Q8)

**Files:**
- Modify: `StartupChecks.cs` (add `CheckPollInterval`), `Live/LaneWatcher.cs:315` (drop `Math.Max`), `SintroOptions.cs` (`LiveOptions.MinimumPollMilliseconds = 250`), `appsettings.jsonc` (one sentence in the `PollMilliseconds` comment: "at least 250")
- Test: `tests/Sintro.ResultViewer.Tests/SecurityTests.cs` (`StartupCheckTests`)

- [ ] **Step 1: Failing test**, next to `impossiblePageSizes_refuseToStart`:

```csharp
[Theory]
[InlineData(0)]
[InlineData(249)]
public void aPollIntervalBelowTheFloor_refusesToStart(int milliseconds)
{
    var settings = ValidSettings();
    settings.Live.PollMilliseconds = milliseconds;
    var ex = Assert.Throws<InvalidOperationException>(() => StartupChecks.Run(Logger, settings, new SessionToken()));
    Assert.Contains("Sintro:Live:PollMilliseconds", ex.Message);
}
```

(`ValidSettings()` and `Logger` already exist in that class; reuse them.)

- [ ] **Step 2: Run, expect failure.** `scripts/test.sh` (or `scripts/dotnet.sh test --filter aPollInterval`).
- [ ] **Step 3: Implement.** In `LiveOptions`: `public const int MinimumPollMilliseconds = 250;`. In `StartupChecks.Run` add `CheckPollInterval(settings);`:

```csharp
private static void CheckPollInterval(SintroOptions settings)
{
    if (settings.Live.PollMilliseconds < LiveOptions.MinimumPollMilliseconds)
        throw new InvalidOperationException(
            $"Sintro:Live:PollMilliseconds is {settings.Live.PollMilliseconds}; it must be at least " +
            $"{LiveOptions.MinimumPollMilliseconds}.");
}
```

In `LaneWatcher`: `var interval = TimeSpan.FromMilliseconds(options.Value.Live.PollMilliseconds);`.
- [ ] **Step 4: Run, expect pass.**
- [ ] **Step 5: Commit.** `Startup: refuse a poll interval below 250 ms instead of clamping`

### Task 3: `ApiError` becomes shared; the lane frame becomes a record

**Files:**
- Create: `Api/ApiError.cs` (namespace `Sintro.ResultViewer.Api`), `Live/LanesFrame.cs`
- Modify: `Api/V2/V2Contracts.cs` (remove `ApiError`), `Security/NetworkGate.cs`, `Security/TokenAuth.cs` (`using Sintro.ResultViewer.Api;`), `Live/LaneWatcher.cs:349`, `Api/V2/V2Endpoints.cs:721`
- Test: `tests/Sintro.ResultViewer.Tests/LiveFeedTests.cs`

**Interfaces:**
- Produces: `public sealed record LanesFrame(IReadOnlyList<LaneStatus> Lanes) { public string Type => "lanes"; }` in `Live/`. Serialised with `SintroJson.Options` (camelCase) it yields `{"lanes":[…],"type":"lanes"}`; key order is irrelevant to every consumer.

- [ ] **Step 1: Failing test** in `LiveFeedTests` (pure, no `[RequiresDatabaseFact]`):

```csharp
[Fact]
public void theLaneFrameSerialisesWithItsDocumentedType()
{
    var json = JsonSerializer.Serialize(new LanesFrame([]), SintroJson.Options);
    using var document = JsonDocument.Parse(json);
    Assert.Equal("lanes", document.RootElement.GetProperty("type").GetString());
    Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("lanes").ValueKind);
}
```

- [ ] **Step 2: Run, expect compile failure** (`LanesFrame` undefined).
- [ ] **Step 3: Implement.** Create the two files; replace both anonymous objects with `new LanesFrame(await repository.ListLanesAsync(token))`; move `ApiError` out of `V2Contracts.cs` with its summary; fix the two `using` lines in `Security/`. `V2Endpoints` keeps `using Sintro.ResultViewer.Api;` implicitly through namespace nesting.
- [ ] **Step 4: Run the whole suite, expect pass.** `theCurrentLaneStateIsPushedOnConnect` still reads `type == "lanes"`.
- [ ] **Step 5: Commit.** `Server: ApiError shared across versions; LanesFrame record replaces two anonymous objects`

### Task 4: `TokenAuth` reads endpoint metadata instead of V2 paths (Q7)

**Files:**
- Create: `Security/QueryTokenOnUpgrade.cs`
- Modify: `Security/TokenAuth.cs`, `Api/V2/V2Endpoints.cs` (`MapLive`, `MapOperations`)
- Test: `tests/Sintro.ResultViewer.Tests/LiveFeedTests.cs`, `SecurityTests.cs`

**Interfaces:**
- Produces: `public sealed class QueryTokenOnUpgrade;` (marker metadata). `MapLive` adds `.WithMetadata(new QueryTokenOnUpgrade())`; `MapOperations` adds `.AllowAnonymous()` to `/health`.

- [ ] **Step 1: Failing test** in `LiveFeedTests`: a WebSocket upgrade attempt with `?token=` against `/api/v2/programs` (no marker) must be refused with 401, not upgraded.

```csharp
[RequiresDatabaseFact]
public async Task theQueryTokenIsAcceptedOnlyWhereAnEndpointDeclaresIt()
{
    using var client = fixture.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/programs?token={ApiFixture.Token}");
    request.Headers.Connection.Add("Upgrade");
    request.Headers.Upgrade.Add(new System.Net.Http.Headers.ProductHeaderValue("websocket"));
    request.Headers.Add("Sec-WebSocket-Version", "13");
    request.Headers.Add("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));
    var response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

- [ ] **Step 2: Run, expect failure.** Today the path check limits `?token=` to `/live`, so this passes already; that is fine. The point of the step is a guard that keeps passing after the rewrite. Note it in the commit message.
- [ ] **Step 3: Rewrite `TokenAuth`.**

```csharp
public async Task InvokeAsync(HttpContext context)
{
    if (RequiresToken(context) && !IsKnownToken(PresentedToken(context)))
    {
        // unchanged 401 body
    }
    await next(context);
}

// /api is the one convention shared by every version; an endpoint opts out with AllowAnonymous.
private static bool RequiresToken(HttpContext context) =>
    context.Request.Path.StartsWithSegments("/api") &&
    context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null;

private static string? PresentedToken(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return header["Bearer ".Length..].Trim();

    // Browsers cannot set headers on a WebSocket handshake; IsWebSocketRequest is meaningful only after UseWebSockets.
    if (context.WebSockets.IsWebSocketRequest &&
        context.GetEndpoint()?.Metadata.GetMetadata<QueryTokenOnUpgrade>() is not null &&
        context.Request.Query.TryGetValue("token", out var query))
        return query[0];

    return null;
}
```

Remove `LivePath`, `HealthPath` and the `using Sintro.ResultViewer.Api.V2;`. In `V2Endpoints`: `MapLive` chain gets `.WithMetadata(new QueryTokenOnUpgrade())`; `MapOperations` gets `.AllowAnonymous()`.

- [ ] **Step 4: Run the whole suite.** Must pass: `healthNeedsNoToken_soMonitoringCanReachIt`, `theLiveFeedAcceptsTheTokenAsAQueryParameter`, `aPlainGetDoesNotAcceptTheTokenInTheQueryString`, `theGateAlsoCoversHealthWhichNeedsNoToken`, and the new test.
- [ ] **Step 5: Update `AGENTS.md`** security bullet: "`UseWebSockets()` must stay before `UseTokenAuth()`; `?token=` is accepted only on a genuine upgrade **to an endpoint carrying `QueryTokenOnUpgrade`**. `/health` is open through `.AllowAnonymous()`." Keep under 10 000 characters (check with `wc -c AGENTS.md`).
- [ ] **Step 6: Commit.** `Security: TokenAuth exemptions come from endpoint metadata, not V2 path constants`

### Task 5: Viewer routes and the banner leave `Program.cs` and `StartupChecks.cs`

**Files:**
- Create: `Viewer/ViewerEndpoints.cs`, `StartupBanner.cs`, `OperatorSettings.cs`
- Modify: `Program.cs`, `StartupChecks.cs`
- Test: existing `SecurityTests` (`pagesCarryingTheSessionTokenAreNeverCached`, `eachFullscreenVariantServesTheViewer`, `anUnknownFullscreenVariantIsNotServed`) and `AppSettingsTests.Registration`

**Interfaces:**
- Produces: `public static void MapViewer(this WebApplication app)` in `Viewer/ViewerEndpoints.cs`; `public static void LogReachableAddresses(ILogger, IEnumerable<string>)` moves unchanged to `StartupBanner`; `public static void AddOperatorSettings(this ConfigurationManager)` in `OperatorSettings.cs`.

- [ ] **Step 1: No new test.** The behaviour is pinned by the tests named above; run them first to see them green.
- [ ] **Step 2: Move.** `ViewerEndpoints.cs`:

```csharp
public static class ViewerEndpoints
{
    private static readonly string[] LiveRoutes = ["/", "/index.html", "/fullscreen/live", "/fullscreen/results", "/fullscreen/live+results"];
    private static readonly string[] DocsRoutes = ["/docs", "/docs.html"];
    private static readonly string[] BrowseRoutes = ["/browse", "/browse.html"];

    // The fullscreen variants are client-side routes, listed so an unknown one is a 404, not a guess.
    public static void MapViewer(this WebApplication app)
    {
        MapPage(app, LiveRoutes, "index.html");
        MapPage(app, DocsRoutes, "docs.html");
        MapPage(app, BrowseRoutes, "browse.html");
    }

    private static void MapPage(WebApplication app, string[] routes, string fileName)
    {
        foreach (var route in routes)
            app.MapGet(route, (HttpContext context, ViewerPage page, SessionToken token) => Render(context, page, token, fileName))
               .ExcludeFromDescription();
    }

    // no-store: the page carries the session token, which must not outlive the process on a shared PC.
    private static IResult Render(HttpContext context, ViewerPage page, SessionToken token, string fileName)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Content(page.Render(fileName, token.Value), "text/html; charset=utf-8");
    }
}
```

`StartupBanner.cs` takes `LogReachableAddresses`, `ResolveDisplayUrls`, `RenderBanner`, `IsWildcard`, `LocalAddresses`, `Format` verbatim. `OperatorSettings.cs` takes `AddOperatorSettings` as an extension method. `Program.cs` becomes: builder, `builder.Configuration.AddOperatorSettings()`, services, `StartupChecks.Run`, middleware, `app.MapV2(); app.MapOpenApi(); app.MapViewer(); app.UseStaticFiles();`, `ApplicationStarted` → `StartupBanner.LogReachableAddresses`, `app.Run()`.
- [ ] **Step 3: Run the whole suite, expect pass.**
- [ ] **Step 4: Update `AGENTS.md`** architecture block: add `StartupBanner.cs`, `Viewer/ViewerEndpoints.cs`.
- [ ] **Step 5: Commit.** `Server: viewer routes in Viewer/, banner in StartupBanner, Program.cs is the composition root only`

---

## Phase 2: repository

### Task 6: Single start-time parser (Q6) and non-null `ShotRow.ProgramID`

**Files:**
- Modify: `Data/SintroRepository.cs` (`ToProgram`, `LoadShotsAsync`, three SQL expressions → `StartedAtSql`), `Data/Rows.cs`, `Data/SintroTime.cs` (remove `ParseStartTime`, `StartTimeFormat`), `tests/SintroTimeTests.cs` (remove the three `parseStartTime_*` tests), `docs/device-database.md` ("Dates are text": one sentence saying SQL `TRY_CONVERT(…, 104)` is the parser and an unparseable start is reported as the epoch)
- Test: `tests/Sintro.ResultViewer.Tests/ScoreCalculatorTests.cs` or a new `ProgramMappingTests.cs`

**Interfaces:**
- Produces: `private const string StartedAtSql = "TRY_CONVERT(datetime2, REPLACE({0}, '-', ' '), 104)"` used via `string.Format(StartedAtSql, "pr.StartTime")` in the three places. `ProgramRow` loses `StartTime`. `ShotRow.ProgramID` is `int`.

- [ ] **Step 1: Failing test.** `ToProgram` is private; expose the epoch rule through a small public static `SintroRepository.StartOf(ProgramRow row) => row.StartedAt ?? DateTime.UnixEpoch` and test it:

```csharp
public class ProgramMappingTests
{
    [Fact]
    public void anUnparseableStartTimeIsReportedAsTheEpochSoBadDataIsVisible()
    {
        var row = new ProgramRow(1, 0, "x", 1, null, null, StartedAt: null, EndShotId: null, CountingShots: 0, IsActive: 0);
        Assert.Equal(DateTime.UnixEpoch, SintroRepository.StartOf(row));
    }
}
```

- [ ] **Step 2: Run, expect compile failure** (`ProgramRow` still has `StartTime`, `StartOf` missing).
- [ ] **Step 3: Implement.** Remove the column from the CTE `prog` and `enriched`, from `ProgramRow`, remove `ParseStartTime`; `ToProgram` uses `StartOf(row)`. Make `ShotRow.ProgramID` non-nullable and `LoadShotsAsync` group on `row.ProgramID`. Introduce `StartedAtSql` and use it in `ProgramProjection`, `ListShootersAsync` (twice) and `ListProgramCatalogAsync`.
- [ ] **Step 4: Run the whole suite** (integration tests need the dev database up: `scripts/test.sh` does that). Expect pass; `aPassWithoutAShooterIsStillIdentifiable` and `timestampsAreIso8601WithAnOffset` exercise the start time.
- [ ] **Step 5: Verify with SQL** that nothing in the export has an unparseable start, so the change is observable only on bad data: `scripts/sql.sh "SELECT COUNT(*) FROM Programs WHERE TRY_CONVERT(datetime2, REPLACE(StartTime,'-',' '), 104) IS NULL"` → expect 0 on the dev export (do not hard-code that in a test).
- [ ] **Step 6: Commit.** `Data: SQL is the only start-time parser; ShotRow.ProgramID is never null`

### Task 7: `Data/Sintro300/` behind `ISintroRepository`, one file per entity, `ShooterFilter` (Q5)

**Files:**
- Create: `Data/ISintroRepository.cs`, `Data/ShooterFilter.cs`, `Data/Sintro300/SintroRepository.cs`, `Data/Sintro300/SintroRepository.Programs.cs`, `Data/Sintro300/SintroRepository.Lanes.cs`, `Data/Sintro300/SintroRepository.Shooters.cs`, `Data/Sintro300/SintroRepository.Catalog.cs`, `Data/Sintro300/LicenseIndex.cs`
- Move (git mv, namespace `Sintro.ResultViewer.Data.Sintro300`): `Data/Rows.cs`, `Data/ScoreCalculator.cs`, `Data/SintroTime.cs`, `Data/TargetKind.cs` → `Data/Sintro300/`
- Delete: `Data/SintroRepository.cs`
- Modify: `Program.cs` (`AddSingleton<ISintroRepository>(…)`), `Api/V2/V2Endpoints.cs` (inject `ISintroRepository`; `ListShooters` builds a `ShooterFilter`), `Live/LaneWatcher.cs` (inject `ISintroRepository`), tests: `using Sintro.ResultViewer.Data.Sintro300;` in `ScoreCalculatorTests`, `SintroTimeTests`, `ProgramMappingTests`; `ApiFixture.DatabaseReachableAsync` resolves `ISintroRepository`; `WebApplicationFactory<SintroRepository>` → `WebApplicationFactory<Program>` in `ApiFixture`, `GatedApiFixture`, `TrustedProxyApiFixture` (add `public partial class Program;` at the end of `Program.cs` to make the entry point visible)
- Test: existing `CatalogEndpointTests`, `CursorPagingTests`, `ProgramEndpointTests`

**Interfaces:**
- Produces:

```csharp
namespace Sintro.ResultViewer.Data;

/// <summary>Read-only access to one device database. Implementations live in a folder named after the device schema (Sintro300); every result is in the shared Domain model, so a client cannot tell them apart.</summary>
public interface ISintroRepository
{
    Task<CursorPage<ShootingProgram>> ListProgramsAsync(ProgramFilter filter, CancellationToken token);
    Task<ShootingProgram?> GetProgramAsync(int id, CancellationToken token);
    Task<IReadOnlyList<LaneStatus>> ListLanesAsync(CancellationToken token);
    Task<string> ReadLiveFingerprintAsync(CancellationToken token);
    Task<CursorPage<Shooter>> ListShootersAsync(ShooterFilter filter, CancellationToken token);
    Task<IReadOnlyList<Shooter>> FindShootersByLicenseAsync(string license, CancellationToken token);
    Task<CursorPage<Club>> ListClubsAsync(string? query, int limit, string? cursor, CancellationToken token);
    Task<IReadOnlyList<ProgramCatalogEntry>> ListProgramCatalogAsync(CancellationToken token);
    Task<bool> CanReachDatabaseAsync(CancellationToken token);
}

public sealed record ShooterFilter
{
    public string? Query { get; init; }
    public int? ClubId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public required int Limit { get; init; }
    public string? Cursor { get; init; }
}
```

`Data/Sintro300/SintroRepository.cs`: `public sealed partial class SintroRepository(string connectionString, ISintroClock clock) : ISintroRepository` keeping `Connect`, `QueryPageAsync`, `ContainsPattern`, `StartedAtSql`, `ToClub`, `StartOf`. The other partial files hold one entity each. `LicenseIndex` is `internal sealed class` in its own file. No second implementation is written (the user asked for the seam only).

- [ ] **Step 1: Write the interface and `ShooterFilter`** first; make the existing class implement it; change DI and the two consumers to the interface; run the suite green. (This proves the seam before anything moves.)
- [ ] **Step 2: `git mv`** the four schema files into `Data/Sintro300/`, fix namespaces and test `using`s; run the suite green.
- [ ] **Step 3: Split the class** into the partial files inside `Data/Sintro300/`, deleting the divider comments; move `LicenseIndex` out as `internal`.
- [ ] **Step 4: Run the whole suite, expect pass.** No wire change.
- [ ] **Step 5: Update `AGENTS.md`**: architecture block gets `Data/Sintro300/` ("EVERYTHING that knows the device schema; one file per entity"), single source of truth becomes "SQL → `Data/Sintro300/SintroRepository*.cs`", "Scoring → `Data/Sintro300/ScoreCalculator.cs`", "Target letters → `Data/Sintro300/TargetKind.cs`"; add one line "Another device or schema version is a new folder implementing `ISintroRepository`". `docs/architecture.md` Layers block the same. Keep `AGENTS.md` under 10 000 characters.
- [ ] **Step 6: Stage.** `Data: schema-specific code in Data/Sintro300 behind ISintroRepository; ShooterFilter mirrors ProgramFilter`

---

## Phase 3: V2 endpoints

### Task 8: Descriptions and query parsing leave `V2Endpoints.cs`

**Files:**
- Create: `Api/V2/V2Descriptions.cs` (`internal static class V2Descriptions` with `const string CollectionHelp, Live, Programs, Program, Shooters, Shooter, Clubs, Catalog, Health`), `Api/V2/V2Query.cs` (`internal static class V2Query` with `ParseState`, `ParseOrder`, `ParseIntList`, `SplitList`, `ClampLimit`)
- Modify: `Api/V2/V2Endpoints.cs`
- Test: existing `CatalogEndpointTests.aMisspelledFilterIsRejectedRatherThanIgnored`, `everyValidStateAndOrderIsAccepted`, `ProgramEndpointTests.listFiltersUnionTheirValuesAndRefuseNonNumbers`, `SecurityTests.theOpenApiDocumentExposesNoShooterData`

- [ ] **Step 1: Move.** Cut the text constants verbatim into `V2Descriptions`; `.WithDescription(V2Descriptions.Programs)`. Cut the five parsers into `V2Query`; call sites become `V2Query.ParseState(state, out var parsedState)`. Delete the `-- Handlers --` and `-- Helpers --` dividers.
- [ ] **Step 2: Run the whole suite, expect pass.** Also `curl -s localhost:8080/openapi/v2.json | jq '.paths["/api/v2/programs"].get.description' | head -c 200` after `scripts/run.sh` to see the description text intact.
- [ ] **Step 3: Commit.** `API v2: descriptions and query parsing in their own files`

---

## Phase 4: viewer modules

### Task 9: `core/ticker.js` becomes `core/display.js` (Q2: wire names unchanged)

**Files:**
- Create: `core/display.js`; delete `core/ticker.js`
- Modify: `core/boards.js`, `app.js`, `tests/ticker.test.js` → rename to `tests/display.test.js`, `tests/boards.test.js`
- Test: `tests/display.test.js`

**Interfaces:**
- Produces:

```js
export const DEFAULT_RESULT_COUNT = 50;
export const MAX_RESULT_COUNT = 500;
export const normaliseDisplaySettings = ({ seconds, results, skip, hidden }) => ({ seconds, results, skip, hidden: boolean });
export const parseDisplayQuery = (search) => normaliseDisplaySettings({ …, hidden: params.get('ticker') === 'off' });
export const displayQuery = (settings) => '?resultCount=…&tickerSkip=…&tickerSeconds=…[&ticker=off]';
export const tickerDurationSeconds = …;   // unchanged
export const tickerContentKey = …;        // unchanged
```

`normaliseDisplaySettings` takes `hidden` as a boolean only; the `'off'` string is interpreted in `parseDisplayQuery`. `boards.js` imports `normaliseDisplaySettings`, `displayQuery`; `boardQuery` is deleted and `boardPath` calls `displayQuery(board)` directly.

- [ ] **Step 1: Rename the test file and update imports; add the test** that `normaliseDisplaySettings({ hidden: 'off' }).hidden === false` (a string is not a boolean any more) and that `parseDisplayQuery('?ticker=off').hidden === true`.
- [ ] **Step 2: Run, expect failure** (module missing).
- [ ] **Step 3: Implement** the rename; `app.js`: `let display = parseDisplayQuery(location.search)`, `display.results`, `display.skip`, `display.seconds`, `display.hidden`. `numberField` max becomes `MAX_RESULT_COUNT`; `RESULT_LIMIT` is replaced by `DEFAULT_RESULT_COUNT`.
- [ ] **Step 4: Run, expect pass**; `boards.test.js` "the path carries mode and settings in the documented order" guards the query string.
- [ ] **Step 5: Manual check:** `/fullscreen/results?resultCount=40&tickerSkip=6&tickerSeconds=8&ticker=off` from README renders with the ticker hidden.
- [ ] **Step 6: Commit.** `Viewer: ticker.js becomes display.js; one source for result-count limits`

### Task 10: HTML builders move to `core/markup.js`

**Files:**
- Create: `core/markup.js`, `tests/markup.test.js`
- Modify: `app.js` (remove `totalCell` … `shotGroupsCell`, `messageRow`, `tickerMarkup`; import them), `browse.js` (`groupHtml`, `rowHtml`, `message` → import), `core/format.js` (receive `formatDateTime` from `core/browse.js`), `core/browse.js` (import `formatDateTime` from `./format.js`), `tests/browse.test.js` (import path), `tests/i18n.test.js` (file list: see Task 11)

**Interfaces:**
- Produces, all pure, all return strings:

```js
export const totalCell = (program, t) => string;
export const shooterName = (program, t) => ({ label, html });
export const clubCell = (program) => string;
export const shotRing = (sector) => string;
export const shotGroupsCell = (program, t, { live = false } = {}) => string;
export const messageRow = (text, columnCount) => string;
export const tickerRun = (items, t) => string;           // the doubled-run markup, was tickerMarkup
export const browseGroupHtml = (group) => string;         // was browse.js groupHtml
export const browseRowHtml = (row) => string;             // was browse.js rowHtml
```

`t` is passed in; `core/` must not hold the language. `app.js` wraps: `const messageRowHere = (text) => messageRow(text, resultColumns());`. `browse.js` replaces `colspan="8"` with `messageRow(text, document.querySelectorAll('table.browse colgroup col').length)` so both pages follow the `<colgroup>` rule.

- [ ] **Step 1: Failing tests** in `tests/markup.test.js` (use `translate(TRANSLATIONS.de, …)` as `t`):

```js
test('a message row spans every column it is given', () => {
    assert.match(messageRow('x', 5), /colspan="5"/);
});
test('a centre hit fills the whole ring', () => {
    assert.match(shotRing(0), /shot-ring is-centre/);
});
test('a withheld total names the reason in its tooltip', () => {
    const html = totalCell({ total: null, totalUnavailable: 'mixedValuation' }, t);
    assert.match(html, /Wertung wechselt/);
});
test('the ticker run is doubled so the loop is seamless', () => {
    const html = tickerRun([{ id: 1, lane: 2, startedAt: '2026-07-08T20:45:00+02:00' }], t);
    assert.equal((html.match(/class="ticker-run"/g) ?? []).length, 2);
});
test('all markup escapes what it is given', () => {
    assert.doesNotMatch(clubCell({ shooter: { club: { name: '<b>' } } }), /<b>/);
});
```

- [ ] **Step 2: Run, expect failure.**
- [ ] **Step 3: Move the functions**, changing only how `t` arrives. `renderTicker` in `app.js` keeps the key guard and calls `tickerRun(tickerItems, t)`.
- [ ] **Step 4: Run all viewer tests, expect pass.**
- [ ] **Step 5: Manual check** of `/`, `/fullscreen/live+results` (ticker runs, dials render), `/browse` (rows, empty-filter message spans the table).
- [ ] **Step 6: Commit.** `Viewer: HTML builders in core/markup.js, tested; message rows read their colspan from the colgroup`

### Task 11: `api.js` page walker, `dom.js` today reader, i18n test globs its files

**Files:**
- Modify: `api.js` (`allPages(path, filters)`; `allPrograms`/`allShooters` become one-liners), `dom.js` (add `readToday(api, fallbackNow)`), `app.js` `syncClock`, `browse.js` `init`, `core/format.js` (add `localIsoDate(date)`), `tests/i18n.test.js`
- Test: `tests/format.test.js`, `tests/i18n.test.js`

**Interfaces:**
- `api.js`: `async allPages(path, filters = {}) { … }` returning the concatenated `items`; `allPrograms = (filters) => this.allPages('/api/v2/programs', filters)`; `allShooters = (filters = {}) => this.allPages('/api/v2/shooters', filters)`.
- `core/format.js`: `export const localIsoDate = (date) => \`${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}\`` (local date, not `toISOString`, which is UTC and shifts the day after 22:00 in Switzerland; this is a quiet behaviour fix, note it in the commit).
- `dom.js`: `export const readToday = async (api) => { try { return (await api.health()).today; } catch { return localIsoDate(new Date()); } }`. `app.js` still needs the full health for the exposure banner, so `syncClock` keeps calling `api.health()` itself and uses `localIsoDate` only in its catch; `browse.js` uses `readToday`.

- [ ] **Step 1: Failing tests.** In `format.test.js`: `localIsoDate(new Date(2026, 6, 8, 23, 30))` is `'2026-07-08'`. In `i18n.test.js`: replace the hand-kept `files` list with a glob over `*.js`, `*.html` in `wwwroot` plus `core/*.js`, excluding `tests/`:

```js
import { readdirSync } from 'node:fs';
const pageFiles = () => [
    ...readdirSync(root).filter((f) => /\.(js|html)$/.test(f)),
    ...readdirSync(join(root, 'core')).map((f) => `core/${f}`),
];
```

- [ ] **Step 2: Run, expect failure** (`localIsoDate` missing).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect pass.** The glob now also scans `boards-dialog.js` once Task 12 creates it.
- [ ] **Step 5: Commit.** `Viewer: one page walker in api.js, one today reader; i18n test discovers its files`

### Task 12: The fullscreen picker leaves `app.js`

**Files:**
- Create: `boards-dialog.js`
- Modify: `app.js`

**Interfaces:**
- Produces: `export const createBoardsDialog = ({ t, onShow }) => ({ open })`. `onShow(board)` is `app.js`'s `showFullscreen` minus the dialog close (`goTo(board.mode, displayQuery(board))` + `requestFullscreen`). The module owns `boards`, `loadBoards`, `saveBoards`, `boardName`, `boardUrl`, `numberField`, `modeOptions`, `boardSettings`, `renderBoards`, `onBoardInput`, `onBoardsClick`, `copyBoardUrl`, and attaches its own `input`/`click` listeners on `#fullscreen-modes` and the close button. `app.js` keeps `exitFullscreen` and the `fullscreenchange` listener.

- [ ] **Step 1: No new unit test** (DOM module); `boards.test.js` already covers the pure part. Before moving, run `scripts/test-web.sh` green.
- [ ] **Step 2: Move the block** `app.js:881-1019` into `boards-dialog.js`. `t` must be the live translator, so pass a function `() => t` or pass `t` and re-create the dialog on language change; simplest: `createBoardsDialog({ translate: (key, params) => t(key, params), onShow })` where `t` is `app.js`'s closure over the mutable `language`.
- [ ] **Step 3: `app.js`** `attachFullscreenHandlers`: `el('fullscreen-button').addEventListener('click', boardsDialog.open)`. Target: `app.js` under 400 lines, no divider comments left.
- [ ] **Step 4: Run tests**, then manual: open the picker, rename a board, add, remove, copy, show, Esc out of fullscreen.
- [ ] **Step 5: Update `docs/architecture.md`** viewer table (`app.js`, `boards-dialog.js`, `browse.js`, `docs.js`, `dom.js` = DOM layer; `core/markup.js`, `core/display.js` listed) and `AGENTS.md` architecture block.
- [ ] **Step 6: Commit.** `Viewer: fullscreen picker in boards-dialog.js`

---

## Phase 5: CSS

### Task 13: `styles.css` reordered and deduplicated, one file (Q4)

**Files:**
- Modify: `wwwroot/styles.css` only

**Target order** (one section each, in this order, no section appearing twice):
1. Root variables and reset (current 51-84)
2. Header, live dot, header link, header button (85-131, 493-501, 795-803)
3. Card, toolbar, inputs, buttons, icon button, input-with-button (133-198, 920-937, 991-1004)
4. Messages, exposure warning, footer, result count, `.hidden` (370-403, 438)
5. Results table and column widths (199-269, with the two `.col-total` blocks merged)
6. Shot chips and dial (270-368)
7. Sections and section head (567-587)
8. Line view (595-666)
9. Office view results box (667-673)
10. Fullscreen: **one** `body.is-fullscreen` block holding every fullscreen override (405-437, 589-593, 675-727 merged, including the `--shot-dial-size` and `--ticker-space` variables), then the `data-mode="live"` rules
11. Ticker (729-791)
12. Fullscreen picker (805-887 plus the stray 1006-1007)
13. Result browser (889-919, 939-990, 1005 with `.browse-actions` merged and the `.filter-*` widths together)
14. Docs page (440-491, 503-565; `pre.response` margin declared once; `.endpoint-body.hidden`, `.response-status.hidden`, `pre.response.hidden` dropped in favour of the global `.hidden`)

Delete the two comments describing removed mechanisms (lines 678 and 731) and replace each with one line stating the current reason: "The ticker strip height is reserved so the result box has a stable height" and "Fixed to the viewport so the table above never needs an exact row count". Section headers stay as `/* -- Name -- */` since a single stylesheet has no other navigation; every other comment follows Task 15's criterion.

- [ ] **Step 1: Take screenshots** of the six URLs with the integrated browser before touching the file (desktop width and 1920×1080 fullscreen for the three `/fullscreen/*` routes). Keep them in the scratchpad.
- [ ] **Step 2: Reorder and merge**, no declaration changes other than the merges listed. Keep every selector's specificity.
- [ ] **Step 3: Diff check.** Normalise both versions to a sorted list of `selector { declaration; }` pairs with a short script in the scratchpad; the only differences must be the merged duplicates and the three dropped `.hidden` duplicates.
- [ ] **Step 4: `scripts/run.sh`**, reload the six URLs, compare with the screenshots.
- [ ] **Step 5: Stage.** `Viewer: styles.css reordered by section; duplicate blocks merged`

---

## Phase 6: comments and docs

### Task 14: dropped

Q1 was answered *keep*: the write-token scope is planned logic and stays exactly as it is.

### Task 15: Comment pass (Q10)

**Files:** every `.cs`, `.js`, `.css`, `.html` under `src/` (not `wwwroot/tests`), `appsettings.jsonc` excluded (operator-facing by design).

Criterion: keep a comment only if it states a measured device fact, a security invariant, or a non-inferable framework/browser behaviour, and only if the name or structure cannot carry it. Where a comment explains *what* a block does, extract the block into a function named after the comment and delete the comment.

Concrete removals found in review (not exhaustive, the executor reads every file):
- `Domain/Models.cs`: all inline record-field comments except `// Clock sector 1-8 of the hit, 0 for a centre hit, null when the device reported none.` (device fact) and the `ShootingProgram` naming summary.
- `Live/LiveHub.cs`: `// Shutting down.`, `// Client vanished.`, `// Shutting down: a display that never answers…` (keep the *why* of `Abort` once, on the `Abort` method).
- `Data/SintroRepository*.cs`: the `CountingShots skips markers by ShotNr 9999 only` comment stays (device fact); `// -- … --` dividers are gone from Task 7.
- `Api/V2/V2Endpoints.cs`: `// Both return the 400 body for an unrecognised value…` → the `V2Query` class summary, once.
- `app.js`: `// Requests overlap on every live message; only the latest may render` → rename `resultsRequest` to `latestResultsRequest`, keep nothing. `// CSS cannot count rows` stays (browser fact). `// Needs a user gesture` stays.
- `core/browse.js`: `// "41, 44" → ['41', '44']` is restating the code; delete. `// Three shot columns can be combined…` → split into `shotCells`, `shotWidth`, `shotHeaders` is already done; delete the paragraph.
- `styles.css`: section dividers stay (Task 13); rule-level comments stay only when they name a browser fact (`table-layout: fixed reads the first row`) or a layout decision that is not visible from the rule (`the ticker replaces scrolling`).
- `tokens.css`: untouched (vendored).

- [ ] **Step 1: Count before.** `grep -cE '^\s*(//|/\*|\*)' <file>` per file into a scratchpad note.
- [ ] **Step 2: Edit file by file**, running `scripts/test.sh` after each directory.
- [ ] **Step 3: Count after** and put the two columns in the commit message body.
- [ ] **Step 4: Commit.** `Comments: keep device facts and invariants, drop narration`

### Task 16: Docs follow the code

**Files:**
- Modify: `AGENTS.md` (architecture block, single-sources list, security bullets; stay under 10 000 characters: `wc -c AGENTS.md`), `docs/architecture.md` (Layers, The viewer table incl. `browse.js`, `dom.js`, `core/markup.js`, `core/display.js`, styles split), `README.md` (only if Q1 removed a row)
- [ ] **Step 1: Edit**, cross-checking every file name against `git ls-files`.
- [ ] **Step 2: `scripts/test.sh`** one last time; `AppSettingsTests` proves the shipped file still binds.
- [ ] **Step 3: Commit.** `Docs: file layout after the cleanup`

---

## Self-review

- **Spec coverage:** S1 → Tasks 3, 4; S2 → 3; S3, S4 → 5; S5 → 6, 7; S6, S7 → 6; S8 → 8; S9 → kept by decision (Q1); S10 → 2; S11, S12 → 15; V1 → 1; V2, V3 → 11; V4 → 10, 12; V5, V6 → 9; V7 → 10; V8 → 10 (`formatDateTime` move), `shotGroups`/`seriesGroup` unification left out deliberately: the two produce different shapes for different consumers; V9 → 1; V10 → 11; V11 → 13; V12 → no task, by design; D1 → 12, 16.
- **Placeholders:** none; every step names its file, code or command.
- **Type consistency:** `LanesFrame` (Task 3) is what Task 4's handler builds; `QueryTokenOnUpgrade` named identically in Task 4 and `AGENTS.md`; `displayQuery`/`parseDisplayQuery`/`normaliseDisplaySettings` (Task 9) are what Task 12's `onShow` and `boards.js` import; `messageRow(text, columnCount)` (Task 10) used by both pages; `ShooterFilter` fields match the `ListShooters` query parameters `q, club, from, to, cursor, limit`.
- **Review Focus:** items 1 to 5 each have their test in Tasks 1, 4, 3, 10, 6.
