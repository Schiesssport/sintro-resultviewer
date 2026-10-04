# Reading results from another program

The API is read-only JSON under `http://<range-pc>:8080/api/v2`. This page is what a consumer
needs; the complete, generated reference is at `/docs` on the running service (or raw at
`/openapi/v2.json`).

## Access

1. The operator adds a token for you in `appsettings.jsonc` under `Sintro:ApiReadTokens`, or as
   the environment variable `Sintro__ApiReadTokens__0=<token>`. 16 characters minimum, 128
   recommended.
2. Your machine must sit in one of the `Sintro:Network:Api` ranges. Private LAN ranges are
   allowed by default; anything else is a `403 forbidden_network`.
3. Send the token on every request:

```
GET /api/v2/programs
Authorization: Bearer <token>
```

Only `/api/v2/health` is open without a token.

## The results list: `GET /api/v2/programs`

One item is one *program*: one shooter's pass at the target, a "Passe". Newest first.

| Parameter | Meaning |
|---|---|
| `from`, `to` | Date window, `YYYY-MM-DD`, inclusive. **Default: today only.** |
| `state` | `active` (on a line now), `finished` (end total written), `abandoned` (neither) |
| `targetCode` | Program number on the device, one or a comma-separated list: `targetCode=41,44` |
| `targetTitle` | Program name contains text |
| `matchCode` | Passes with a shot carrying one of these match codes, comma-separated like `targetCode` |
| `license` | Shooter's licence number, leading zeros optional; comma-separated for several shooters |
| `lane` | Line number |
| `withoutResult` | `true` also returns passes with no counting shots. Default `false` |
| `order` | `desc` (default) or `asc`. With `state=finished` the list is in finishing order, otherwise in starting order |
| `cursor`, `limit` | Paging; `limit` defaults to 200, max 2000 |

```
curl -H "Authorization: Bearer $TOKEN" \
  "http://range-pc:8080/api/v2/programs?from=2026-07-08&to=2026-07-08&state=finished"
```

```json
{
  "items": [
    {
      "id": 2000,
      "targetCode": 31,
      "targetTitle": "Hirssimatch Vorrunde",
      "lane": 6,
      "startedAt": "2026-07-08T20:45:54+02:00",
      "finishedAt": "2026-07-08T20:46:51.89+02:00",
      "state": "finished",
      "shooter": {
        "license": "012345",
        "firstName": "Hans",
        "lastName": "Muster",
        "shooterId": 66,
        "club": { "id": 102104167, "number": "1.02.1.04.167", "name": "Feldschützen Musterdorf" },
        "duplicateLicense": false
      },
      "contestShooterName": null,
      "totals": [
        { "targetType": "A5", "valuation": 5, "value": 31,
          "fineValues": [74, 61, 78, 55, 59, 93, 60, 72, 44, 0] }
      ],
      "series": [
        {
          "targetType": "A5",
          "valuation": 5,
          "subtotal": 14,
          "bestFineValue": 74,
          "fineValues": [74, 61, 78, 55],
          "shots": [
            { "number": 1, "matchCode": 12, "value": 4, "fineValue": 74, "innerTen": false, "hitSector": 1,
              "x": 39, "y": 129, "at": "2026-07-08T20:46:12.55+02:00" }
          ]
        }
      ],
      "sighting": []
    }
  ],
  "nextCursor": "WyIxOTQ1IiwiREVTQyIsIlByb2dyYW1JRCJd",
  "hasMore": true
}
```

### Fields

| Field | Meaning |
|---|---|
| `shooter` | `null` when nobody logged in at the line; registering is optional. `lane` + `startedAt` always identify a pass. `contestShooterName` is the device's free-text name field, if the operator typed one |
| `duplicateLicense` | The device has no unique constraint on licences; `true` means another shooter carries the same number |
| `totals` | One entry per target and ring scale, in the order first shot: `value` is the sum of the counting shots on that scale, `fineValues` their fine values in firing order. A pass shot on one scale has exactly one entry; a pass that changed scale has several, never one number across scales. `valuation: null` means the device recorded no scale for those series |
| `totals[].valuation` | Ring scale: `4`, `5`, `10`, `100` |
| `targetCode`, `targetTitle` | The program number and name as the operator set them on the device. Free text, not a key: operators rename programs |
| `series` | Counting shots grouped by stage, in firing order; so are `shots` inside a series and `sighting`. The position in the array is the only ordinal: the third series is `series[2]`, its first three shots `series[2].shots[0:3]`. `targetType` is target letter plus scale: `A10`, `B4`, `S10` (Sau). `fineValues` are the shots' fine values in firing order; `bestFineValue` the best among hits (misses excluded), the usual tie-breaker |
| `shots[].number` | The device's shot count: counting shots count from 1 across the whole pass, sighting shots have their own count. It matches the device display, it is not a position inside a series |
| `shots[].matchCode` | The event match (Stich) the operator entered for the pass, stored by the device on every shot. `null` outside events. It is independent of `targetCode`: one program is shot under several match codes |
| `shots[].fineValue` | Tenth-ring value (`74` = 7.4); the ring `value` follows from it, not the reverse, so the compact lists carry fine values. The usual tie-breaker, the best fine value of a series, is `series[].bestFineValue`. `innerTen`: the device's centre-hit flag (Mouche); such a hit also has `hitSector` `0`. `hitSector`: `1` is twelve o'clock, clockwise in 45° steps, `0` no direction (an inner ten, or a shot the device could not place), `null` unknown. `x`, `y`: hit coordinates in mm from the centre, `y` up |
| `sighting` | Probe series, same shape as `series`, one per stage. Never counted in `totals` |
| `startedAt`, `finishedAt`, `at` | ISO 8601 with the range's UTC offset. `finishedAt` is `null` until the device writes the end total |

