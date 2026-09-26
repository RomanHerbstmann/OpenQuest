# OpenQuest API (.NET 10)

ASP.NET Core Minimal API. Data model: [docs/data-model/erd.md](../../docs/data-model/erd.md). Decision: [ADR-0003](../../docs/adr/0003-backend-dotnet.md).
The machine-readable contract is the OpenAPI spec at **`/openapi/v1.json`** (running API).

## Run

From source:

```bash
docker compose up -d db minio                         # PostGIS (5432) + MinIO (9000, console 9001)
dotnet run --project apps/api/OpenQuest.Api           # http://localhost:5076, applies migrations, seeds catalog + admin
```

Or everything in Docker (PostGIS, MinIO, this API as a container from `apps/api/Dockerfile`, and the importer):

```bash
docker compose up -d --build                          # API on http://localhost:5076
docker compose logs api | grep admin                  # generated admin password (first start only)
```

Don't combine the two: the `api` container already uses port 5076. To switch to `dotnet run`, stop it with `docker compose stop api`.

`appsettings.Development.json` (committed) only holds non-secret values that match `docker-compose.yml`. In the container, `docker-compose.yml` points the connection string and MinIO to the service names (`db`, `minio`). Secrets are never in git:

- **JWT key:** generated randomly on every start in Development (tokens die on restart). Elsewhere `Jwt__Key` is required.
- **Admin:** created on first start. Without `Admin__Password` a password is generated and **printed once in the log**
  (`Created admin 'admin' with generated password: ...`). To choose your own: `Admin__Password=... dotnet run ...`, or
  `dotnet user-secrets set Admin:Password "..." --project apps/api/OpenQuest.Api`. Outside Development `Admin__Password` is required.
- Everything else: see [`.env.example`](../../.env.example).

The API does not import data. Assets come from the separate importer ([apps/importer](../importer/README.md)), which writes the open data tables; the API only reads them. The schema still belongs to the API: the importer waits until the API has applied its migrations and seeded the asset types.

## Tests

```bash
dotnet test                                           # unit tests + API integration tests
```

API integration tests need PostGIS. By default they start a Testcontainers container. To reuse a running server instead
(for example the compose one), set `OPENQUEST_TEST_DB="Host=localhost;Port=5432;Database=postgres;Username=openquest;Password=openquest"`;
each run then creates and drops its own database.

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
| Progress | `GET /me` | Besides id, username and role: `totalPoints`, `level` (`level`, `current`, `required`, `percent`, `isMaxLevel`) and `cardCount` |
| Cards | `GET /me/cards?offset&limit`, `GET /me/collection` | The player's tree cards (newest first) and the tree book, see "Tree cards" below |
| Points | `GET /me/points?offset&limit` | The player's ledger, newest first: `amount`, `reason` (`quest_approved`), `submissionId`, `questTitle`, `createdAt` |

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
| `admin` | `POST /admin/quests` (creates a campaign with one quest per selected asset), `GET /admin/quests`, `POST /admin/quests/{id}/status`, `GET /admin/campaigns`, `GET /admin/sync/runs`, `GET /admin/assets/{id}/history`, `GET /admin/reports`, `GET /admin/readings`, `GET /admin/publications`, `POST /admin/publications/retry`, `GET /admin/outbox` |

Create quests for trees without a known genus inside a map rectangle:

```json
POST /admin/quests
{ "taskType": "verify_attribute", "taskConfig": { "attribute": "genus" },
  "maxCompletions": 2, "rewardPoints": 20,
  "target": { "attributeFilter": { "genus": null },
              "bbox": { "minLon": 7.60, "minLat": 51.95, "maxLon": 7.64, "maxLat": 51.97 }, "limit": 100 } }
```

`target` accepts `assetIds`, `bbox`, `attributeFilter` (JSONB containment on asset attributes, for example `{"quality_flags":["placeholder_genus"]}`), `withoutApprovedPhoto` and `withOpenReport` (assets with an open report from an external feed: a category such as `tree_damage` / `oak_processionary_moth`, or `any`). `assetType` selects `tree` (default) or `natural_monument`.
An asset never gets the same quest twice.

## Phone app (CORS and photos)

