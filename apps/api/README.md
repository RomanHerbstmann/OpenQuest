# OpenQuest API (.NET 10)

ASP.NET Core Minimal API. Data model: [docs/data-model/erd.md](../../docs/data-model/erd.md). Decision: [ADR-0003](../../docs/adr/0003-backend-dotnet.md).
The machine-readable contract is the OpenAPI spec at **`/openapi/v1.json`** (running API).

## Run

```bash
docker compose up -d                                  # PostGIS (5432) + MinIO (9000, console 9001)
dotnet run --project apps/api/OpenQuest.Api           # http://localhost:5076, applies migrations, seeds catalog + admin
```

`appsettings.Development.json` (committed) only holds non-secret values that match `docker-compose.yml`. Secrets are never in git:

- **JWT key:** generated randomly on every start in Development (tokens die on restart). Elsewhere `Jwt__Key` is required.
- **Admin:** created on first start. Without `Admin__Password` a password is generated and **printed once in the log**
  (`Created admin 'admin' with generated password: ...`). To choose your own: `Admin__Password=... dotnet run ...`, or
  `dotnet user-secrets set Admin:Password "..." --project apps/api/OpenQuest.Api`. Outside Development `Admin__Password` is required.
- Everything else: see [`.env.example`](../../.env.example).

Load the trees of Münster (about 43,000, takes ~10 s, idempotent):

```bash
TOKEN=$(curl -s localhost:5076/auth/login -H 'content-type: application/json' \
  -d '{"username":"admin","password":"<your admin password>"}' | jq -r .token)
curl -X POST localhost:5076/admin/sync -H "authorization: Bearer $TOKEN"     # then GET /admin/sync/status
```

## Tests

```bash
dotnet test                                           # unit tests + API integration tests
```

API integration tests need PostGIS. By default they start a Testcontainers container. To reuse a running server instead
(for example the compose one), set `OPENQUEST_TEST_DB="Host=localhost;Port=5432;Database=postgres;Username=openquest;Password=openquest"`;
each run then creates and drops its own database.
`OPENQUEST_FULL_DATASET=/path/to/wfs-dump.json` additionally checks the Münster parser against the full real dataset.

## Conventions for clients

- JSON is camelCase, enum values are `snake_case` strings (`verify_attribute`, `removed_at_source`). Coordinates are WGS84 (`lat`, `lon`).
- Auth: `Authorization: Bearer <token>` from `/auth/login` or `/auth/register`. Roles: `player`, `moderator`, `admin`.
- Errors: `{ "error": "<code>", "message": "...", "details": ... }` with a matching HTTP status; validation errors use RFC 7807 problem details.

## Player flow

| Step | Call | Notes |
|---|---|---|
| Sign up | `POST /auth/register` `{username, password}` | No e-mail. Response contains `recoveryCodes` (8): **shown once**, tell the player to save them |
| Log in | `POST /auth/login` | |
| Forgot password | `POST /auth/recover` `{username, recoveryCode, newPassword}` | Each code works once. `POST /me/recovery-codes` `{password}` creates a fresh set |
| Map | `GET /quests/nearby?lat&lon&radius` | Nearest first; full quests and quests you already hold are hidden. `GET /assets/nearby` shows all objects |
| Accept | `POST /quests/{id}/claim` | 409 `no_free_slots` / `already_claimed` / `quest_unavailable`. Claim expires after `claimTtlMinutes` (default 30). `POST /claims/{id}/cancel` |
| Complete | `POST /claims/{id}/submit` (multipart) | fields `lat`, `lon`, `payload` (JSON text), `photo` (optional file) |
| Status | `GET /me/claims` | Includes submission status and the moderator's rejection reason |

`payload` per task type (JSON Schema is in `task_type.result_schema`):

| Task type | payload | Photo |
|---|---|---|
| `photo` | `{}` | required |
| `verify_attribute` | `{"value":"Tilia"}` (optional `confirmed`, `note`) | optional |
| `measure` | `{"value":123.5}` (positive number) | optional |
| `condition_report` | `{"condition":"good\|damaged\|dead\|gone"}` | optional |

