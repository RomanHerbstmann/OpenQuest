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

- Responsive Münster map with OpenStreetMap tiles and 24 fictional demo trees.
- Markers for open, confirmed and reported missing trees, tree detail sheets, map filters and geolocation with a manual fallback.
- Bottom navigation and initial missions, collection and profile screens.
- Shared player context with local progress and level calculation.
- Admin review demo at `/admin` with eight sample reports, search, status filters, map context, review notes, local decisions and CSV/GeoJSON export.

Quest completion is not implemented yet. Phase 3 will add questions, locally stored observations, XP rewards and collection entries. Tree data is bundled with the app; map tiles require an internet connection. The prototype uses fictional tree data and does not import the municipal tree inventory yet.

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

## Entscheidungen

- [ADR-0001: Baumkataster direkt vom WFS beziehen, Rückkanal über GitHub und Open Data Koordination](docs/adr/0001-baumkataster-datenbezug-und-rueckkanal.md)
- [ADR-0002: Tree photo verification with a vision ensemble, Jev and geo context](docs/adr/0002-tree-photo-verification.md)

## Recherche (Research Notes)

- [Research Note 0001: Open-Data-Portal Münster und Baumkataster, Datenbezug und Rückkanal](docs/research/0001-opendata-muenster-baumkataster.md)
