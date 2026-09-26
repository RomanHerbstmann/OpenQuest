"""End-to-end syncs against a real PostGIS database (skipped without one)."""

import psycopg
import pytest

from openquest_importer.adapters.de_muenster.trees import MuensterTreesAdapter
from openquest_importer.config import SourceConfig, SyncOptions
from openquest_importer.snapshots import LocalSnapshotStore
from openquest_importer.sync import RemovalGuardError, SchemaChangedError, run_sync
from tests.conftest import sample_options

ONE_METRE_LAT = 1 / 111_320


def source(max_removal_ratio=0.2):
    return SourceConfig(key="test-trees", adapter="de_muenster.trees", name="Test trees",
                        sync=SyncOptions(match_radius_m=1.0, max_removal_ratio=max_removal_ratio))


def sync(conn, path, store, **kwargs):
    cfg = kwargs.pop("cfg", source())
    return run_sync(conn, cfg, MuensterTreesAdapter(sample_options(path)), store, **kwargs)


def assets(conn):
    return {row[0]: row[1:] for row in conn.execute(
        "SELECT id, status, attributes->>'genus', ST_Y(geom::geometry) FROM asset")}


@pytest.fixture
def store(tmp_path):
    return LocalSnapshotStore(tmp_path / "snapshots")


def test_first_sync_imports_all_trees(db_url, sample_collection, write_collection, store):
    with psycopg.connect(db_url) as conn:
        report = sync(conn, write_collection(sample_collection), store)
        assert (report.record_count, report.created, report.updated, report.removed) == (12, 12, 0, 0)

        status, snapshot_key, record_count, created = conn.execute(
            "SELECT status, snapshot_key, record_count, assets_created FROM sync_run").fetchone()
        assert (status, record_count, created) == ("succeeded", 12, 12)
        snapshot = store.load(snapshot_key)
        assert snapshot.content == write_collection(sample_collection).read_bytes()
        assert set(snapshot.extras) == {"streets", "districts", "quarters"}

        assert conn.execute("SELECT count(*) FROM asset_snapshot WHERE change_type = 'created'").fetchone()[0] == 12
        placeholder = conn.execute(
            "SELECT count(*) FROM asset WHERE attributes->'quality_flags' ? 'placeholder_genus'").fetchone()[0]
        assert placeholder == 4


def test_resync_keeps_ids_and_records_changes(db_url, sample_collection, write_collection, store):
    with psycopg.connect(db_url) as conn:
        sync(conn, write_collection(sample_collection, "v1.geojson"), store)
        before = assets(conn)

        features = sample_collection["features"]
        features[0]["geometry"]["coordinates"][1] += 0.3 * ONE_METRE_LAT   # moved 30 cm
        features[4]["properties"]["baumgruppe"] = "Tilia"                   # genus found
        removed_feature = features.pop(2)                                   # tree gone
        features.append({"type": "Feature", "properties": {"str_schl": "09999", "baumgruppe": "Acer"},
                         "geometry": {"type": "Point", "coordinates": [7.7, 51.99]}})
        report = sync(conn, write_collection(sample_collection, "v2.geojson"), store)

        assert (report.created, report.updated, report.unchanged, report.removed) == (1, 2, 9, 1)
        after = assets(conn)
        assert set(before) <= set(after)  # no asset lost its id
        assert sum(1 for v in after.values() if v[0] == "removed_at_source") == 1
        assert sum(1 for v in after.values() if v[1] == "Tilia") == 3

        changes = dict(conn.execute(
            "SELECT change_type, count(*) FROM asset_snapshot s JOIN sync_run r ON r.id = s.sync_run_id"
            " WHERE r.started_at = (SELECT max(started_at) FROM sync_run) GROUP BY change_type"))
        assert changes == {"created": 1, "updated": 2, "removed": 1}
        gone_raw = conn.execute(
            "SELECT raw->'properties' FROM asset_snapshot WHERE change_type = 'removed'").fetchone()[0]
        assert gone_raw == removed_feature["properties"]