The phone app (Android/iOS, a Capacitor shell around the web frontend) runs its web view under `https://localhost` (Android) and `capacitor://localhost` (iOS).
Both are in `Cors:Origins` of the Development settings and in `.env.example`; a deployment has to list them in `Cors__Origins` as well.
Photos come straight from the phone camera: `POST /claims/{id}/submit` takes a JPEG up to `Storage:MaxPhotoBytes` (10 MB, a 2048 px JPEG is about 1 to 2 MB);
EXIF is stripped and the orientation applied on the server, so the app does not have to. To reach the API from a phone on the same network, start it with
`--urls http://0.0.0.0:5076` and use the computer's address as API URL in the app.

## Tree cards

Every approved submission can give a **tree card**: the genus of the tree (for a genus quest the genus the player found out), with a rarity of `common`, `uncommon`, `rare`
or `legendary` ([ADR-0009](../../docs/adr/0009-tree-cards-and-rarity.md)). How rare it turns out to be depends on how frequent that genus is among the trees of the
tree's **district**: an abundant genus (the lime trees of Münster) gives mostly common cards, a scarce one (a Ginkgo among a thousand Tilia) has much better chances of
a rare one, and a genus that does not grow in the district at all the best. The dice roll comes from the submission's id, so a redelivered event gives the same card.
No genus known and none found out: no card.

Chances go up further when the submission brings something new: `new_information` (the data had no genus or another one) and `condition_fact` (damaged, dead or gone
was reported). Each moves the genus one class towards scarce. The reasons are on the card (`reasons`: `scarce_in_district`, `new_to_district`, `new_information`,
`condition_fact`), so the app can say why a card is special.

| Who | Call | Notes |
|---|---|---|
| player | `GET /me/cards?offset&limit` | Cards, newest first: `genus`, `rarity`, `frequency` (`abundant`, `common`, `scarce`, `very_scarce`), `share` of the genus in the district, `reasons`, district, tree position, `obtainedAt` |
| player | `GET /me/collection` | The tree book: `totalCards`, `distinctGenera`, `byRarity` (all four), `genera[]` with `count`, `bestRarity`, `firstObtainedAt` (best first) |
| player | `GET /districts/{id}/genera?limit` | Which genera grow in a district: `treeCount`, `share`, `frequency`, most frequent first, plus `knownGenusTrees` and `calculatedAt` |
| admin | `POST /admin/districts/{id}/genera/refresh` | Recalculate the genus statistics now |

The genus statistics are counted from the assets inside the district's outline (about 75 ms per district for 43,000 trees). They are recalculated when missing, after the district
was redrawn or changed, or when older than `Gamification:GenusStatsMaxAgeHours` (24), because the importer changes the assets on its own. Districts with fewer than 30 trees of
known genus (`MinSample`) do not have reliable statistics: nothing counts as scarce there. The thresholds and chances can be tuned per deployment (`Gamification:Rarity`,
see `.env.example`); the defaults are in `RarityProfile.Default` in the core.

## Recurring quests (weekly, for stale data)

Players are sent to the assets nobody has looked at for a long time, on a rhythm ([ADR-0010](../../docs/adr/0010-recurring-quests-and-sync-events.md)). Two parts:

**1. Selecting stale assets.** `asset_activity` records when a player last had an asset approved (`last_verified_at`, `verification_count`; no row = never). Quest targets
(`POST /admin/quests`, and the schedules below) can use it, and places:

| `target` field | Selects |
|---|---|
| `notVerifiedForDays: 365` | assets nobody verified for a year (or ever); with a `limit` the never-verified first, then the longest unchecked |
| `districtId` | assets inside this district's outline |
| `cityId` | assets inside any active district of the city |

They combine with the other selectors (`attributeFilter`, `limit`, ...). A quest whose `endsAt` has passed no longer blocks a new quest of the same kind for that asset.

