"""Tree heights from the normalized digital surface model of NRW (nDOM50).

nDOM = object height above ground on a 0.5 m grid (image based surface model
from summer flights minus the laser scanning terrain model), Geobasis NRW,
licence dl-de/zero-2.0. Same method as ``packages/adapters/de-nrw`` (PR #7):

- the height of a tree is the 95th percentile of the nDOM values within
  ``radius_m`` (2.5 m) of the tree point: the crown top, robust to noise;
- trees are grouped into ``cell_m`` (50 m) squares, one WCS request per square.

Heights are cached per tree position in SQLite (``cache_dir``), so only new or
moved trees cause requests after the first sync (~10,000 requests for Münster).
Caveat (see docs/data-model/data-sources.md in PR #7): this is the height at the
inventory point, not a measured tree height. Trees next to buildings can pick
up the building; values below 2 m usually mean a young, pruned or missing tree.
"""

from __future__ import annotations

import json
import logging
import math
import sqlite3
import time
import urllib.parse
import urllib.request
from collections import defaultdict
from contextlib import closing
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Any, Callable, Mapping

from openquest_importer import __version__
from openquest_importer.adapters.base import NormalizedAsset, SnapshotFile
from openquest_importer.enrichers.base import Enricher, EnricherError
from openquest_importer.enrichers.de_nrw.geotiff import GeoTiffError, Raster, read_geotiff
from openquest_importer.geo import to_utm

log = logging.getLogger(__name__)

WCS_URL = "https://www.wcs.nrw.de/geobasis/wcs_nw_ndom"
COVERAGE_ID = "nw_ndom"
USER_AGENT = f"OpenQuest-Importer/{__version__} (+https://github.com/RomanHerbstmann/OpenQuest)"

# Plausible nDOM values; everything else is treated as noise / no data.
MIN_VALID_M, MAX_VALID_M = -5.0, 100.0

#: Fetches the raster for the UTM box (x0, y0, x1, y1). Replaceable in tests.
CellFetcher = Callable[[float, float, float, float], Raster]


def percentile(values: list[float], p: float) -> float | None:
    """Nearest-rank percentile of the plausible values; ``None`` if there are none."""
    valid = sorted(v for v in values if math.isfinite(v) and MIN_VALID_M < v < MAX_VALID_M)
    if not valid:
        return None
    return valid[min(len(valid) - 1, math.floor(p * len(valid)))]


def sample_height(raster: Raster, x: float, y: float, radius_m: float) -> float | None:
    """95th percentile of the raster cells whose centre lies within ``radius_m`` of (x, y)."""
    col_min = max(0, math.floor((x - radius_m - raster.origin_x) / raster.res_x))
    col_max = min(raster.width - 1, math.ceil((x + radius_m - raster.origin_x) / raster.res_x))
    # res_y is negative for north-up rasters
    row_min = max(0, math.floor((y + radius_m - raster.origin_y) / raster.res_y))
    row_max = min(raster.height - 1, math.ceil((y - radius_m - raster.origin_y) / raster.res_y))
    window = []
    for row in range(row_min, row_max + 1):
        py = raster.origin_y + (row + 0.5) * raster.res_y
        for col in range(col_min, col_max + 1):
            px = raster.origin_x + (col + 0.5) * raster.res_x
            if (px - x) ** 2 + (py - y) ** 2 <= radius_m**2:
                window.append(raster.value(row, col))
    height = percentile(window, 0.95)
    return None if height is None else round(height, 1)


