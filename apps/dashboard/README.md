# @openquest/dashboard

All 43,114 Münster inventory trees on a map, with one search field. The query is interpreted by **Jev** (TypeSafe AI via OpenRouter): ordering, genus (German and English common names, e.g. "Birken", "plane trees"), city district or quarter, and whether the user wants a list or a count. Numbers (heights, "top 10") and street names are parsed deterministically; filtering and ordering run on the server, so results are reproducible.

```sh
cp .env.example .env                                   # OPENROUTER_API_KEY
pnpm install
pnpm --filter @openquest/dashboard start               # http://localhost:8787
pnpm --filter @openquest/dashboard build-data          # optional: rebuild data/trees.json (~20 min, resumable)
```

Without `OPENROUTER_API_KEY` the search falls back to simple rules.

Example queries: `größte Bäume`, `Birken in Hiltrup`, `älteste Eichen`, `Linden über 25 m`, `Kastanien an der Weseler Straße`, `Bäume ohne Gattung`, `wie viele Ginkgos?`, `tallest oaks in Hiltrup`.

## Data

`data/trees.json` (committed, 2.4 MB, columnar) is built by `scripts/build-data.ts`:

| Field | Source |
|---|---|
| Position, genus, street key | Stadt Münster, Digitales Baumkataster (WFS), dl-de/by-2.0 |
| Street name | Münster street directory WFS (`odstrasseserv`) |
| District (6), quarter (45) | Münster open data portal GeoJSON |
| **Height** | **Geobasis NRW, nDOM50** (normalized surface model, 0.5 m, summer flights), dl-de/zero-2.0: 95th percentile within 2.5 m of the tree point, one WCS request per 50 m cell |

Honest limits:

- **Age is not recorded.** "älteste" / "jüngste" sort by height as a rough proxy, and the UI says so.
- Heights are surface model heights at the inventory point: a tree next to a tall building can pick up the building (e.g. the 39 to 40 m entries at Kanalstraße are suspicious); trees under 2 m are mostly young, pruned or gone and are hidden when sorting by smallest.
- Places that are neither a district, a quarter nor a street (e.g. "am Aasee") are not resolved yet.

## Architecture

```
browser (MapLibre, OSM raster tiles)  --GET /api/trees-->   server.ts  (data/trees.json, gzipped)
                                      --POST /api/search-->  search.ts: interpret() -> Jev systemone (sort, genus, area, intent)
                                                                         parseDeterministic() (heights, limit, street)
                                                             execute()   filter + order, top N + all match indices
```

The OpenRouter key never leaves the server. Search results are cached per query.