**2. Weekly schedules.** A schedule is a template; every week on its weekday and time (**in the city's time zone**, week = Monday to Sunday) the API creates the quests, open for
`durationHours`. `rewardPoints` is the bonus that makes the quest worth more than a normal one. A run happens once per schedule and week (restarts and several instances are safe);
if the API was down at the time it catches up later in the same week as long as the window is still open.

| Call | Notes |
|---|---|
| `POST /admin/quest-schedules` | Create. Body below. Checked by creating the quests once and rolling back (400/422 with the reasons) |
| `GET /admin/quest-schedules`, `GET .../{id}` | With `nextRunAt` (null while paused) and `lastRun` |
| `PUT /admin/quest-schedules/{id}` | Change the given fields; `isEnabled: false` pauses it |
| `DELETE /admin/quest-schedules/{id}` | Deletes the schedule and its run history; created quests stay |
| `GET /admin/quest-schedules/{id}/runs` | Runs, newest first: `periodKey` (`2026-W39`), `questsCreated`, `campaignId`, `error` |
| `POST /admin/quest-schedules/{id}/preview` | `{ "wouldCreate": n }` right now, nothing saved |
| `POST /admin/quest-schedules/{id}/run` | Create the quests now, additionally to the weekly runs (also when paused) |

```json
{
  "name": "Sunday check", "cityId": "…", "weekday": "sunday", "time": "08:00", "durationHours": 24,
  "taskType": "photo", "title": "Sunday check: when did you last see this tree?", "maxCompletions": 1, "rewardPoints": 30,
  "target": { "notVerifiedForDays": 365, "limit": 50 }
}
```

Without `districtId` and `cityId` in the target it applies to the schedule's city (its districts). A schedule only finds assets inside districts, so draw them first. Set
`Gamification:QuestScheduleIntervalSeconds` (60) to change how often the worker checks the clock.

**Sync events.** After every successful run the importer sends `pg_notify('sync_finished', run_id)`; the API listens and puts an `AssetSyncCompleted` event into the outbox
(also for runs it missed while it was down, the last two days). Register an `IEventHandler<AssetSyncCompleted>` to react; the built-in one marks the district genus
statistics as out of date.

## Admin panel

The API serves a small admin panel at **`/panel/`** (static files in `apps/api/OpenQuest.Api/wwwroot/panel`, no build step, plain JavaScript modules,
Leaflet is vendored; only the map tiles come from OpenStreetMap). Log in with an `admin` (or `moderator`) account. It uses the REST endpoints below, so it
doubles as a reference for the frontend team.

- **Cities & districts** (admin): pick or create a city, **draw districts on the map** (click the corners in order, drag to move, right-click to delete a point;
  the numbers show the order), live check while drawing (crossing edges are marked, overlaps with other districts are highlighted, saving is blocked until the
  shape is valid), edit, deactivate, delete, **import GeoJSON**, and see the district leaderboard (all time / this week).
- **Moderation** (moderator, admin): the review queue with photo, answer and distance; approve (pays the points) or reject with a reason.

The panel sends a Content-Security-Policy (scripts only from itself); the token is kept in `sessionStorage` and is gone when the tab closes.

## Cities and districts (drawn in the admin panel)

Admins split a city into districts ("Stadtviertel") in the admin panel; players see them on the map and compete in a district leaderboard
([ADR-0007](../../docs/adr/0007-cities-and-districts-drawn-by-admins.md)). A city has any number of districts, a district belongs to one city.
The outline is an **ordered ring of points**: the points are connected in the order they were drawn, the last one back to the first. Edges must not
cross, and districts of one city must not overlap (sharing a border is fine).

All shapes are **GeoJSON** (`Polygon`, or a `Feature` holding one): coordinates are `[lon, lat]`, the ring may be open or closed, the order is kept,
holes and `MultiPolygon` are not supported. The API stores the corners as ordered `district_point` rows and derives the area (`geom`) and a centre for labels.

| Who | Call | Notes |
|---|---|---|
| admin | `POST /admin/cities` `{name, key?, countryCode?, centerLat?, centerLon?, defaultZoom?, timezone?}` | `key` defaults to a slug of the name, `timezone` to `Europe/Berlin` (decides where a leaderboard week starts). `PUT /admin/cities/{id}`, `DELETE /admin/cities/{id}` (409 while it has districts), `GET /admin/cities` |
| admin | `POST /admin/cities/{id}/districts/validate` `{geometry, districtId?}` | **Dry run for the drawing tool**: returns `{valid, pointCount, centroidLat, centroidLon, problems[]}` and saves nothing. Pass `districtId` when redrawing an existing district |
| admin | `POST /admin/cities/{id}/districts` `{name, description?, color?, key?, geometry}` | 201 with the district. `color` is `#RRGGBB`. Problems: 422 `invalid_geometry` with `details.problems[]`, 409 `name_taken` / `key_taken` |
| admin | `PUT /admin/districts/{id}` `{name?, description?, color?, isActive?}` | Deactivate instead of deleting to keep the leaderboard history. Re-activating checks for overlaps again |
| admin | `PUT /admin/districts/{id}/geometry` `{geometry}` | Replaces the whole outline. Points already awarded keep the district they were earned in |
| admin | `DELETE /admin/districts/{id}` | Only if no points were earned there (409 `district_has_points`) |
| admin | `POST /admin/cities/{id}/districts/import` `{geoJson, nameProperty, keyProperty?, descriptionProperty?}` | Creates one district per feature of a `FeatureCollection`. All or nothing: on problems 422 `invalid_import` with `details.features[]` (index, name, problems) |
| admin | `GET /admin/cities/{id}/districts?geometry=true`, `GET /admin/districts/{id}` | Include deactivated districts |
| player | `GET /cities`, `GET /cities/{idOrKey}` | Cities with map centre, zoom, time zone, number of districts |
| player | `GET /cities/{id}/districts?geometry=true` | Active districts with name, description, colour, centre, points; with `geometry=true` also the outline as GeoJSON |
| player | `GET /districts/{id}` | One district plus `rank` in its city and `contributors` |
| player | `GET /districts/lookup?lat&lon` | The district a position lies in (404 `no_district` if none) |
| player | `GET /cities/{id}/leaderboard?period=all\|week` | Ranking of the districts: `rank`, `points`, `contributors`, `contributions`. `week` = Monday 00:00 to Sunday in the city's time zone. Equal points and contributors share a rank |
| player | `GET /districts/{id}/leaderboard?period=&limit=` | The players with the most points in the district |

Reading needs a login, writing needs the `admin` role. Points are awarded to the district the quest's **tree lies in** (at the moment of approval);
trees outside every district count for the player only.

Draw a district (what a Leaflet drawing tool sends and receives, `[lon, lat]`):

```json
POST /admin/cities/{cityId}/districts
{ "name": "Kreuzviertel", "description": "Studentisch, viele Linden", "color": "#2C8054",
  "geometry": { "type": "Polygon",
                "coordinates": [[[7.6100, 51.9600], [7.6250, 51.9600], [7.6250, 51.9700], [7.6100, 51.9700], [7.6100, 51.9600]]] } }
```

Problems come as a list, the same for the dry run and for saving. Edge indices count the points of the ring; `lat`/`lon` is where two edges cross:

```json
{ "error": "invalid_geometry", "details": { "problems": [
  { "code": "self_intersection", "message": "Edge 0 and edge 2 cross or touch each other.", "edgeA": 0, "edgeB": 2, "lat": 51.965, "lon": 7.617 },
  { "code": "overlaps_district", "message": "The outline overlaps the district 'Dom'.", "districtId": "…", "districtName": "Dom", "overlapRatio": 0.42 } ] } }
```

Problem codes: `self_intersection`, `overlaps_district`, `too_few_points`, `too_many_points` (max 5000), `duplicate_point`, `zero_area`, `invalid_coordinate`,
`invalid_geometry`, `geometry_required`, `unsupported_geometry` (not a Polygon), `holes_not_supported`.

## Import, snapshots and history

The API does not import anything; a separate importer loads the city's data set and records every run in `sync_run` and every change of an asset in `asset_snapshot`. The API shows the result read-only:

- `GET /admin/sync/runs`: the latest runs with status, counters and error;
- `GET /admin/reports?status=open&category=tree_damage`: reports from external feeds (e.g. the city's "Mängelmelder"), with the linked asset;
- `GET /admin/readings?metric=soil_moisture_grass_sand_0_60cm&days=14`: environment readings such as daily soil moisture;
- `GET /admin/assets/{id}/history`: the versions of an asset (`created`, `updated`, `removed`).

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

Gamification beyond points, levels, the district leaderboard, cards and weekly quests (new-tree reports, badges), statistics, `media.captured_at` (EXIF time is dropped, not stored),
account deletion (`user.deleted_at` is honored on login but there is no endpoint), street name enrichment, admin-created moderators (set `user.role` in the database for now).
