"""Straßen.NRW trees, natural monuments, Open311 reports, DWD soil readings, avenues and areas (no database)."""

import json
from datetime import datetime, timezone

import pytest

from openquest_importer.adapters import create_adapter
from openquest_importer.adapters.base import AdapterError, NormalizedAsset, Snapshot
from openquest_importer.adapters.de_common.genera import genera_in_text, genus_for_common_name
from openquest_importer.adapters.de_muenster.natural_monuments import (
    MuensterNaturalMonumentsAdapter,
    parse_measure,
)
from openquest_importer.adapters.de_nrw.strassen_trees import StrassenNrwTreesAdapter
from openquest_importer.adapters.dwd.soil import DwdSoilDailyAdapter
from openquest_importer.adapters.open311.reports import Open311ReportsAdapter, redact
from openquest_importer.enrichers import create_enricher
from openquest_importer.enrichers.base import EnricherError
from openquest_importer.enrichers.de_nrw.alleen import AlleenEnricher, parse_avenues
from openquest_importer.geo import SegmentIndex, from_utm, to_utm
from tests.conftest import DISTRICTS_SAMPLE, FIXTURES, QUARTERS_SAMPLE

STRASSEN = FIXTURES / "strassen_nrw_trees_sample.csv"
OPEN311 = FIXTURES / "open311_sample.json"
DWD = FIXTURES / "dwd_soil_1766_sample.txt.gz"
DWD_STATIONS = FIXTURES / "dwd_soil_stations_sample.txt"
ALLEEN = FIXTURES / "alleen_sample.gml"
MONUMENTS = FIXTURES / "natural_monuments_sample.json"
SERVICES = {"1001742": "tree_damage", "1001746": "oak_processionary_moth"}

TREE_A = (7.612346614095637, 51.974634186495123)  # Grevener Straße


def asset(lon, lat):
    return NormalizedAsset(lon=lon, lat=lat, attributes={}, raw={}, source_hash="h")


def strassen_options():
    return {"file": str(STRASSEN), "boundary_file": str(DISTRICTS_SAMPLE)}


def open311_options():
    return {"file": str(OPEN311), "services": SERVICES}


def dwd_options():
    return {"station_id": "1766", "file": str(DWD), "stations_file": str(DWD_STATIONS)}


def monument_options():
    return {"file": str(MONUMENTS), "boundary_file": str(DISTRICTS_SAMPLE)}


# --- geometry and names ------------------------------------------------------

def test_from_utm_inverts_to_utm():
    for lat, lon in [(51.9746, 7.6123), (52.05, 7.49), (50.94, 6.96)]:
        back_lat, back_lon = from_utm(*to_utm(lat, lon))
        assert back_lat == pytest.approx(lat, abs=1e-8) and back_lon == pytest.approx(lon, abs=1e-8)


def test_segment_index_finds_nearest_line_within_distance():
    index = SegmentIndex([("a", [(0, 0), (100, 0)]), ("b", [(0, 30), (100, 30)])], max_distance=10)
    assert index.nearest(50, 3) == ("a", 3)
    assert index.nearest(50, 26)[0] == "b"
    assert index.nearest(50, 15) is None  # 15 m from both lines
    assert index.nearest(120, 0) is None  # beyond the end of the line


@pytest.mark.parametrize(("name", "expected"), [
    ("Linde", ("Tilia", True)), ("  eiche ", ("Quercus", True)), ("Kastanie", (None, True)),
    ("Obstbaum", (None, True)), ("Wunderbaum", (None, False)),
])
def test_genus_for_common_name(name, expected):
    assert genus_for_common_name(name) == expected


def test_genera_in_text():
    assert genera_in_text("2 Rosskastanien, 1 Blutbuche") == {"Aesculus", "Fagus"}
    assert genera_in_text("3 Stieleichen") == {"Quercus"}
    assert genera_in_text("1 Kastanie") == {None}
    assert genera_in_text(None) == set()
    assert genera_in_text("1 Baumreihe: 8 Zerreichen") == {"Quercus"}
    assert genera_in_text("25 Kopfweiden") == {"Salix"}
    assert genera_in_text("1 Esskastanie") == {"Castanea"}
    assert genera_in_text("1 Sicheltanne") == {"Cryptomeria"}
    assert genera_in_text("1 Urweltmammutbaum") == {"Metasequoia"}
    assert genera_in_text("5 Findlinge") == set()