## Paging and syncing

Pass `nextCursor` back as `cursor` while `hasMore` is `true`. Never use offsets: the device inserts
while you read and prunes old rows from the other end. `nextCursor` is always present on a non-empty
page, so the last page still gives you a position to resume from.

To keep your own database up to date with the results of an event:

1. Let the operator enter the event's shooting days, and always send them as `from` and `to`.
   The range is also used for training and tests; without the window those passes would be
   imported as results.
2. `GET /programs?state=finished&order=asc&from=<first day>&to=<last day>` and page until
   `hasMore` is `false`.
3. Store the last `nextCursor`.
4. Later, call again with the same `from`, `to` and `cursor=<stored>`. You receive exactly the
   passes inside the window that finished since, whenever they were started, and no others. Store
   the new `nextCursor` and repeat. An empty page has `nextCursor: null`; keep the cursor you have.

Two details make this safe: with `state=finished` the list is in **finishing** order, so a pass that
ran long and ended after your last sync still comes after your cursor; and the window is on the
**start** date, so a pass that starts on an event day and runs past midnight is still included. A
cursor that was not issued by this API, or was issued under another `order` or `state`, is
`400 invalid_cursor`. A request with a cursor but no window returns every day; use that only when
you really want everything.

### Changing the window, refetching

`from` and `to` are independent: either may be left out for an open end, and both may be changed
between calls when the operator corrects them. A cursor is only a position in finishing order, so
after changing the window drop the cursor and walk the new window from the start. Walking the same
window twice returns the same passes in the same order, and a pass's `id` never changes, so an
import that upserts by `id` can refetch any time. Only very old days may come back shorter: the
device prunes its oldest passes.

## Other endpoints

| Endpoint | Returns |
|---|---|
| `GET /programs/{id}` | One pass, same shape as a list item |
| `GET /shooters?q=&club=&from=&to=` | Registered shooters, paged. `q` matches name or licence; `from`/`to` keeps only shooters with a pass in that window |
| `GET /shooters/{license}` | Every shooter on that licence plus their passes (paged with `cursor`, `limit`, `order`) |
| `GET /clubs?q=` | The Swiss club register as held by the device, paged |
| `GET /program-catalog` | Distinct `(targetCode, targetTitle)` pairs with `timesShot` and `lastStartedAt`; operators rename programs freely. Not paged: `{ items }` only |
| `GET /live` | `{ "lanes": [...] }`, every line with the pass currently on it, `currentProgram: null` when free |
| `GET /health` | `{ databaseReachable, today, liveClients, publicExposure }`, no token needed |

### Live updates

`/api/v2/live` also upgrades to a WebSocket. The handshake cannot carry a header, so the token goes
in the URL here and only here: `ws://range-pc:8080/api/v2/live?token=<token>`. On connect you get
the current lane state, then a new frame whenever a line changes or a shot is fired:

```json
{ "lanes": [ { "number": 1, "currentProgram": { ...program... } }, ... ] }
```

Results are not pushed. When a lane frame shows a pass as `finished`, fetch `/programs` (or that
`/programs/{id}`).

## Errors

Every non-2xx answer has the same body:

```json
{ "error": "invalid_state", "detail": "Unknown state 'finishd'. Expected one of: active, finished, abandoned." }
```

| Status | `error` |
|---|---|
| 400 | `invalid_state`, `invalid_order`, `invalid_filter`, `invalid_cursor` |
| 401 | `unauthorized` |
| 403 | `forbidden_network` |
| 404 | `not_found` |
| 503 | `database_unreachable` (health only) |

An unknown filter value is rejected, never ignored, so a typo cannot silently widen what you receive.
