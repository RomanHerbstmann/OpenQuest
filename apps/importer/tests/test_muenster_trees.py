import pytest

from openquest_importer.adapters import create_adapter
from openquest_importer.adapters.base import AdapterError, Identity, Snapshot
from openquest_importer.adapters.de_muenster.trees import (
    MuensterTreesAdapter,
    normalize_genus,
    normalize_street_key,
)
from tests.conftest import sample_options


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        ("Tilia", ("Tilia", None, [])),
        ("  Quercus ", ("Quercus", None, [])),
        ("Baum Amt62", (None, None, ["placeholder_genus"])),
        ("Baumgruppe", (None, None, ["placeholder_genus"])),
        ("Standort", (None, None, ["placeholder_genus"])),
        ("Leerer", (None, None, ["placeholder_genus"])),
        ("Leerer Standort", (None, None, ["placeholder_genus"])),
        ("Unbekannt", (None, None, ["placeholder_genus"])),
        ("", (None, None, ["placeholder_genus"])),
        (None, (None, None, ["placeholder_genus"])),
        ("Catalpha", ("Catalpa", None, ["typo_corrected"])),
        ("Cladrastris", ("Cladrastis", None, ["typo_corrected"])),
        ("Malus-Hybride", ("Malus", None, ["typo_corrected"])),
        ("Metasequoia glyptostroboides", ("Metasequoia", "Metasequoia glyptostroboides", [])),
    ],
)
def test_normalize_genus(raw, expected):
    assert normalize_genus(raw) == expected


@pytest.mark.parametrize(
    ("raw", "expected"),
    [("02505", "02505"), ("3120", "03120"), ("", None), (None, None), (" 04400 ", "04400")],
)
def test_normalize_street_key(raw, expected):
    assert normalize_street_key(raw) == expected


def test_registry_finds_adapter():
    adapter = create_adapter("de_muenster.trees", sample_options())
    assert isinstance(adapter, MuensterTreesAdapter)
    assert adapter.identity is Identity.SPATIAL


def test_parse_sample():
    adapter = MuensterTreesAdapter(sample_options())
    parsed = adapter.parse(adapter.fetch())

    assert parsed.fields == frozenset({"str_schl", "baumgruppe"})
    assert len(parsed.assets) == 12

    first = parsed.assets[0]
    assert (first.lon, first.lat) == (7.612346614095637, 51.974634186495123)
    assert first.attributes == {
        "genus": "Tilia",
        "genus_raw": "Tilia",
        "species": None,
        "street_key": "02505",
        "street_name": "Grevener Straße",
        "district": "Mitte",
        "quarter": "Uppenberg",
        "quality_flags": [],
    }
    assert first.raw["properties"] == {"str_schl": "02505", "baumgruppe": "Tilia"}
    assert first.external_id is None

    # Identical records get identical hashes, different ones don't.
    hashes = [a.source_hash for a in parsed.assets]
    assert len(set(hashes)) == len(hashes)


def test_near_duplicates_are_flagged():
    adapter = MuensterTreesAdapter(sample_options())
    assets = adapter.parse(adapter.fetch()).assets
    flagged = [a for a in assets if "near_duplicate" in a.attributes["quality_flags"]]
    # The two Quercus points are 0.5 m apart; everything else is further away.
    assert len(flagged) == 2
    assert {a.attributes["genus"] for a in flagged} == {"Quercus"}


def test_parse_rejects_other_crs(sample_collection):
    sample_collection["crs"]["properties"]["name"] = "urn:ogc:def:crs:EPSG::25832"
    snapshot = Snapshot(content=_dump(sample_collection), extension="geojson")
    with pytest.raises(AdapterError, match="WGS84"):
        MuensterTreesAdapter({"enrich": False}).parse(snapshot)


def test_parse_rejects_non_points(sample_collection):
    sample_collection["features"][0]["geometry"] = {"type": "LineString", "coordinates": [[7, 51], [7.1, 51.1]]}
    with pytest.raises(AdapterError, match="Point"):
        MuensterTreesAdapter({"enrich": False}).parse(Snapshot(content=_dump(sample_collection), extension="geojson"))


def test_parse_rejects_garbage():
    with pytest.raises(AdapterError):
        MuensterTreesAdapter({"enrich": False}).parse(Snapshot(content=b"<html>Service unavailable</html>", extension="geojson"))


def _dump(collection: dict) -> bytes:
    import json

    return json.dumps(collection).encode()
