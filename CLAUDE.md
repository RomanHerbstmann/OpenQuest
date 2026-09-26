# CLAUDE.md

Guidance for Claude Code (and human contributors) working in this repository.

## Project overview

**OpenQuest** is an open-source web app that turns updating municipal open data into a location-based game (think *Pokémon Go* for city data). Players walk around their city, pick up quests on a map (e.g. "photograph this tree", "confirm the species", "report the tree's condition") and complete them on site. The collected contributions flow back into the city's open data.

- Started at **MS-Hack** (Münster hackathon).
- First city: **Münster**, using the [Open Data Platform Münster](https://opendata.stadt-muenster.de).
- First data domain: **tree inventory (Baumbestand / Baumkataster)**.
- Developed openly on GitHub: https://github.com/RomanHerbstmann/OpenQuest
- Goal beyond the hackathon: any city can plug in its own data source by writing an **adapter**, and host its own instance.

## Core concepts (domain language)

Use these terms consistently in code, UI and docs.

| Term | Meaning |
|---|---|
| **Asset** | A real-world object from a city's open data (a tree, later maybe a bench, playground, bike rack…). Has an id, a geo position and domain attributes. |
| **Asset type** | The kind of asset (`tree`, …). Defines its attribute schema and which task types make sense for it. |
| **Data source adapter** | Pluggable importer module that reads one data set of one city and normalizes it to assets. The Münster tree adapter is just one implementation. |
| **Enricher** | Pluggable importer module that adds attributes keyed by location from another provider (e.g. tree height from the NRW surface model). Not tied to a city; any data source in its area can switch it on. |
| **Sync run / snapshot** | One import of a data source. The raw download is stored unchanged; `asset_snapshot` records what happened to each asset (created / updated / removed). |
| **Quest** | A task created by an admin, bound to one or more assets, e.g. "Take a photo of tree #4711". Has a **completion limit** (`maxCompletions`). |
| **Task type** | What the player has to do: `photo`, `verify_attribute` (e.g. species), `measure` (e.g. trunk circumference), `condition_report`, … |
| **Claim** | A player accepting a quest. Claims reserve a slot and **expire** after a timeout if not completed, so slots are released again. |
| **Submission / Contribution** | The result a player uploads (photo, values, GPS position, timestamp). |
| **Review** | Admin/moderator (later possibly community voting) approves or rejects a submission. Only approved submissions count and get exported. |
| **Export** | Turning approved contributions into something the city can ingest (dataset, CSV/GeoJSON, API call). |

### Key rule: limited completions

A quest may only be completed **X times** (`maxCompletions`). This prevents 100 players photographing the same tree.
- `activeClaims + approvedOrPendingSubmissions < maxCompletions` must hold before a new claim is granted.
- Claim creation must be **atomic / race-safe** (DB transaction or row lock, not a read-then-write in application code).
- Expired claims free their slot. Rejected submissions free their slot.
- Once the limit is reached the quest disappears from the player map.

## Modularity: the adapter architecture

The single most important architectural rule: **nothing outside an adapter package may know anything specific to Münster (or any other city).** No Münster URLs, dataset ids, field names or coordinate systems in core, API or frontend code.

```
City open data ──► Importer (Python): adapter + enrichers ──► PostGIS ──► API (.NET) ──► Web app / Admin panel
                                                                           │
City ◄──────────── write-back: domain events → published changes ◄────────┘  (approved contributions)
```

- **Import** is done only by the importer (`apps/importer`, [README](apps/importer/README.md)), **not by the .NET API**; the API only reads assets, data sources and sync runs. Assets are synced into our own database on a schedule, so the city's platform is not a runtime dependency of the game.
- **Write-back** is done by the API and is **event-driven**: accepting a contribution publishes a domain event (transactional outbox); handlers push the change to open data right away, never on a timer ([ADR-0004](docs/adr/0004-event-driven-writeback.md)).

The importer's contract (the Python code is the source of truth):

- **Adapter** (`openquest_importer/adapters/base.py`, entry point group `openquest.adapters`): declares `asset_type`, `identity` (`EXTERNAL_ID` if the source has stable ids, else `SPATIAL`) and `expected_fields`; implements `fetch()` (download the complete data set, reference files included) and `parse()` (normalized assets: WGS84 lon/lat, attributes, untouched `raw` record, `source_hash`).
- **Enricher** (`openquest_importer/enrichers/base.py`, entry point group `openquest.enrichers`): declares the `attributes` it sets and implements `enrich(assets)`; may keep a cache between syncs.
- Both are selected in `apps/importer/importer.toml` per data source; another city can ship its adapter or enricher as a separate Python package.

Adapter rules:
- Map city-specific field names (e.g. German column names) to core attribute names inside the adapter only.
- Adapters are selected via configuration, never via `if city == "muenster"` in core code.
- Fail loudly when the source changes (field list, CRS, format) instead of importing data the adapter doesn't understand.
- Every adapter and enricher ships with fixture data and tests so it can be developed offline.

Adding a new city = add an adapter (+ config). Adding a new asset type = add it to `AssetType.Known` in `packages/core/OpenQuest.Core/Domain/AssetType.cs` (attribute schema + allowed task types), then support it in an adapter. New attributes of an existing type go into its schema there as well.

## Münster data source

Decided in [ADR-0001](docs/adr/0001-baumkataster-datenbezug-und-rueckkanal.md), evidence in [Research Note 0001](docs/research/0001-opendata-muenster-baumkataster.md). Read both before working on the Münster adapter.

- **Portal:** https://opendata.stadt-muenster.de runs **DKAN 7 on Drupal 7** (not CKAN). Its API only holds metadata, is slow and has no write access. Don't use it for data.
- **Source:** the tree data comes live from the city's **MapServer WFS** `https://geo.stadt-muenster.de/mapserv/odgruen_serv`, layer `Baeume` (the importer reads GeoJSON in WGS84). CORS is open.
- **Content:** ~43k trees with only three fields: point, `str_schl` (street key), `baumgruppe` (genus). Data as of 2017/2020, about half of the city's trees. **No stable id**: we assign our own asset id (also stored as `external_id`) and match records spatially on re-sync ([ADR-0006](docs/adr/0006-eigene-asset-id-und-raeumliches-matching.md)). ~8 % placeholders instead of a genus (e.g. `Baum Amt62`). Details: [docs/data-model/erd.md](docs/data-model/erd.md#münster-tree-data-gruen_opendatacsv).
- **More sources** (see [erd.md](docs/data-model/erd.md#other-data-sources) and the [importer README](apps/importer/README.md)): Straßen.NRW trees along federal/state roads (dl-de/zero-2.0), the city's natural monuments (WMS; **licence not stated, source disabled**), citizen reports from the "Mängelmelder" (Open311, dl-de/by-2.0) and DWD daily soil moisture (GeoNutzV). Reports and readings have their own tables (`asset_report`, `environment_reading`); quests can target assets with an open report (`target.withOpenReport`).
- **Enrichment:** the Münster adapter adds `street_name` (street directory WFS `odstrasseserv`, join over `str_schl`), `district` and `quarter` (Stadtbezirke / Stadtteile GeoJSON from the portal, point in polygon). The enricher `de_nrw.alleen` marks trees in legally protected avenues (`avenue_id`, `avenue_name`). The enricher `de_nrw.ndom_height` adds `height_m` from the **nDOM50 of Geobasis NRW** (95th percentile of the object height within 2.5 m of the tree point; not a measured tree height).
- **Snapshots:** the importer syncs on start and then daily. It stores every download unchanged (reference files and enrichment results included) and keeps the history (`SYNC_RUN`, `ASSET_SNAPSHOT`). If the WFS schema changes, the sync fails loudly.
- **License:** Münster data (trees, streets, districts, quarters) dl-de/by-2.0, attribution required; nDOM dl-de/zero-2.0 (no conditions, we still credit Geobasis NRW). Use the attribution text from ADR-0001 in the app, README and every published file. No city logos or coat of arms, nothing that looks official.
- **Write-back:** the portal cannot be written to. Approved contributions go back as a published cleaned dataset (GitHub) plus a message to the city's open data coordination (see ADR-0001).

## Data model

The ERD and the reasoning behind it live in [docs/data-model/erd.md](docs/data-model/erd.md). Keep it in sync when the schema changes. Short version: open data objects are generic `ASSET`s with an `ASSET_TYPE` and JSONB `attributes` (validated by a JSON Schema per type), so new data sets need no schema migration.

- **The schema belongs to the API:** EF Core migrations in `apps/api/OpenQuest.Api/Data/Migrations` run when the API starts, and the API seeds the asset types (with their attribute schemas) from `AssetType.cs`. The importer has no migrations and waits until the API has set up the database.
- The EF tables have no database defaults; code that writes them (API or importer) sets ids and timestamps itself.
- After adding an EF migration, run `apps/importer/scripts/update-ef-schema.sh`: the importer's database tests build their schema from `apps/importer/tests/fixtures/ef_schema.sql` and fail while it is outdated.

## Tech stack

Decided for the backend (see [ADR-0003](docs/adr/0003-backend-dotnet.md)); the frontend stack is decided by the frontend team.

- **Backend:** .NET 10, ASP.NET Core Minimal API, EF Core + Npgsql + NetTopologySuite. OpenAPI spec at `/openapi/v1.json` is the contract for clients.
- **Database:** PostgreSQL + **PostGIS** ([data model](docs/data-model/erd.md)).
- **File storage:** S3-compatible (MinIO locally) for photos and export files.
- **Auth:** username + password (argon2id), JWT, recovery codes, roles `player | moderator | admin`.
- **Importer:** Python ≥ 3.11 (`apps/importer`), psycopg 3 and jsonschema; no GIS libraries (own point-in-polygon index, UTM conversion and GeoTIFF reader).
- **Photo verification:** TypeScript packages (pnpm workspace) with vision models via OpenRouter ([ADR-0002](docs/adr/0002-tree-photo-verification.md)).
- **Local dev:** Docker Compose runs PostGIS, MinIO, the API and the importer. The whole stack must stay startable with `docker compose up`; add new services there.
- **Web app / admin panel:** mobile-first PWA with MapLibre GL + OpenStreetMap tiles (proposal, owned by the frontend team; not on `main` yet).

Layout:

```
apps/
  api/OpenQuest.Api/                                # C#: ASP.NET Core API, persistence (EF Core), auth, background jobs
  api/Dockerfile
  importer/                                         # Python: adapters, enrichers, sync, CLI, Dockerfile, tests
packages/
  core/OpenQuest.Core/                              # C#: domain model, asset types, quest/claim/geofence rules, exporters (framework-free)
  adapters/de-muenster/src, test/                   # TypeScript: nearby trees from the WFS for photo verification
  tree-verification/                                # TypeScript: photo verification pipeline
tests/          # C#: unit tests (core) and API integration tests
eval/           # TypeScript: evaluation of the photo verification
docs/           # ADRs, data model, research notes
```

## Game & product requirements

Player (web app):
- Map with nearby quests (and optionally all assets); player position via browser geolocation.
- Accept (claim) a quest → navigate there → complete it on site.
- **Geofence check:** completion only allowed within a configurable radius of the asset (e.g. 30 m); store the reported GPS position with the submission.
- Photos taken via the camera in the app (`capture`), not arbitrary gallery uploads, where feasible.
- Gamification: points/XP, levels, badges, streaks, maybe leaderboards per district. Keep it motivating, not grindy.

Admin panel:
- Create quests for a single asset, a selection on the map, or by filter (e.g. "all trees in district X without a photo").
- Set task type, `maxCompletions`, reward points, optional time window.
- Review queue for submissions (approve / reject with reason).
- Overview of progress and export of approved contributions.

## Privacy, safety & abuse

This is a public, open-source civic project — handle data carefully (GDPR / DSGVO).
- Strip EXIF metadata from uploaded photos before storing/publishing (keep only what we need, e.g. timestamp, stored separately).
- Minimize personal data: pseudonymous player accounts (username + password, **no email address**; lost passwords are recovered with one-time recovery codes), no precise location history beyond what a submission needs.
- Photos may contain people or license plates — review step before anything is published; consider blurring later.
- Anti-cheat basics: geofence, rate limiting, duplicate photo detection (perceptual hash), claim expiry.
- Player safety: don't create quests in unsafe spots (roads, private property); show a safety hint.
- Never commit secrets. Use `.env` files (with a committed `.env.example`).

## Conventions

- **Language:** code, identifiers, commits, issues and docs in **English** (open source, other cities). UI is localized; German is the first locale — no hard-coded UI strings.
- Keep domain logic (quest limits, claim expiry, geofence) in `packages/core` (`OpenQuest.Core`), framework-free and unit-tested.
- Conventional Commits (`feat:`, `fix:`, `docs:`, …). Small, focused PRs.
- Record significant architecture decisions as short ADRs in `docs/adr/` (numbered, e.g. `0001-…md`) and research with sources in `docs/research/`.
- License: open source — **TODO: choose license** (e.g. MIT, Apache-2.0 or EUPL-1.2, the latter being common for public sector projects) and add `LICENSE`.

## Commands

### Everything in Docker

```bash
docker compose up -d --build                  # PostGIS, MinIO, API (http://localhost:5076), importer
docker compose logs api | grep admin          # admin password, generated and printed once on the first start
docker compose logs -f importer               # waits for the API's schema, then syncs (on start, then daily)
```

The official `postgis/postgis` image is amd64 only; `docker-compose.yml` pins `platform: linux/amd64` so it runs on Apple Silicon through emulation.

### Backend (.NET 10)

```bash
docker compose up -d db minio                 # PostGIS + MinIO only, to run the API from source
dotnet run --project apps/api/OpenQuest.Api   # API on http://localhost:5076 (migrates + seeds on start)
dotnet build                                  # whole solution (OpenQuest.slnx)
dotnet test                                   # unit + integration tests (see apps/api/README.md for the test database)
dotnet ef migrations add <Name> --project apps/api/OpenQuest.Api --output-dir Data/Migrations
```

More: [apps/api/README.md](apps/api/README.md).

### TypeScript packages (pnpm)

pnpm workspace (Node >= 20). Copy `.env.example` to `.env` and set `OPENROUTER_API_KEY`.

- `pnpm test`: all package tests (unit tests run offline)
- `pnpm typecheck`: frontend and all packages
- `LIVE=1 pnpm --filter @openquest/adapter-de-muenster test`: include live WFS smoke test
- `pnpm eval:fetch && pnpm eval`: tree photo verification evaluation (see ADR-0002)
- `pnpm verify <image> --lat .. --lon ..`: verify a single photo against the nearest Münster tree
- `pnpm --filter @openquest/dashboard start`: tree map with Jev search on http://localhost:8787 (`pnpm --filter @openquest/tree-search build-data` rebuilds the tree data)

Packages so far: `packages/tree-verification` (photo verification, framework free), `packages/adapters/de-muenster` (Münster tree WFS as `NearbyTreeProvider`, street names, districts), `packages/adapters/de-nrw` (tree heights from the NRW nDOM50), `packages/tree-search` (Jev search over all trees + prebuilt data), `apps/dashboard` (tree map with Jev search). The C# core lives next to them in `packages/core/OpenQuest.Core`.

### Frontend prototype (Next.js)

The frontend prototype lives in `src/` and uses Next.js, React, Tailwind CSS and Leaflet. It shows Münster trees from a bundled snapshot plus the live WFS, with demo game state in browser-local storage. The map search (`src/app/api/tree-search`) queries all Münster trees through `packages/tree-search` (Jev with `OPENROUTER_API_KEY`, rule fallback without). Photo verification is not yet connected to the frontend.

Requires Node.js 20.9 or newer. Copy `.env.example` to `.env` and set `OPENROUTER_API_KEY` for photo verification, evaluation and Jev search (the Next.js app reads `.env` or `.env.local`; the demo also runs without a key).

```bash
pnpm install --frozen-lockfile
pnpm dev        # http://localhost:3000
pnpm typecheck
pnpm build
pnpm start      # serve the production build
```

### Importer (Python)

```bash
docker compose run --rm importer sync de-muenster-trees --force   # one-off command in the container
docker compose run --rm importer adapters                         # installed adapters and enrichers
```

Local development:

```bash
docker compose up -d db minio api                         # the API creates the schema on start
cd apps/importer && python3 -m venv .venv && .venv/bin/pip install -e ".[dev]"
.venv/bin/openquest-importer check --wait 120            # wait until the API has set up the schema
.venv/bin/openquest-importer sync de-muenster-trees       # import Münster trees from the WFS
.venv/bin/pytest                                          # unit tests
OPENQUEST_TEST_DATABASE_URL=postgresql://openquest:openquest@localhost:5432/postgres .venv/bin/pytest   # + database tests
```

The first sync fetches ~10,000 nDOM cells (a few minutes); later syncs use the cache (volume `importer-cache`).

## Open questions

- The city may be working on a new tree dataset (`od-ms/converter-scripts`, see ADR-0001) — check before investing in data cleaning.
- Moderation model: admin-only review vs. community validation (e.g. "2 of 3 players agree on the species").
- Hosting for the Münster instance.
