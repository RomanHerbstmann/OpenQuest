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
- Seven schematic game districts with clearer borders and owner colors, an always-visible top-three badge in each mapped quarter, a crown for the sole leader, shared ranks for ties, and a focused capture view with a personal progress goal. The shapes are **not official Münster district boundaries**.
- Bottom navigation and initial missions, collection and profile screens.
- Design 2.0 visual system across the map, tree profile, scan, collection, missions, profile and admin dashboard: floating navigation, richer motion, clearer status and a premium card presentation. A searchable nature lexicon at `/lexicon` contains 13 species/group portraits with leaf, bark, fruit, season and habitat cues. These are editorial prototype notes; they are not measured facts about an individual tree. Tree profiles and the card viewer link to the corresponding portrait.
- The supplied OpenQuest chest-and-wordmark artwork is used throughout the app, with the chest as the browser icon. Display headings use a locally bundled Press Start 2P font; its OFL license is included in `public/fonts/OFL.txt`.
- Shared player context with local progress and level calculation. V3 seeds the Eiche, Linde and Zierkirsche cards as revealed; other card images use the supplied OpenQuest back until a new scan unlocks them. The v1 demo save is migrated without treating earlier Festtanne tests as a stage reveal.
- Camera scan prototype in the collection and tree details: choose a specific tree point when starting from the Baumbuch, capture or choose a photo, see the scan animation and an explicitly labeled demo species response, correct the species, and save each scan separately in browser `localStorage`. Repeated scans of the same individual tree increase the find count and history while its card image unlocks only once. A mobile camera-picker fallback is available if the live preview fails. The photo itself is not stored or sent to a server. The recognition seam is `src/lib/mockScan.ts`.
- The Baumbuch lists only species with a supplied card image, shows the shared Ahorn artwork once, and can be filtered to revealed cards. Locked artwork is also hidden in the card viewer and lexicon. Species without card art remain available as text in the nature lexicon or as scan suggestions.
- Ten normalized asset examples from the supplied `assets_first_10.csv` are bundled in `src/data/treeAssetSamples.json`. Their UUIDs, exact coordinates, genus, street, district, height, source and last-seen date are bound to ten additional map points. All ten lack a confirmed species, so precise species in the demo scan are suggestions only. The existing 24 quest markers retain their original WFS coordinates and synthetic `ms-*` identifiers unless a sample asset matches by location, genus and street key; the ten examples do not match them. A full asset export/API is needed to attach backend UUIDs and additional attributes to all markers.
- Admin review demo at `/admin` with eight sample reports plus new local scan suggestions, search, status filters, map context, review notes, local decisions and CSV/GeoJSON export. Scan suggestions retain the selected tree ID and remain pending until reviewed; no GPS check or photo persistence exists yet.

Quest completion is not implemented yet. Demo scans add a local pending species suggestion, not an approved municipal update. They do not grant repeat XP or immediate territory points. Only reviewed, distinct tree IDs can enter the district ranking, so scanning one tree multiple times cannot multiply its score. The presentation Festtanne awards 25 XP once when first collected through its pin, matching the supplied card art. A bundled city-tree snapshot supports the density overlay and the Quest pins use selected municipal inventory coordinates; game states and precise species remain demo content. Map tiles require an internet connection.

For the stage demo, click **Festtanne scannen** on the map, then scan the small tree or select a photo. The test response proposes Festtanne, reveals its supplied full-art card, and adds it to the Baumbuch after confirmation. The pin is positioned on the OpenStreetMap building for Hafenweg 7; it is a presentation prop, not a public tree or territory contribution.

Territory ownership is a demo preview: only project-verified observations of distinct trees count once per player and district if the tree was observed within the last 30 days. The unique leader holds the district; a tie leaves it contested. Fictional rival contributions populate the leaderboard. Future player observations can join the scoring once mission submission and review are connected. Existing collection progress does not count as verified territory points.

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
