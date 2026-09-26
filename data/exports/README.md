# Example export

`assets_first_10.csv`: the first 10 assets from the database after an import with the Python importer (`apps/importer`, `sync --all`), ordered by `first_seen_at`, then `id`. Attributes are flattened into columns (`genus`, `street_name`, `district`, `quarter`, `height_m`, `avenue_id`, `avenue_name`, …), `quality_flags` are separated by `;`.

Exported with:

```bash
docker compose run --rm importer sync --all
docker compose exec -T db psql -U openquest -d openquest -c "\copy (SELECT ... FROM asset ... LIMIT 10) TO STDOUT WITH (FORMAT csv, HEADER)"
```

The rest of `data/` (snapshots, caches) stays git-ignored; files here are added with `git add -f`.

## Sources and licenses

- Trees, street names, districts and quarters: Stadt Münster, Digitales Baumkataster, [dl-de/by-2-0](https://www.govdata.de/dl-de/by-2-0), https://opendata.stadt-muenster.de/dataset/digitales-baumkataster-m%C3%BCnster. Daten bereinigt und angereichert durch Team OpenQuest.
- `height_m`: Geobasis NRW, nDOM50, [dl-de/zero-2-0](https://www.govdata.de/dl-de/zero-2-0). Object height above ground at the tree point, not a measured tree height.
- `avenue_id`, `avenue_name`: LANUK NRW, Alleenkataster NRW (LINFOS), [dl-de/zero-2-0](https://www.govdata.de/dl-de/zero-2-0).
