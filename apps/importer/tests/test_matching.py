from uuid import uuid4

import pytest

from openquest_importer.adapters.base import Identity, NormalizedAsset
from openquest_importer.matching import ExistingAsset, MatchingError, match

# About 1 m in latitude around Münster.
ONE_METRE_LAT = 1 / 111_320


def new(lon, lat, hash_, attributes=None, external_id=None):
    return NormalizedAsset(lon=lon, lat=lat, attributes=attributes or {}, raw={}, source_hash=hash_,
                           external_id=external_id)


def old(lon, lat, hash_, attributes=None, external_id=None):
    return ExistingAsset(id=uuid4(), lon=lon, lat=lat, source_hash=hash_, external_id=external_id,
                         attributes=attributes or {})


def test_first_sync_creates_everything():
    incoming = [new(7.6, 51.9, "a"), new(7.7, 51.9, "b")]
    result = match([], incoming, Identity.SPATIAL)
    assert result.created == incoming
    assert not (result.updated or result.unchanged or result.removed)


def test_identical_records_are_unchanged():
    existing = [old(7.6, 51.9, "a"), old(7.7, 51.9, "b")]
    incoming = [new(7.7, 51.9, "b"), new(7.6, 51.9, "a")]
    result = match(existing, incoming, Identity.SPATIAL)
    assert {(o.id, n.source_hash) for o, n in result.unchanged} == {(existing[0].id, "a"), (existing[1].id, "b")}
    assert not (result.created or result.updated or result.removed)


def test_slightly_moved_tree_keeps_its_id():
    tree = old(7.6, 51.9, "a")
    moved = new(7.6, 51.9 + 0.4 * ONE_METRE_LAT, "a-moved")
    result = match([tree], [moved], Identity.SPATIAL, radius_m=1.0)
    assert result.updated == [(tree, moved)]
    assert not (result.created or result.removed)


def test_corrected_genus_at_same_position_keeps_its_id():
    tree = old(7.6, 51.9, "a", {"genus": None})
    corrected = new(7.6, 51.9, "a-corrected", {"genus": "Tilia"})
    result = match([tree], [corrected], Identity.SPATIAL)
    assert result.updated == [(tree, corrected)]


def test_tree_moved_too_far_is_removed_and_created():
    tree = old(7.6, 51.9, "a")
    far = new(7.6, 51.9 + 3 * ONE_METRE_LAT, "a-far")
    result = match([tree], [far], Identity.SPATIAL, radius_m=1.0)
    assert result.removed == [tree]
    assert result.created == [far]


def test_closest_pair_wins_and_each_asset_is_used_once():
    a = old(7.6, 51.9, "a")
    b = old(7.6, 51.9 + 0.9 * ONE_METRE_LAT, "b")
    # Both new records are within 1 m of both old ones; the nearest pairing must be chosen.
    near_a = new(7.6, 51.9 + 0.1 * ONE_METRE_LAT, "x")
    near_b = new(7.6, 51.9 + 0.8 * ONE_METRE_LAT, "y")
    result = match([a, b], [near_b, near_a], Identity.SPATIAL, radius_m=1.0)
    pairs = {(o.id, n.source_hash) for o, n in result.updated}
    assert pairs == {(a.id, "x"), (b.id, "y")}
    assert not (result.created or result.removed)


def test_changed_attributes_with_same_raw_record_update():
    tree = old(7.6, 51.9, "a", {"quality_flags": []})
    same_raw = new(7.6, 51.9, "a", {"quality_flags": ["near_duplicate"]})
    result = match([tree], [same_raw], Identity.SPATIAL)
    assert result.updated == [(tree, same_raw)]


def test_duplicate_identical_records_both_match():
    a1, a2 = old(7.6, 51.9, "dup"), old(7.6, 51.9, "dup")
    result = match([a1, a2], [new(7.6, 51.9, "dup"), new(7.6, 51.9, "dup")], Identity.SPATIAL)
    assert {o.id for o, _ in result.unchanged} == {a1.id, a2.id}


def test_match_by_external_id():
    kept, gone = old(7.6, 51.9, "a", external_id="1"), old(7.7, 51.9, "b", external_id="2")
    moved_far = new(8.0, 52.0, "a2", external_id="1")
    fresh = new(7.8, 51.9, "c", external_id="3")
    result = match([kept, gone], [moved_far, fresh], Identity.EXTERNAL_ID)
    assert result.updated == [(kept, moved_far)]
    assert result.created == [fresh]
    assert result.removed == [gone]


def test_external_id_duplicates_are_rejected():
    with pytest.raises(MatchingError, match="Duplicate"):
        match([], [new(7.6, 51.9, "a", external_id="1"), new(7.7, 51.9, "b", external_id="1")],
              Identity.EXTERNAL_ID)
