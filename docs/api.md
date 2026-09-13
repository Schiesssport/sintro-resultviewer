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
| `number`, `name` | Program number; program name contains text |
| `license` | Shooter's licence number, leading zeros optional |
| `lane` | Line number |
| `withoutResult` | `true` also returns passes with no counting shots. Default `false` |
| `order` | `desc` (default) or `asc`; see syncing |
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
      "number": 31,
      "name": "Hirssimatch Vorrunde",
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
      "total": { "value": 31, "valuation": 5 },
      "totalUnavailable": null,
      "shotCount": 10,
      "shotValues": [4, 3, 4, 3, 3, 5, 3, 4, 2, 0],
      "series": [
        {
          "index": 1,
          "valuation": 5,
          "targetCode": "A5",
          "shotCount": 4,
          "subtotal": 14,
          "bestFineValue": 74,
          "shots": [
            { "number": 1, "value": 4, "fineValue": 74, "mouche": false, "hitSector": 1,
              "x": 39, "y": 129, "at": "2026-07-08T20:46:12.55+02:00" }
          ]
        }
      ],
      "sighting": []
    }
  ],
  "nextCursor": "WyIxOTQ1IiwiREVTQyJd"
}
```

### Fields

| Field | Meaning |
|---|---|
| `shooter` | `null` for most passes: registering is optional. `lane` + `startedAt` always identify a pass. `contestShooterName` is the device's free-text name field, if the operator typed one |
| `duplicateLicense` | The device has no unique constraint on licences; `true` means another shooter carries the same number |
| `total` | Sum of all counting shots. `null` when the series used different ring scales (`totalUnavailable: "mixedValuation"`) or a scale is unknown (`"unknownValuation"`); use each series' `subtotal` then |
| `total.valuation` | Ring scale: `5`, `10`, `100`, ... |
| `shotValues` | Counting-shot ring values in firing order, sighting shots excluded |
| `series` | Counting shots grouped by stage. `targetCode` is target letter plus scale: `A10`, `B4`, `S10` (Sau). `bestFineValue` is the best tenth-value among hits |
| `shots[].fineValue` | Tenth-ring value (`74` = 7.4). `mouche`: centre hit. `hitSector`: `1` is twelve o'clock, clockwise in 45° steps, `0` centre, `null` unknown. `x`, `y`: device coordinates |
| `sighting` | Probe series, same shape as `series`, one per stage. Never counted in `total` |
| `startedAt`, `finishedAt`, `at` | ISO 8601 with the range's UTC offset. `finishedAt` is `null` until the device writes the end total |

## Paging and syncing

Pass `nextCursor` back as `cursor` until it is `null`. Never use offsets: the device inserts while
you read and prunes old rows from the other end.

To mirror results into your own database, request `order=asc`, page to the end, and store the last
`nextCursor`. Passing it again later returns exactly the passes added since. A cursor that was not
issued by this API, or was issued for the other `order`, is `400 invalid_cursor`.

## Other endpoints

| Endpoint | Returns |
|---|---|
| `GET /programs/{id}` | One pass, same shape as a list item |
| `GET /shooters?q=&club=` | Registered shooters, paged. `q` matches name or licence |
| `GET /shooters/{license}` | Every shooter on that licence plus their passes (paged with `cursor`, `limit`, `order`) |
| `GET /clubs?q=` | The Swiss club register as held by the device, paged |
| `GET /program-catalog` | Distinct `(number, name)` pairs with counts; operators rename programs freely |
| `GET /live` | Every line with the pass currently on it, `currentProgram: null` when free |
| `GET /health` | `{ databaseReachable, today, liveClients, publicExposure }`, no token needed |

### Live updates

`/api/v2/live` also upgrades to a WebSocket. The handshake cannot carry a header, so the token goes
in the URL here and only here: `ws://range-pc:8080/api/v2/live?token=<token>`. On connect you get
the current lane state, then a new frame whenever a line changes or a shot is fired:

```json
{ "type": "lanes", "lanes": [ { "number": 1, "currentProgram": { ...program... } }, ... ] }
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
| 400 | `invalid_state`, `invalid_order`, `invalid_cursor` |
| 401 | `unauthorized` |
| 403 | `forbidden_network` |
| 404 | `not_found` |
| 503 | `database_unreachable` (health only) |

An unknown filter value is rejected, never ignored, so a typo cannot silently widen what you receive.