class NdomHeightEnricher(Enricher):
    """Sets ``height_m``. Options:

    ``wcs_url``         default Geobasis NRW nDOM WCS
    ``radius_m``        window around the tree point, default 2.5
    ``cell_m``          request cell size, default 50
    ``concurrency``     parallel requests, default 6
    ``timeout_s``       per request, default 60
    ``retries``         per cell, default 3
    ``max_age_days``    re-fetch cached heights older than this, default 365
    """

    attributes = frozenset({"height_m"})

    def __init__(self, options: Mapping[str, Any] | None = None, cache_dir: Path | None = None,
                 fetch_cell: CellFetcher | None = None) -> None:
        super().__init__(options, cache_dir)
        self.wcs_url = self.options.get("wcs_url", WCS_URL)
        self.radius_m = float(self.options.get("radius_m", 2.5))
        self.cell_m = float(self.options.get("cell_m", 50))
        self.concurrency = int(self.options.get("concurrency", 6))
        self.timeout_s = float(self.options.get("timeout_s", 60))
        self.retries = int(self.options.get("retries", 3))
        self.max_age_s = float(self.options.get("max_age_days", 365)) * 86_400
        self._fetch_cell = fetch_cell or self._fetch_cell_from_wcs

    # --- public ------------------------------------------------------------

    def enrich(self, assets: list[NormalizedAsset]) -> SnapshotFile | None:
        positions = [to_utm(a.lat, a.lon) for a in assets]
        keys = [self._cache_key(x, y) for x, y in positions]

        with closing(self._open_cache()) as cache:
            heights = self._cached(cache, set(keys))
            missing = [i for i, key in enumerate(keys) if key not in heights]
            if missing:
                self._fetch_missing(cache, missing, positions, keys, heights)

        for asset, key in zip(assets, keys):
            asset.attributes["height_m"] = heights[key]

        with_height = sum(1 for k in keys if heights[k] is not None)
        log.info("nDOM heights: %d of %d trees (%d fetched, %d from cache)",
                 with_height, len(assets), len(missing), len(assets) - len(missing))
        return SnapshotFile(
            content=json.dumps({
                "source": self.wcs_url,
                "coverage": COVERAGE_ID,
                "method": {"percentile": 0.95, "radius_m": self.radius_m},
                "heights_by_source_hash": {a.source_hash: a.attributes["height_m"] for a in assets},
            }, sort_keys=True, separators=(",", ":")).encode(),
            extension="json",
        )

    # --- fetching ------------------------------------------------------------

    def _fetch_missing(self, cache: sqlite3.Connection, missing: list[int],
                       positions: list[tuple[float, float]], keys: list[str],
                       heights: dict[str, float | None]) -> None:
        cells: dict[tuple[int, int], list[int]] = defaultdict(list)
        for i in missing:
            x, y = positions[i]
            cells[(math.floor(x / self.cell_m), math.floor(y / self.cell_m))].append(i)
        log.info("nDOM: fetching %d cells for %d trees", len(cells), len(missing))

        margin = math.ceil(self.radius_m) + 1
        failures: list[str] = []
        done = 0
        with ThreadPoolExecutor(max_workers=self.concurrency) as pool:
            futures = {
                pool.submit(self._fetch_with_retry,
                            cx * self.cell_m - margin, cy * self.cell_m - margin,
                            (cx + 1) * self.cell_m + margin, (cy + 1) * self.cell_m + margin): (cx, cy)
                for cx, cy in cells
            }
            for future in as_completed(futures):
                cell = futures[future]
                try:
                    raster = future.result()
                except Exception as exc:  # collected; successful cells are still cached
                    failures.append(f"cell {cell}: {exc}")
                    continue
                now = time.time()
                rows = []
                for i in cells[cell]:
                    height = sample_height(raster, *positions[i], self.radius_m)
                    heights[keys[i]] = height
                    rows.append((keys[i], height, now))
                cache.executemany("INSERT OR REPLACE INTO heights VALUES (?, ?, ?)", rows)
                done += 1
                if done % 500 == 0:
                    cache.commit()
                    log.info("nDOM: %d / %d cells", done, len(cells))
        cache.commit()
        if failures:
            raise EnricherError(
                f"nDOM: {len(failures)} of {len(cells)} cells failed (the others are cached, "
                f"the next sync resumes). First error: {failures[0]}"
            )

    def _fetch_with_retry(self, x0: float, y0: float, x1: float, y1: float) -> Raster:
        for attempt in range(self.retries + 1):
            try:
                return self._fetch_cell(x0, y0, x1, y1)
            except (OSError, GeoTiffError):
                if attempt == self.retries:
                    raise
                time.sleep(2**attempt)
        raise AssertionError("unreachable")

    def _fetch_cell_from_wcs(self, x0: float, y0: float, x1: float, y1: float) -> Raster:
        query = urllib.parse.urlencode([
            ("SERVICE", "WCS"), ("VERSION", "2.0.1"), ("REQUEST", "GetCoverage"),
            ("COVERAGEID", COVERAGE_ID), ("FORMAT", "image/tiff"),
            ("SUBSET", f"x({x0:.0f},{x1:.0f})"), ("SUBSET", f"y({y0:.0f},{y1:.0f})"),
        ])
        request = urllib.request.Request(f"{self.wcs_url}?{query}", headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(request, timeout=self.timeout_s) as response:
            return read_geotiff(response.read())

    # --- cache ---------------------------------------------------------------

    def _cache_key(self, x: float, y: float) -> str:
        # Position to 0.1 m plus the method, so changed options don't reuse old values.
        return f"{x:.1f}_{y:.1f}_r{self.radius_m:g}"

    def _open_cache(self) -> sqlite3.Connection:
        if self.cache_dir is None:
            conn = sqlite3.connect(":memory:")
        else:
            self.cache_dir.mkdir(parents=True, exist_ok=True)
            conn = sqlite3.connect(self.cache_dir / "de_nrw_ndom_heights.sqlite")
        conn.execute("CREATE TABLE IF NOT EXISTS heights (key TEXT PRIMARY KEY, height REAL, fetched_at REAL)")
        return conn

    def _cached(self, cache: sqlite3.Connection, keys: set[str]) -> dict[str, float | None]:
        cutoff = time.time() - self.max_age_s
        return {
            key: height
            for key, height, fetched_at in cache.execute("SELECT key, height, fetched_at FROM heights")
            if key in keys and fetched_at >= cutoff
        }
