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
| **Data source adapter** | Pluggable module that reads assets from a city's open data platform (and optionally exports contributions back). The Münster adapter is just one implementation. |
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
City open data platform  ──►  Data source adapter  ──►  Core (normalized Assets)  ──►  API  ──►  Web app / Admin panel
                          ◄──  (optional export)   ◄──  approved Contributions
```

An adapter implements a small, stable interface (sketch — the real one lives in the core package and is the source of truth):

```ts
interface DataSourceAdapter {
  id: string;                         // e.g. "de-muenster"
  name: string;                       // human readable
  supportedAssetTypes: AssetType[];   // e.g. ["tree"]

  // Read side
  fetchAssets(query: { assetType: AssetType; bbox?: BBox; updatedSince?: Date }): AsyncIterable<Asset>;
  getAsset(assetType: AssetType, externalId: string): Promise<Asset | null>;

  // Write-back side (optional – many platforms are read-only)
  exportContributions?(contributions: ApprovedContribution[]): Promise<ExportResult>;
}
```

Adapter rules:
- Normalize everything to the core `Asset` model: WGS84 (EPSG:4326) coordinates, stable `externalId`, typed attributes. Keep the raw source record in a `raw` field for debugging.
- Map city-specific field names (e.g. German column names) to core attribute names inside the adapter only.
- Adapters are selected via configuration (env / config file), never via `if (city === "muenster")` in core code.
- Every adapter ships with fixture data and tests so it can be developed offline.
- Assets are **imported/synced into our own database** (scheduled job), not fetched live per request. The open data platform is not a runtime dependency of the game.

Adding a new city = add a new adapter package + config. Adding a new asset type = add an asset type definition (schema + allowed task types) in core, then support it in adapters.

## Münster data source

- Platform: https://opendata.stadt-muenster.de (check the platform's API; many German municipal portals are CKAN-based).
- Dataset: tree inventory (`gruen_opendata.csv`, ~43k trees) with only three columns: `WKT` (point, WGS84), `str_schl` (street key), `baumgruppe` (genus). **It has no id column**, so the adapter must derive a stable `external_id`. Details: [docs/data-model/erd.md](docs/data-model/erd.md#münster-tree-data-gruen_opendatacsv).
- **TODO: verify dataset id on the portal, update cadence and license** and document them in the adapter's README.
- Respect the dataset license (typically dl-de/by-2.0 or CC BY) — attribution must be shown in the app.
- Write-back: the portal is most likely read-only for us. Plan for exporting approved contributions as a dataset/file handed to the city until an official ingestion path exists.

## Data model

The ERD and the reasoning behind it live in [docs/data-model/erd.md](docs/data-model/erd.md). Keep it in sync when the schema changes. Short version: open data objects are generic `ASSET`s with an `ASSET_TYPE` and JSONB `attributes` (validated by a JSON Schema per type), so new data sets need no schema migration.

## Suggested tech stack (proposal — not final)

Nothing is set in stone yet. Update this section once decisions are made.

- **Language:** TypeScript end-to-end.
- **Monorepo:** pnpm workspaces.
- **Web app:** mobile-first **PWA** (camera + geolocation in the browser, no app store needed). Map with **MapLibre GL** + OpenStreetMap-based tiles.
- **Admin panel:** separate app or protected area of the web app for quest creation, review queue and stats.
- **Backend API:** Node.js (e.g. Fastify or NestJS) with OpenAPI spec.
- **Database:** PostgreSQL + **PostGIS** for geo queries ("quests within 500 m").
- **File storage:** S3-compatible (MinIO locally) for photos.
- **Local dev:** Docker Compose (Postgres/PostGIS, MinIO).

Proposed layout:

```
apps/
  web/          # player PWA
  admin/        # admin panel
  api/          # backend API + sync jobs
packages/
  core/         # domain model, adapter interface, quest/claim logic (framework-free)
  adapters/
    de-muenster/  # Münster open data adapter
  ui/           # shared UI components (optional)
docs/           # architecture notes, ADRs, adapter guide
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
- Keep domain logic (quest limits, claim expiry, geofence) in `packages/core`, framework-free and unit-tested.
- Conventional Commits (`feat:`, `fix:`, `docs:`, …). Small, focused PRs.
- Record significant architecture decisions as short ADRs in `docs/adr/`.
- License: open source — **TODO: choose license** (e.g. MIT, Apache-2.0 or EUPL-1.2, the latter being common for public sector projects) and add `LICENSE`.

## Commands

No code exists yet. Add build/test/dev commands here as soon as the project is scaffolded (e.g. `pnpm install`, `pnpm dev`, `pnpm test`, `docker compose up`).

## Open questions

- Exact Münster tree dataset and its fields/license (see above).
- How approved contributions get back to the city (file export vs. API vs. manual process) — needs contact with Stadt Münster.
- Moderation model: admin-only review vs. community validation (e.g. "2 of 3 players agree on the species").
- Hosting for the Münster instance.
