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

Decided in [ADR-0001](docs/adr/0001-baumkataster-datenbezug-und-rueckkanal.md), evidence in [Research Note 0001](docs/research/0001-opendata-muenster-baumkataster.md). Read both before working on the Münster adapter.

- **Portal:** https://opendata.stadt-muenster.de runs **DKAN 7 on Drupal 7** (not CKAN). Its API only holds metadata, is slow and has no write access. Don't use it for data.
- **Source:** the tree data comes live from the city's **MapServer WFS** `https://geo.stadt-muenster.de/mapserv/odgruen_serv`, layer `Baeume` (GeoJSON in WGS84 for the map, `SRSNAME=EPSG:25832` for the database). CORS is open.
- **Content:** ~43k trees with only three fields: point, `str_schl` (street key), `baumgruppe` (genus). Data as of 2017/2020, about half of the city's trees. **No stable id**, so the adapter derives `external_id` itself. ~8 % placeholders instead of a genus (e.g. `Baum Amt62`). Details: [docs/data-model/erd.md](docs/data-model/erd.md#münster-tree-data-gruen_opendatacsv).
- **Snapshots:** the adapter loads snapshots (manually or daily) and keeps the history (`SYNC_RUN`, `ASSET_SNAPSHOT`). If the WFS schema changes, the import must fail loudly.
- **License:** dl-de/by-2.0, attribution required. Use the attribution text from ADR-0001 in the app, README and every published file. No city logos or coat of arms, nothing that looks official.
- **Write-back:** the portal cannot be written to. Approved contributions go back as a published cleaned dataset (GitHub) plus a message to the city's open data coordination (see ADR-0001).

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
- Record significant architecture decisions as short ADRs in `docs/adr/` (numbered, e.g. `0001-…md`) and research with sources in `docs/research/`.
- License: open source — **TODO: choose license** (e.g. MIT, Apache-2.0 or EUPL-1.2, the latter being common for public sector projects) and add `LICENSE`.

## Commands

pnpm workspace (Node >= 20). Copy `.env.example` to `.env` and set `OPENROUTER_API_KEY`.

- `pnpm install`
- `pnpm test` / `pnpm typecheck`: all packages (unit tests run offline)
- `LIVE=1 pnpm --filter @openquest/adapter-de-muenster test`: include live WFS smoke test
- `pnpm eval:fetch && pnpm eval`: tree photo verification evaluation (see ADR-0002)
- `pnpm verify <image> --lat .. --lon ..`: verify a single photo against the nearest Münster tree

Packages so far: `packages/tree-verification` (photo verification, framework free), `packages/adapters/de-muenster` (Münster tree WFS as `NearbyTreeProvider`).

## Open questions

- Tree id: ADR-0001 includes the genus in the id hash, which breaks quests when a genus gets corrected. OpenQuest needs a coordinate-only id (see [erd.md](docs/data-model/erd.md#münster-tree-data-gruen_opendatacsv)).
- The city may be working on a new tree dataset (`od-ms/converter-scripts`, see ADR-0001) — check before investing in data cleaning.
- Moderation model: admin-only review vs. community validation (e.g. "2 of 3 players agree on the species").
- Hosting for the Münster instance.
