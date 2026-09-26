# OpenQuest importer

Loads open data sets into the OpenQuest database and keeps them up to date: trees, natural monuments, citizen reports and environment readings.

## Sources (`importer.toml`)

| Source key | Adapter | Writes | Enrichers | Licence |
|---|---|---|---|---|
| `de-muenster-trees` | `de_muenster.trees` | ~43,000 city trees (`asset`, type `tree`) with street, district, quarter | nDOM height, avenues | dl-de/by-2.0 |
| `de-nrw-strassen-trees-muenster` | `de_nrw.strassen_trees` | ~2,300 trees along federal/state roads in Münster (Straßen.NRW) | district, quarter, nDOM height, avenues | dl-de/zero-2.0 |
| `de-muenster-natural-monuments` | `de_muenster.natural_monuments` | natural monuments (`asset`, type `natural_monument`) from the city's WMS. **Disabled**: licence not stated | avenues | unknown |
| `de-muenster-maengelmelder-trees` | `open311.reports` | citizen reports "Baum" and "Eichenprozessionsspinner" (`asset_report`), linked to the nearest tree | – | dl-de/by-2.0 |
| `dwd-soil-1766` | `dwd.soil_daily` | daily soil moisture, evaporation, soil temperature of station Münster/Osnabrück (`environment_reading`) | – | GeoNutzV |

`sync --all` runs every enabled source; a disabled one still runs when named (`sync de-muenster-natural-monuments`).

