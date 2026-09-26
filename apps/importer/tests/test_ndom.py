import json
import sqlite3

import pytest

from openquest_importer.adapters.base import NormalizedAsset
from openquest_importer.enrichers import create_enricher
from openquest_importer.enrichers.base import EnricherError
from openquest_importer.enrichers.de_nrw.geotiff import GeoTiffError, read_geotiff
from openquest_importer.enrichers.de_nrw.ndom import NdomHeightEnricher, percentile, sample_height
from openquest_importer.geo import to_utm
from tests.conftest import NDOM_CELL

# Two lime trees on Grevener Straße, inside the fixture cell (EPSG:25832 404682..404736 / 5759122..5759176).
TREE_A = (7.612346614095637, 51.974634186495123)
TREE_B = (7.61253784216682, 51.974672640232157)


def asset(lon, lat, hash_="h"):
    return NormalizedAsset(lon=lon, lat=lat, attributes={}, raw={}, source_hash=hash_)


class CellServer:
    """Serves the fixture cell for every request and counts requests."""

    def __init__(self, fail=False):
        self.raster = read_geotiff(NDOM_CELL.read_bytes())
        self.requests = []
        self.fail = fail

    def __call__(self, x0, y0, x1, y1):
        self.requests.append((x0, y0, x1, y1))
        if self.fail:
            raise OSError("WCS down")
        return self.raster


def test_to_utm_matches_city_wfs():
    # The city WFS returns this tree at 404685.243 / 5759126.262 in EPSG:25832.
    x, y = to_utm(TREE_A[1], TREE_A[0])
    assert x == pytest.approx(404685.243, abs=0.01)
    assert y == pytest.approx(5759126.262, abs=0.01)


def test_read_geotiff_fixture():
    raster = read_geotiff(NDOM_CELL.read_bytes())
    assert (raster.width, raster.height) == (108, 108)
    assert (raster.origin_x, raster.origin_y, raster.res_x, raster.res_y) == (404682.0, 5759176.0, 0.5, -0.5)


def test_read_geotiff_rejects_error_pages():
    with pytest.raises(GeoTiffError, match="Not a TIFF"):
        read_geotiff(b"<ServiceExceptionReport>...</ServiceExceptionReport>")


def test_percentile_ignores_noise():
    assert percentile([1.0, 2.0, 3.0, 4.0, 1000.0, float("nan"), -9999.0], 0.95) == 4.0
    assert percentile([], 0.95) is None


def test_sample_height_from_fixture():
    raster = read_geotiff(NDOM_CELL.read_bytes())
    assert sample_height(raster, *to_utm(TREE_A[1], TREE_A[0]), 2.5) == 11.0
    assert sample_height(raster, *to_utm(TREE_B[1], TREE_B[0]), 2.5) == 15.8


def test_enricher_sets_heights_and_uses_one_request_per_cell(tmp_path):
    server = CellServer()
    enricher = NdomHeightEnricher({}, tmp_path, fetch_cell=server)
    assets = [asset(*TREE_A, "a"), asset(*TREE_B, "b")]
    result = enricher.enrich(assets)

    assert [a.attributes["height_m"] for a in assets] == [11.0, 15.8]
    assert len(server.requests) == 1  # both trees are in the same 50 m cell
    x0, y0, x1, y1 = server.requests[0]
    assert (x0, y0, x1, y1) == (404650 - 4, 5759100 - 4, 404700 + 4, 5759150 + 4)
    assert json.loads(result.content)["heights_by_source_hash"] == {"a": 11.0, "b": 15.8}


def test_enricher_cache_avoids_second_request(tmp_path):
    server = CellServer()
    NdomHeightEnricher({}, tmp_path, fetch_cell=server).enrich([asset(*TREE_A)])
    again = [asset(*TREE_A)]
    NdomHeightEnricher({}, tmp_path, fetch_cell=server).enrich(again)
    assert len(server.requests) == 1
    assert again[0].attributes["height_m"] == 11.0


def test_expired_cache_entries_are_fetched_again(tmp_path):
    server = CellServer()
    NdomHeightEnricher({}, tmp_path, fetch_cell=server).enrich([asset(*TREE_A)])
    with sqlite3.connect(tmp_path / "de_nrw_ndom_heights.sqlite") as db:
        db.execute("UPDATE heights SET fetched_at = 0")
    NdomHeightEnricher({"max_age_days": 30}, tmp_path, fetch_cell=server).enrich([asset(*TREE_A)])
    assert len(server.requests) == 2


def test_failing_wcs_fails_the_enrichment(tmp_path):
    enricher = NdomHeightEnricher({"retries": 0}, tmp_path, fetch_cell=CellServer(fail=True))
    with pytest.raises(EnricherError, match="1 of 1 cells failed"):
        enricher.enrich([asset(*TREE_A)])


def test_registry_finds_enricher(tmp_path):
    enricher = create_enricher("de_nrw.ndom_height", {"radius_m": 3}, tmp_path)
    assert isinstance(enricher, NdomHeightEnricher)
    assert enricher.radius_m == 3.0 and enricher.attributes == {"height_m"}
