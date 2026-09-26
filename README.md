# OpenQuest
Hackathon-Projekt: öffentliche Daten der Stadt Münster analysieren, aufbereiten und zurückgeben.

Domain: [openquest.fun](https://openquest.fun)

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

## Recherche (Research Notes)

- [Research Note 0001: Open-Data-Portal Münster und Baumkataster, Datenbezug und Rückkanal](docs/research/0001-opendata-muenster-baumkataster.md)
