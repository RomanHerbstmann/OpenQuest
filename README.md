# OpenQuest
Hackathon-Projekt: öffentliche Daten der Stadt Münster analysieren, aufbereiten und zurückgeben.

Domain: [openquest.fun](https://openquest.fun)

## Frontend prototype

The repository includes a mobile-first Next.js prototype for the Münster tree quest demo. It implements phases 1 and 2 of [the prototype brief](openquest-codex-prototype-brief-v0.1.md), plus a local admin review demo. The interface is in German.

### Run locally

Requires Node.js 20.9 or newer and pnpm.

```bash
pnpm install --frozen-lockfile
pnpm dev
```

Open `http://localhost:3000`. For a production build, run `pnpm build` followed by `pnpm start`.

### Included features

- Responsive Münster map with light, colorful OpenStreetMap tiles and a localized tree-density overlay from the city's public digital tree inventory. Individual street trees appear as small dots when zoomed in. The browser refreshes the visible area from Münster's WFS; `public/data/muenster-trees-snapshot.json` is a bundled 25 September 2026 fallback. The 24 regular Quest markers now use distinct, original WFS tree coordinates matched by recorded genus and nearby location (retrieved 26 September 2026); their street labels come from Münster's street WFS. Exact species, verification status, rarity, and game progress remain demo content because the tree inventory supplies genus and position only. The separate presentation tree at Hafenweg 7 keeps its fixed stage location and does not affect the density overlay. Tree data: Stadt Münster, Digitales Baumkataster, dl-de/by-2.0.
- Markers for open, confirmed and reported missing trees, tree detail sheets, map filters and geolocation with a manual fallback.
- Seven schematic game districts on the map with a district overview, rankings and a 30-day territory rule. The shapes are **not official Münster district boundaries**.
- Bottom navigation and initial missions, collection and profile screens.
- Design 2.0 visual system across the map, tree profile, scan, collection, missions, profile and admin dashboard: floating navigation, richer motion, clearer status and a premium card presentation. A searchable nature lexicon at `/lexicon` contains 13 species/group portraits with leaf, bark, fruit, season and habitat cues. These are editorial prototype notes; they are not measured facts about an individual tree. Tree profiles and the card viewer link to the corresponding portrait.
- Shared player context with local progress and level calculation.
- Camera scan in the collection and tree details: capture or choose a photo, see a scan animation and save one card per species in browser `localStorage`. A mobile camera-picker fallback is available if the live preview fails. Photos are checked by the real tree photo verification (see below); the presentation Festtanne keeps its scripted demo answer.
- Tree photo verification: `src/lib/treeScan.ts` downscales the photo in the browser to about 1024 px JPEG (this drops EXIF and GPS tags), adds the browser position and the quest tree, and posts it to `POST /api/verify`. The route runs `@openquest/tree-verification` with the Münster WFS and OSM as neighbor tree sources. The scan dialog shows the verdict, the detected genus with probability, a hint on a genus mismatch and the reasons in German. XP and the card are only granted on `approve`; `review` is queued as an observation with status `needs_review` for the admin demo; `reject` grants nothing. The photo is not stored.
- The Baumbuch lists only species with a supplied card image, shows the shared Ahorn artwork once, and can be filtered to collected cards. Species without card art remain available in the nature lexicon.
- Natural language search above the map (for example "die größten Birken in Hiltrup") over all 43,114 Münster trees via `POST /api/tree-search`. It uses Jev when `OPENROUTER_API_KEY` is set in `.env` or `.env.local` and falls back to rules otherwise. Matches are drawn as a separate map layer, the top 25 are numbered.
- Admin review demo at `/admin` with eight sample reports, search, status filters, map context, review notes, local decisions and CSV/GeoJSON export.

Quest completion is not implemented yet. Phase 3 will add questions, locally stored observations and XP rewards. Demo scans only add collection cards and do not grant territory points. The presentation Festtanne awards 25 XP once when first collected through its pin, matching the supplied card art. A bundled city-tree snapshot supports the density overlay and the Quest pins use selected municipal inventory coordinates; game states and precise species remain demo content. Map tiles require an internet connection.

For the stage demo, click **Festtanne scannen** on the map, then scan the small tree or select a photo. The test response proposes Festtanne, reveals its supplied full-art card, and adds it to the Baumbuch after confirmation. The pin is positioned on the OpenStreetMap building for Hafenweg 7; it is a presentation prop, not a public tree or territory contribution.

Territory ownership is a demo preview: only project-verified observations of distinct trees count once per player and district if the tree was observed within the last 30 days. The unique leader holds the district; a tie leaves it contested. Fictional rival contributions populate the leaderboard. Future player observations can join the scoring once mission submission and review are connected. Existing collection progress does not count as verified territory points.

### Photo verification API

Put `OPENROUTER_API_KEY` into `.env.local` (never commit it). Without the key `/api/verify` answers `503` and the app falls back to the demo answer and says so in the dialog.

`POST /api/verify` takes `multipart/form-data`: `image` (JPEG, PNG or WebP, max 5 MB), optional `lat`, `lon`, `accuracy` (player position, meters) and either `treeId` (a quest tree from `src/data/trees.ts`) or `expectedLat`, `expectedLon`, `expectedGenus`. Optional `capturedAt` (ISO time) lets old gallery photos go to review. The server strips JPEG/PNG metadata before verification and allows 10 requests per minute and IP (in memory), because every call costs OpenRouter credits. The response is `{ expected, result }` with the full `TreeVerificationResult`; errors are `{ error, message }`.

```bash
curl -F image=@eval/images/platanus-2.jpg -F lat=51.964258 -F lon=7.627308 -F accuracy=8 -F treeId=ms-004 http://localhost:3000/api/verify
```

The admin area is a local demo without authentication or a backend. Review decisions only update browser `localStorage`; they never change official city data. Public deployment requires access control, server-side persistence and review rules.

### Validation

```bash
pnpm typecheck
pnpm build
```

### Prototype planning

- [Hackathon concept](ms-hack-stadtbaum-quest-konzept-1.md)
- [Prototype brief](openquest-codex-prototype-brief-v0.1.md)
- [Frontend plan](openquest-frontend-planung-v0.1.md)

## Dashboard

Alle Bäume auf einer Karte, Suche per Jev: [apps/dashboard](apps/dashboard/README.md) (`pnpm --filter @openquest/dashboard start`).

## Entscheidungen

- [ADR-0001: Baumkataster direkt vom WFS beziehen, Rückkanal über GitHub und Open Data Koordination](docs/adr/0001-baumkataster-datenbezug-und-rueckkanal.md)
- [ADR-0002: Tree photo verification with a vision ensemble, Jev and geo context](docs/adr/0002-tree-photo-verification.md)
- [ADR-0003: Backend in .NET 10](docs/adr/0003-backend-dotnet.md)
- [ADR-0004: Event-driven write-back to open data](docs/adr/0004-event-driven-writeback.md)
- [ADR-0005: Tree assessment from player photos (condition, tree pit, phenology, inventory)](docs/adr/0005-tree-assessment-from-player-photos.md)
- [ADR-0006: Eigene Asset-ID und räumliches Matching beim Import](docs/adr/0006-eigene-asset-id-und-raeumliches-matching.md)
- [ADR-0007: Cities and districts are drawn by admins and owned by the API](docs/adr/0007-cities-and-districts-drawn-by-admins.md)

## Datenmodell

- [ERD](docs/data-model/erd.md)

## Backend

- [apps/api/README.md](apps/api/README.md): starten, API-Überblick, Tests

## Importer

- [apps/importer/README.md](apps/importer/README.md): Open-Data-Import (Python), Adapter und Enricher

## Daten

- [Data sources: Herkunft, Lizenzen, Attribution aller Daten](docs/data-model/data-sources.md)
- [Data model (ERD)](docs/data-model/erd.md)

## Recherche (Research Notes)

- [Research Note 0001: Open-Data-Portal Münster und Baumkataster, Datenbezug und Rückkanal](docs/research/0001-opendata-muenster-baumkataster.md)