def test_unchanged_resync_writes_no_history(db_url, sample_collection, write_collection, store):
    with psycopg.connect(db_url) as conn:
        path = write_collection(sample_collection)
        sync(conn, path, store)
        report = sync(conn, path, store)
        assert (report.created, report.updated, report.unchanged, report.removed) == (0, 0, 12, 0)
        assert conn.execute("SELECT count(*) FROM asset_snapshot").fetchone()[0] == 12


def test_schema_change_fails_loudly(db_url, sample_collection, write_collection, store):
    with psycopg.connect(db_url) as conn:
        sample_collection["features"][0]["properties"]["pflanzjahr"] = "1990"
        with pytest.raises(SchemaChangedError, match="pflanzjahr"):
            sync(conn, write_collection(sample_collection), store)
        status, error = conn.execute("SELECT status, error FROM sync_run").fetchone()
        assert status == "failed" and "pflanzjahr" in error
        assert conn.execute("SELECT count(*) FROM asset").fetchone()[0] == 0


def test_removal_guard_protects_against_truncated_download(db_url, sample_collection, write_collection, store):
    with psycopg.connect(db_url) as conn:
        sync(conn, write_collection(sample_collection, "full.geojson"), store)
        sample_collection["features"] = sample_collection["features"][:3]
        truncated = write_collection(sample_collection, "truncated.geojson")

        with pytest.raises(RemovalGuardError):
            sync(conn, truncated, store)
        assert conn.execute("SELECT count(*) FROM asset WHERE status = 'active'").fetchone()[0] == 12

        report = sync(conn, truncated, store, force=True)
        assert report.removed == 9


def test_identical_downloads_share_one_snapshot_file(db_url, sample_collection, write_collection, store):
    with psycopg.connect(db_url) as conn:
        path = write_collection(sample_collection)
        sync(conn, path, store)
        sync(conn, path, store)
        keys = [row[0] for row in conn.execute("SELECT snapshot_key FROM sync_run")]
        assert len(keys) == 2 and keys[0] == keys[1]
        # trees, streets, districts, quarters and the manifest, each stored once
        assert len([p for p in store.root.rglob("*") if p.is_file()]) == 5


class FakeHeightEnricher:
    """Sets height_m from latitude, so the test sees which values were applied."""

    attributes = frozenset({"height_m"})

    def enrich(self, assets):
        from openquest_importer.adapters.base import SnapshotFile

        for asset in assets:
            asset.attributes["height_m"] = round((asset.lat - 51.9) * 100, 1)
        return SnapshotFile(content=b'{"fake": true}', extension="json")


def test_enrichers_set_attributes_and_are_stored_in_snapshot(db_url, sample_collection, write_collection, store):
    with psycopg.connect(db_url) as conn:
        run_sync(conn, source(), MuensterTreesAdapter(sample_options(write_collection(sample_collection))), store,
                 enrichers=[("fake.height", FakeHeightEnricher())])
        heights = [r[0] for r in conn.execute("SELECT (attributes->>'height_m')::float FROM asset")]
        assert len(heights) == 12 and all(h is not None for h in heights)
        key = conn.execute("SELECT snapshot_key FROM sync_run").fetchone()[0]
        assert store.load(key).extras["enrichment:fake.height"].content == b'{"fake": true}'


def test_assets_get_own_id_as_external_id(db_url, sample_collection, write_collection, store):
    """Münster has no ids; external_id (required and unique in the API's schema) is the asset's own id."""
    with psycopg.connect(db_url) as conn:
        sync(conn, write_collection(sample_collection), store)
        rows = conn.execute("SELECT id::text, external_id FROM asset").fetchall()
        assert rows and all(asset_id == external_id for asset_id, external_id in rows)

        sample_collection["features"][0]["properties"]["baumgruppe"] = "Acer"  # changed record keeps its id
        sync(conn, write_collection(sample_collection, "v2.geojson"), store)
        assert sorted(conn.execute("SELECT id::text, external_id FROM asset").fetchall()) == sorted(rows)


def test_database_prepared_by_api_is_accepted(db_url):
    from openquest_importer.db import schema_problems

    with psycopg.connect(db_url) as conn:
        assert schema_problems(conn) == []
