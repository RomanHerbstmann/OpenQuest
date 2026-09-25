import json

import pytest

from openquest_importer.adapters.base import AdapterError, Snapshot, SnapshotFile
from openquest_importer.adapters.de_muenster.reference import parse_districts, parse_street_names
from openquest_importer.adapters.de_muenster.trees import MuensterTreesAdapter
from openquest_importer.geo import AreaIndex
from openquest_importer.snapshots import LocalSnapshotStore
from tests.conftest import DISTRICTS_SAMPLE, SAMPLE, STREETS_SAMPLE, sample_options


def square(x1, y1, x2, y2):
    return [[x1, y1], [x2, y1], [x2, y2], [x1, y2], [x1, y1]]


# --- AreaIndex -------------------------------------------------------------

def test_area_index_finds_containing_area():
    index = AreaIndex([
        ("west", {"type": "Polygon", "coordinates": [square(0, 0, 1, 1)]}),
        ("east", {"type": "MultiPolygon", "coordinates": [[square(1, 0, 2, 1)], [square(5, 5, 6, 6)]]}),
    ])
    assert len(index) == 2
    assert index.find(0.5, 0.5) == "west"
    assert index.find(1.5, 0.5) == "east"
    assert index.find(5.5, 5.5) == "east"
    assert index.find(3, 3) is None


def test_area_index_respects_holes():
    ring_with_hole = {"type": "Polygon", "coordinates": [square(0, 0, 10, 10), square(4, 4, 6, 6)]}
    index = AreaIndex([("donut", ring_with_hole)])
    assert index.find(1, 1) == "donut"
    assert index.find(5, 5) is None


def test_area_index_handles_concave_polygons_and_open_rings():
    # U shape, ring not explicitly closed
    u_shape = [[0, 0], [3, 0], [3, 3], [2, 3], [2, 1], [1, 1], [1, 3], [0, 3]]
    index = AreaIndex([("u", {"type": "Polygon", "coordinates": [u_shape]})], strips=4)
    assert index.find(0.5, 2) == "u"
    assert index.find(2.5, 2) == "u"
    assert index.find(1.5, 2) is None  # inside the notch


def test_area_index_rejects_other_geometries():
    with pytest.raises(ValueError, match="Polygon"):
        AreaIndex([("x", {"type": "Point", "coordinates": [0, 0]})])


# --- Münster reference data ------------------------------------------------

def test_parse_street_names_pads_keys():
    names = parse_street_names(STREETS_SAMPLE.read_bytes())
    assert names["02505"] == "Grevener Straße"
    assert parse_street_names("STR_SCHL,NAME\n3120,Hohe Geist\n".encode())["03120"] == "Hohe Geist"


def test_parse_street_names_fails_on_changed_format():
    with pytest.raises(AdapterError, match="missing fields"):
        parse_street_names(b"SCHLUESSEL,STRASSE\n02505,Grevener Stra\xc3\x9fe\n")


def test_parse_districts():
    index = parse_districts(DISTRICTS_SAMPLE.read_bytes())
    assert index.find(7.62, 51.96) == "Mitte"
    assert index.find(7.64, 51.97) == "Münster-Ost"


def test_parse_districts_fails_without_name():
    data = json.loads(DISTRICTS_SAMPLE.read_text(encoding="utf-8"))
    del data["features"][0]["properties"]["NAME_STADT"]
    with pytest.raises(AdapterError, match="NAME_STADT"):
        parse_districts(json.dumps(data).encode())


# --- Adapter ---------------------------------------------------------------

def test_trees_are_enriched():
    adapter = MuensterTreesAdapter(sample_options())
    by_key = {}
    for asset in adapter.parse(adapter.fetch()).assets:
        by_key.setdefault(asset.attributes["street_key"], set()).add(
            (asset.attributes["street_name"], asset.attributes["district"]))
    assert by_key["02505"] == {("Grevener Straße", "Mitte")}
    quarters = {a.attributes["quarter"] for a in adapter.parse(adapter.fetch()).assets}
    assert quarters == {"Uppenberg", "Überwasser", "Schützenhof", "Schlachthof", "Rumphorst"}
    assert by_key["04400"] == {("Ludgeristraße", "Münster-Ost")}
    assert by_key["01234"] == {(None, "Mitte")}  # key not in the street list
    assert by_key[None] == {(None, "Mitte")}      # tree without street key


def test_fetch_includes_reference_data_in_snapshot():
    snapshot = MuensterTreesAdapter(sample_options()).fetch()
    assert snapshot.extras["streets"].content == STREETS_SAMPLE.read_bytes()
    assert snapshot.extras["districts"].extension == "geojson"
    assert set(snapshot.extras) == {"streets", "districts", "quarters"}


def test_enrichment_can_be_disabled():
    adapter = MuensterTreesAdapter({"file": str(SAMPLE), "enrich": False})
    snapshot = adapter.fetch()
    assert not snapshot.extras
    attributes = adapter.parse(snapshot).assets[0].attributes
    assert attributes["street_name"] is None and attributes["district"] is None and attributes["quarter"] is None


def test_snapshot_without_reference_data_is_rejected():
    snapshot = Snapshot(content=SAMPLE.read_bytes(), extension="geojson")
    with pytest.raises(AdapterError, match="reference data"):
        MuensterTreesAdapter().parse(snapshot)


# --- Snapshot store with extra files -----------------------------------------

def test_store_round_trips_snapshot_with_extras(tmp_path):
    store = LocalSnapshotStore(tmp_path)
    snapshot = Snapshot(content=b'{"a": 1}', extension="geojson",
                        extras={"streets": SnapshotFile(content=b"STR_SCHL,NAME\n", extension="csv")})
    key = store.save("src", snapshot)
    assert key.endswith(".manifest.json")
    assert store.load(key) == snapshot
    assert store.save("src", snapshot) == key  # identical snapshot, same key

    plain = Snapshot(content=b"x", extension="geojson")
    assert store.load(store.save("src", plain)) == plain