Adapters come in three kinds, by what they write: **assets** (`DataSourceAdapter`), **reports** (`ReportAdapter`: matched by the source's id, linked to the nearest asset within `link_radius_m`) and **readings** (`ReadingAdapter`: matched by station, metric and time). Report and reading feeds only show a window of recent data, so nothing is removed when it drops out of the window.

Each sync:

1. downloads the complete data set through an **adapter** and stores the file unchanged (`sync_run.snapshot_key`),
2. fails if the source's fields changed (`sync_run.schema_hash`),
3. normalizes the records and validates them against the asset type's JSON Schema,
4. **matches** them to the existing assets, so each asset keeps our own id (`asset.id`),
5. writes created / updated / removed assets plus their history (`asset_snapshot`) in one transaction.

Every run is recorded in `sync_run`, including failed ones. Data model: [docs/data-model/erd.md](../../docs/data-model/erd.md). The id decision is in [ADR-0006](../../docs/adr/0006-eigene-asset-id-und-raeumliches-matching.md).

## Run with Docker

Everything runs in Docker, no local Python needed. From the repository root:

```bash
docker compose up -d --build          # PostGIS + importer
docker compose logs -f importer
```

The importer container applies the migrations, syncs all sources from `importer.toml` and then syncs again every `SYNC_INTERVAL_SECONDS` (default 86400 = daily; `0` = sync once and exit). Snapshots are kept in the `snapshots` volume.

Run a single command in the container (arguments are passed to `openquest-importer`):

```bash
docker compose run --rm importer sync de-muenster-trees --force
docker compose run --rm importer adapters
```

## Database schema

The schema belongs to the **API**: its EF Core migrations create the tables when the API starts, and it seeds the asset types (the tree attribute schema is `AssetType.Tree` in `packages/core/OpenQuest.Core/Domain/AssetType.cs`). The importer has no migrations of its own. It writes `data_source`, `sync_run`, `asset` and `asset_snapshot`, reads `asset_type`, and refuses to run until the API has set up the database (`openquest-importer check`). In Docker it waits for the API on start.

New attributes an adapter or enricher writes must be added to the asset type in C#; `tests/test_ef_schema.py` fails otherwise.

## Local setup (development)

Requires Python ≥ 3.11 and Docker.

```bash
docker compose up -d db minio api            # from the repository root; the API sets up the schema
cd apps/importer
python3 -m venv .venv
.venv/bin/pip install -e ".[dev]"
.venv/bin/openquest-importer check --wait 120
.venv/bin/openquest-importer sync de-muenster-trees
```

Commands (run in `apps/importer`, or pass `--config`):

| Command | What it does |
|---|---|
| `openquest-importer check [--wait SECONDS]` | Checks that the API has set up the database schema |
| `openquest-importer sync SOURCE…` / `sync --all` | Syncs the given sources from `importer.toml` |
| `openquest-importer sync SOURCE --force` | Applies a sync even if it removes more than `max_removal_ratio` of the assets |
| `openquest-importer sync SOURCE --accept-schema-change` | Imports even if the source's fields changed (a sync normally fails then); the run records the new `schema_hash`. Afterwards update `expected_fields` in the adapter |
| `openquest-importer adapters` | Lists installed adapters and enrichers |

After every successful run (assets, reports and readings alike) the importer sends `pg_notify('sync_finished', <sync_run id>)` in the same transaction as the run's result
(`mark_succeeded` in `sync.py`). The API listens and turns it into an event; failed runs send nothing.

`DATABASE_URL` overrides the database URL and `OPENQUEST_SNAPSHOT_DIR` the snapshot directory from `importer.toml`. Locally, snapshots are stored in `data/snapshots/` at the repository root (git-ignored).

## Münster tree adapter

`de_muenster.trees` reads the trees from the city's WFS and cleans them (placeholder genera, typos, species, 5-digit street keys, near-duplicates). It also adds

- `street_name` from the street directory WFS (join over `str_schl`),
- `district` (Stadtbezirk) and `quarter` (Stadtteil) by point in polygon against the GeoJSON files from the open data portal.

The street list and the district and quarter files are downloaded with every sync and stored in the snapshot, so a sync can be reproduced exactly. If their format changes, the sync fails. Options (in `importer.toml` under `[sources.options]`):

| Option | Default |
|---|---|
| `wfs_url`, `layer` | city WFS `odgruen_serv`, `Baeume` |
| `enrich` | `true`; `false` skips street names, districts and quarters |
| `streets_url`, `districts_url`, `quarters_url` | city street WFS and portal district / quarter GeoJSON |
| `file`, `streets_file`, `districts_file`, `quarters_file` | read local files instead (offline development; `tests/fixtures` has samples) |
| `timeout_s` | `120` |

## Enrichers

Enrichers add attributes that come from other data sets than the source, keyed by location. They are not tied to a city: switch one on for any source it covers.

```toml
[[sources.enrichers]]
name = "de_nrw.ndom_height"

[sources.enrichers.options]
radius_m = 2.5
```

| Enricher | Sets | Source |
|---|---|---|
| `de_nrw.ndom_height` | `height_m` | nDOM50 of Geobasis NRW (WCS, dl-de/zero-2.0): 95th percentile of the object height within `radius_m` of the tree point, one request per 50 m cell. Options: `radius_m`, `cell_m`, `concurrency`, `timeout_s`, `retries`, `max_age_days` |
| `de_nrw.alleen` | `avenue_id`, `avenue_name` | Alleenkataster NRW (LINFOS WFS, dl-de/zero-2.0): trees within `distance_m` (default 10) of a legally protected avenue |
| `geo.area_name` | the `attribute` option | any polygon GeoJSON (`url`/`file`) with the name in `name_field`, e.g. Stadtbezirke → `district`. Can be configured several times per source |

Enrichers keep data between syncs in the cache directory (`[cache] dir`, `OPENQUEST_CACHE_DIR`; volume `importer-cache` in Docker). For nDOM the first sync of Münster fetches ~10,000 cells, later syncs only the cells of new or moved trees. If cells fail, the sync fails; successful cells are cached, so the next sync resumes. What an enricher used is stored in the snapshot.

Writing one: subclass `Enricher` (`openquest_importer/enrichers/base.py`), set `attributes`, implement `enrich(assets)`, and register it under the entry point group `openquest.enrichers`. Add its attributes to the asset type's schema with a migration.

## How assets are matched

Adapters declare an identity strategy:

- **`EXTERNAL_ID`**: the source has stable ids; records are matched by `external_id`.
- **`SPATIAL`**: the source has no ids (Münster). A record is matched to the asset with an identical record (`source_hash`) first, otherwise to the nearest unmatched asset within `match_radius_m` (default 1 m). Closest pairs win; each asset and each record is used at most once. Unmatched records become new assets, unmatched assets are marked `removed_at_source`.

An asset is **updated** when its raw record or its normalized attributes changed, so improved cleaning rules reach existing assets too.

The **removal guard** stops a sync that would remove more than `max_removal_ratio` (default 20 %) of the active assets. A truncated or broken download can't wipe the data set.

## Writing an adapter for another city

1. Subclass `DataSourceAdapter` (`openquest_importer/adapters/base.py`): set `asset_type`, `identity` and `expected_fields`, implement `fetch()` (download the whole data set) and `parse()` (return `NormalizedAsset`s in WGS84 with attributes that match the asset type's schema). For reports or readings subclass `ReportAdapter` / `ReadingAdapter` instead. `adapters/http.py` (downloads with retries, local files for offline development) and `adapters/boundary.py` (clip to a city) help.
2. Register it under the entry point group `openquest.adapters`, either in this package's `pyproject.toml` or in your own package:

   ```toml
   [project.entry-points."openquest.adapters"]
   "de_example.benches" = "openquest_example.benches:BenchesAdapter"
   ```

3. Add a source to `importer.toml` with `adapter = "de_example.benches"`.
4. Ship a small fixture file and tests so the adapter can be developed offline.

Keep everything city-specific inside the adapter. The core never branches on city names.

## Tests

```bash
.venv/bin/pytest
```

Unit tests need nothing else. The database tests run when `OPENQUEST_TEST_DATABASE_URL` points to a Postgres/PostGIS server (e.g. `postgresql://openquest:openquest@localhost:5432/postgres` with docker compose). They create and drop their own temporary database, build the API's schema from `tests/fixtures/ef_schema.sql` and seed the tree asset type from `AssetType.cs`. After adding an EF migration, regenerate the schema file with `scripts/update-ef-schema.sh` (needs the .NET SDK); `tests/test_ef_schema.py` fails while it is outdated.