# --- Straßen.NRW trees -------------------------------------------------------

def test_strassen_trees_are_clipped_and_mapped():
    adapter = create_adapter("de_nrw.strassen_trees", strassen_options())
    assert isinstance(adapter, StrassenNrwTreesAdapter)
    parsed = adapter.parse(adapter.fetch())
    assert parsed.fields == adapter.expected_fields
    by_raw = {a.attributes["genus_raw"]: a for a in parsed.assets}
    assert set(by_raw) == {"Linde", "Kastanie", "Eiche", "Wunderbaum"}  # the tree in Cologne is outside
    assert by_raw["Linde"].attributes == {"genus": "Tilia", "genus_raw": "Linde", "species": None, "quality_flags": []}
    assert by_raw["Kastanie"].attributes["quality_flags"] == ["ambiguous_genus"]
    assert by_raw["Wunderbaum"].attributes["genus"] is None
    assert by_raw["Linde"].lon == pytest.approx(7.62, abs=1e-6) and by_raw["Linde"].lat == pytest.approx(51.965, abs=1e-6)
    assert by_raw["Linde"].raw["Baumart"] == "Linde"


def test_strassen_trees_need_a_boundary():
    with pytest.raises(AdapterError, match="boundary"):
        StrassenNrwTreesAdapter({"file": str(STRASSEN)}).fetch()


# --- natural monuments -------------------------------------------------------

@pytest.mark.parametrize(("text", "value"), [("30,5", 30.5), ("14,5 - 24,0", 24.0), ("ca. 22", 22.0), ("", None), (None, None)])
def test_parse_measure(text, value):
    assert parse_measure(text) == value


def test_monuments_pair_locations_with_records():
    adapter = MuensterNaturalMonumentsAdapter(monument_options())
    parsed = adapter.parse(adapter.fetch())
    assert parsed.fields == adapter.expected_fields
    by_number = {a.external_id: a for a in parsed.assets}
    assert set(by_number) == {"901", "902"}  # 903 lies outside the boundary; 902 appears in two cells
    plane = by_number["901"].attributes
    assert plane["genus"] == "Platanus" and plane["height_m"] == 30.5 and plane["circumference_m"] == 6.12
    assert plane["crown_diameter_m"] == 22.0 and plane["location"] == "Teststraße 1, im Hof."
    group = by_number["902"].attributes
    assert group["genus"] is None and group["quality_flags"] == ["ambiguous_genus"] and group["height_m"] == 24.0
    assert group["historical_context"] is None
    assert by_number["901"].lon == pytest.approx(7.6210, abs=1e-6)


def test_monuments_fail_when_locations_and_records_differ(tmp_path):
    bundle = json.loads(MONUMENTS.read_text(encoding="utf-8"))
    bundle["cells"][0]["html"] = bundle["cells"][0]["html"].replace("Nummer:", "Keine Nummer:", 1)
    path = tmp_path / "bundle.json"
    path.write_text(json.dumps(bundle), encoding="utf-8")
    adapter = MuensterNaturalMonumentsAdapter({**monument_options(), "file": str(path)})
    with pytest.raises(AdapterError, match="same order"):
        adapter.parse(adapter.fetch())


# --- Open311 reports ---------------------------------------------------------

def test_redact_removes_contact_details():
    text = "Rückfragen an max.muster@example.org oder 0251 123456, Tel. +49 251 9876543. Ast 3 m lang."
    cleaned = redact(text)
    assert "example.org" not in cleaned and "123456" not in cleaned and "9876543" not in cleaned
    assert "Ast 3 m lang." in cleaned


