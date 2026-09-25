# OpenQuest importer

Loads open data sets (first: the Münster tree inventory) into the OpenQuest database and keeps them up to date.

Each sync:

1. downloads the complete data set through an **adapter** and stores the file unchanged (`sync_run.snapshot_key`),
2. fails if the source's fields changed (`sync_run.schema_hash`),
3. normalizes the records and validates them against the asset type's JSON Schema,
4. **matches** them to the existing assets, so each asset keeps our own id (`asset.id`),
5. writes created / updated / removed assets plus their history (`asset_snapshot`) in one transaction.

Every run is recorded in `sync_run`, including failed ones. Data model: [docs/data-model/erd.md](../../docs/data-model/erd.md). The id decision is in [ADR-0002](../../docs/adr/0002-eigene-asset-id-und-raeumliches-matching.md).

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

## Local setup (development)

Requires Python ≥ 3.11 and Docker.

```bash
docker compose up -d db                      # from the repository root; PostGIS on localhost:5433
cd apps/importer
python3 -m venv .venv
.venv/bin/pip install -e ".[dev]"
.venv/bin/openquest-importer migrate
.venv/bin/openquest-importer sync de-muenster-trees
```

Commands (run in `apps/importer`, or pass `--config`):

| Command | What it does |
|---|---|
| `openquest-importer migrate` | Applies the SQL migrations in `db/migrations/` |
| `openquest-importer sync SOURCE…` / `sync --all` | Syncs the given sources from `importer.toml` |
| `openquest-importer sync SOURCE --force` | Applies a sync even if it removes more than `max_removal_ratio` of the assets |
| `openquest-importer adapters` | Lists installed adapters |

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

Enrichers keep data between syncs in the cache directory (`[cache] dir`, `OPENQUEST_CACHE_DIR`; volume `importer-cache` in Docker). For nDOM the first sync of Münster fetches ~10,000 cells, later syncs only the cells of new or moved trees. If cells fail, the sync fails; successful cells are cached, so the next sync resumes. What an enricher used is stored in the snapshot.

Writing one: subclass `Enricher` (`openquest_importer/enrichers/base.py`), set `attributes`, implement `enrich(assets)`, and register it under the entry point group `openquest.enrichers`. Add its attributes to the asset type's schema with a migration.

## How assets are matched

Adapters declare an identity strategy:

- **`EXTERNAL_ID`**: the source has stable ids; records are matched by `external_id`.
- **`SPATIAL`**: the source has no ids (Münster). A record is matched to the asset with an identical record (`source_hash`) first, otherwise to the nearest unmatched asset within `match_radius_m` (default 1 m). Closest pairs win; each asset and each record is used at most once. Unmatched records become new assets, unmatched assets are marked `removed_at_source`.

An asset is **updated** when its raw record or its normalized attributes changed, so improved cleaning rules reach existing assets too.

The **removal guard** stops a sync that would remove more than `max_removal_ratio` (default 20 %) of the active assets. A truncated or broken download can't wipe the data set.

## Writing an adapter for another city

1. Subclass `DataSourceAdapter` (`openquest_importer/adapters/base.py`): set `asset_type`, `identity` and `expected_fields`, implement `fetch()` (download the whole data set) and `parse()` (return `NormalizedAsset`s in WGS84 with attributes that match the asset type's schema).
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

Unit tests need nothing else. The database tests run when `OPENQUEST_TEST_DATABASE_URL` points to a Postgres/PostGIS server (e.g. `postgresql://openquest:openquest@localhost:5433/postgres` with docker compose). They create and drop their own temporary database.