Submit errors: 422 `outside_geofence` (with `distanceMeters`), 422 `invalid_submission` (with `details.problems`), 422 `invalid_photo`,
409 `duplicate_photo`, 409 `claim_expired`, 409 `already_submitted`, 413 `photo_too_large`. The quest's task config (for example `{"attribute":"genus"}`) is part of every quest returned to the client.
Approved photos are public at `GET /media/{id}` (only after approval, never before).

## Admin and moderation

| Role | Calls |
|---|---|
| `moderator`, `admin` | `GET /admin/submissions?status=pending`, `GET /admin/media/{id}`, `POST /admin/submissions/{id}/review` `{approved, reason}` (reason required to reject; rejecting frees the slot) |
| `admin` | `POST /admin/quests` (creates a campaign with one quest per selected asset), `GET /admin/quests`, `POST /admin/quests/{id}/status`, `GET /admin/campaigns`, `POST /admin/sync`, `GET /admin/sync/status`, `GET /admin/sync/runs/{id}/snapshot`, `GET /admin/assets/{id}/history`, `GET /admin/publications`, `POST /admin/publications/retry`, `GET /admin/outbox` |

Create quests for trees without a known genus inside a map rectangle:

```json
POST /admin/quests
{ "taskType": "verify_attribute", "taskConfig": { "attribute": "genus" },
  "maxCompletions": 2, "rewardPoints": 20,
  "target": { "attributeFilter": { "genus": null },
              "bbox": { "minLon": 7.60, "minLat": 51.95, "maxLon": 7.64, "maxLat": 51.97 }, "limit": 100 } }
```

`target` accepts `assetIds`, `bbox`, `attributeFilter` (JSONB containment on asset attributes, for example `{"quality_flags":["placeholder_genus"]}`) and `withoutApprovedPhoto`.
An asset never gets the same quest twice.

## Import, snapshots and history

`POST /admin/sync` (also daily, `Adapters:SyncIntervalHours`) downloads the city's data set and updates our assets. Every run is a snapshot:

- the download is stored unchanged (`GET /admin/sync/runs/{id}/snapshot`), so any snapshot can be reloaded exactly as it was;
- `asset_snapshot` records only what happened to an asset (`created`, `updated`, `removed`); an unchanged import adds no rows. `GET /admin/assets/{id}/history` shows an asset's versions;
- the run fails loudly if the source's field list changed (`schema_hash`), the CRS is unexpected, required fields are missing, or the source suddenly delivers less than half of the known assets. Continue after checking the adapter with `POST /admin/sync?acceptSchemaChange=true`.

## Data flow back to the city (event-driven)

Player results never overwrite asset data. A submission proposes an `attribute_change`; **the moment a moderator approves it,
the change is pushed to open data** ([ADR-0004](../../docs/adr/0004-event-driven-writeback.md)); nobody has to export anything:

```
approve  ->  transaction: change = accepted + outbox event (same commit)  ->  NOTIFY  ->  handler  ->  publishers
                                                                                     |-> public feed  /open-data/{dataSource}/changes.geojson | .csv   (always)
                                                                                     '-> GitHub repository file                                     (if configured)
```

- The feed URL is stable, public, carries the required attribution, and is meant to be linked by the city (see ADR-0001).
- Delivery is retried with backoff if a channel fails; `GET /admin/outbox` shows pending / dead messages,
  `POST /admin/publications/retry` re-emits events for accepted-but-unpublished changes.
- Rejected submissions publish nothing. Other parts of the system can react to `SubmissionApproved` / `SubmissionRejected` (rewards later) by registering a handler.

## Code structure

`Composition/ServiceRegistration.cs` is the only place that names implementations; everything else depends on small interfaces
(`IQuestClaimService`, `ISubmissionService`, `IContributionPublisher`, `IBlobWriter`, ...). Extend by adding a handler
(`IEventHandler<T>`), a publisher (`IContributionPublisher`), or a startup task (`IStartupTask`).

## Not built yet

Gamification (points ledger, badges, leaderboards; hook: handle `SubmissionApproved`), statistics, `media.captured_at` (EXIF time is dropped, not stored),
account deletion (`user.deleted_at` is honored on login but there is no endpoint), street name enrichment, admin-created moderators (set `user.role` in the database for now).