def test_open311_reports_are_normalized():
    adapter = create_adapter("open311.reports", open311_options())
    assert isinstance(adapter, Open311ReportsAdapter)
    parsed = adapter.parse(adapter.fetch())
    assert parsed.fields == adapter.expected_fields
    by_id = {r.external_id: r for r in parsed.reports}
    assert set(by_id) == {"9001", "9002", "9003", "9004"}  # 9005 is lighting, not configured
    assert by_id["9001"].category == "tree_damage" and by_id["9001"].status == "open"
    assert "example.org" not in by_id["9001"].description and "[E-Mail entfernt]" in by_id["9001"].description
    assert "example.org" not in json.dumps(by_id["9001"].raw)
    assert by_id["9004"].category == "oak_processionary_moth" and "9876543" not in by_id["9004"].description
    assert by_id["9002"].status_notes == "Wurde beseitigt. Ihre Stadt Münster."
    assert by_id["9001"].reported_at == datetime(2026, 9, 20, 8, 0, tzinfo=timezone.utc)


def test_open311_needs_services():
    with pytest.raises(AdapterError, match="services"):
        Open311ReportsAdapter({"file": str(OPEN311)}).parse(Snapshot(content=OPEN311.read_bytes(), extension="json"))


# --- DWD soil readings ---------------------------------------------------------

def test_dwd_readings():
    adapter = create_adapter("dwd.soil_daily", dwd_options())
    assert isinstance(adapter, DwdSoilDailyAdapter)
    parsed = adapter.parse(adapter.fetch())
    assert parsed.fields == adapter.expected_fields
    sand = [r for r in parsed.readings if r.metric == "soil_moisture_grass_sand_0_60cm"]
    assert len(sand) == 3 and {r.unit for r in sand} == {"%nFK"}
    assert all(r.lat == 52.13 and r.lon == 7.70 and r.station_id == "1766" for r in parsed.readings)
    # The last day has a missing value in BFGL01_AG only, which is not imported anyway: 3 days × 5 metrics.
    assert len(parsed.readings) == 15
    assert max(r.measured_at for r in parsed.readings) == datetime(2026, 9, 25, tzinfo=timezone.utc)


def test_dwd_skips_missing_values():
    adapter = DwdSoilDailyAdapter({**dwd_options(), "metrics": {"BFGL01_AG": ["soil_moisture_grass_loam_0_10cm", "%nFK"]}})
    readings = adapter.parse(adapter.fetch()).readings
    assert len(readings) == 2  # the -999 day is dropped


def test_dwd_needs_station():
    with pytest.raises(AdapterError, match="station_id"):
        DwdSoilDailyAdapter({"file": str(DWD)}).fetch()


# --- enrichers ---------------------------------------------------------------

def test_parse_avenues():
    avenues = parse_avenues(ALLEEN.read_bytes())
    assert [a["id"] for a in avenues] == ["AL-MS-TEST1"]
    assert avenues[0]["name"] == "Testallee Grevener Straße" and len(avenues[0]["lines"][0]) == 2


def test_alleen_enricher_marks_trees_near_the_avenue():
    near, far = asset(*TREE_A), asset(7.6401, 51.9701)
    result = AlleenEnricher({"file": str(ALLEEN), "distance_m": 10}).enrich([near, far])
    assert near.attributes == {"avenue_id": "AL-MS-TEST1", "avenue_name": "Testallee Grevener Straße"}
    assert far.attributes == {"avenue_id": None, "avenue_name": None}
    assert result.extension == "gml"


def test_alleen_enricher_reports_wfs_errors(tmp_path):
    path = tmp_path / "error.xml"
    path.write_text('<ExceptionReport xmlns="http://www.opengis.net/ows/1.1"><Exception/></ExceptionReport>')
    with pytest.raises(EnricherError, match="WFS error"):
        AlleenEnricher({"file": str(path)}).enrich([asset(*TREE_A)])


def test_area_enricher():
    enricher = create_enricher("geo.area_name", {"file": str(QUARTERS_SAMPLE), "name_field": "NAME_STATI",
                                                 "attribute": "quarter"})
    assert enricher.attributes == {"quarter"}
    tree = asset(*TREE_A)
    enricher.enrich([tree])
    assert tree.attributes == {"quarter": "Uppenberg"}


def test_area_enricher_needs_options():
    with pytest.raises(EnricherError, match="name_field"):
        create_enricher("geo.area_name", {"file": str(QUARTERS_SAMPLE)})
