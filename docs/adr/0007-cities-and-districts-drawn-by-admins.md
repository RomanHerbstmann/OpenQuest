# ADR-0007: Cities and districts are drawn by admins and owned by the API

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-09-26 |
| Decider | Backend developer (hackathon team) |
| Related | [ADR-0003](0003-backend-dotnet.md) (backend), [ADR-0004](0004-event-driven-writeback.md) (events), [ERD](../data-model/erd.md) |

## Context

The game needs a split of the city into districts ("Stadtviertel") for the leaderboard: the district with the most points "owns" the city.
The city's open data has district boundaries for Münster, but other cities may not, and a district split for a game is a design choice, not a
fact: an admin may want other borders, names and descriptions per city. The importer already stores `district` / `quarter` names on trees,
but these are raw source data and tied to what the city publishes.

## Decision

- **Admins define the districts** in the admin panel. The API stores them: `city` → many `district` → ordered `district_point` rows.
  The API's schema owns these tables (like everything else except what the importer writes).
- **Shape = ordered ring of points.** The points are connected in the order they were drawn and close back to the first. The API derives a
  PostGIS polygon (`district.geom`) and a centre from them on every save. Edges must not cross (`PolygonRules` in the core), and the districts of one
  city must not overlap (shared borders are fine; a tiny tolerance, `Gamification:OverlapToleranceRatio`, absorbs rounding at shared borders).
- **GeoJSON is the wire format** for shapes (`Polygon` or `Feature`, `[lon, lat]`), because drawing tools on the map produce and consume it. A
  dry-run endpoint reports problems (crossing point, overlapping district) while drawing. GeoJSON import creates districts from a
  `FeatureCollection` so an existing split (for example Münster's 45 Stadtteile) does not have to be drawn by hand.
- **Membership is geometric.** Points are awarded to the district the quest's tree lies in (`ST_Covers` on approval). The ledger stores the
  district (`point_transaction.district_id`), so redrawing a district later does not move points that were already earned. Deactivating a
  district keeps its history; deleting is only possible while no points were earned there.
- **The leaderboard is computed from the ledger** (per district, `all` or the calendar week in the city's time zone); `district.total_points` is a
  cache like `user.total_points`.
- Reading (cities, districts, leaderboard) needs a login, writing needs the `admin` role. Admins are global for now, not per city.

## Consequences

- No dependence on the importer or on a city's open data for the game's districts; every city can be set up through the API.
- Only single, simple polygons are supported (no holes, no multi-part districts). An enclave would have to be drawn as its own district.
- Trees outside every district count for the player, not for a district.
- Later game parts (card rarity per district, weekly quests per district) can build on `district` and `district_point`.
