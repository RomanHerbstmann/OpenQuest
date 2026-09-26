"""Syncs of reports, readings and the new asset sources against PostGIS (skipped without one)."""

import json

import psycopg
import pytest

from openquest_importer.adapters.de_muenster.natural_monuments import MuensterNaturalMonumentsAdapter
from openquest_importer.adapters.de_muenster.trees import MuensterTreesAdapter
from openquest_importer.adapters.de_nrw.strassen_trees import StrassenNrwTreesAdapter
from openquest_importer.adapters.dwd.soil import DwdSoilDailyAdapter
from openquest_importer.adapters.open311.reports import Open311ReportsAdapter
from openquest_importer.config import SourceConfig
from openquest_importer.enrichers.geo_areas import AreaNameEnricher
from openquest_importer.snapshots import LocalSnapshotStore
from openquest_importer.sync import SyncError, run_sync
from tests.conftest import DISTRICTS_SAMPLE, QUARTERS_SAMPLE, sample_options
from tests.test_more_sources import OPEN311, dwd_options, monument_options, open311_options, strassen_options


@pytest.fixture
def store(tmp_path):
    return LocalSnapshotStore(tmp_path / "snapshots")


def source(key, adapter, **options):
    return SourceConfig(key=key, adapter=adapter, name=key, options=options)


def import_city_trees(conn, store):
    run_sync(conn, source("trees", "de_muenster.trees"), MuensterTreesAdapter(sample_options()), store)


def test_reports_are_created_linked_and_updated(db_url, store, tmp_path):
    with psycopg.connect(db_url) as conn:
        import_city_trees(conn, store)
        cfg = source("reports", "open311.reports", link_radius_m=25, **open311_options())
        report = run_sync(conn, cfg, Open311ReportsAdapter(cfg.options), store)
        assert (report.record_count, report.created, report.updated) == (4, 4, 0)

        rows = {r[0]: r[1:] for r in conn.execute(
            "SELECT r.external_id, r.category, r.status, a.attributes->>'genus', r.distance_m, r.description"
            " FROM asset_report r LEFT JOIN asset a ON a.id = r.asset_id")}
        # 9001 is ~7 m from the first Grevener Straße lime tree, 9003 far away from every tree.
        assert rows["9001"][:3] == ("tree_damage", "open", "Tilia") and rows["9001"][3] < 25
        assert rows["9003"][2] is None and rows["9003"][3] is None
        assert rows["9004"][0] == "oak_processionary_moth"
        assert "example.org" not in rows["9001"][4]

        # Same feed again: nothing changes. Then the city closes 9001.
        again = run_sync(conn, cfg, Open311ReportsAdapter(cfg.options), store)
        assert (again.created, again.updated, again.unchanged) == (0, 0, 4)
        bundle = json.loads(OPEN311.read_text(encoding="utf-8"))
        bundle["1001742"][0]["status"] = "closed"
        bundle["1001742"][0]["status_notes"] = "Ast entfernt."
        changed = tmp_path / "open311.json"
        changed.write_text(json.dumps(bundle), encoding="utf-8")
        cfg2 = source("reports", "open311.reports", link_radius_m=25, file=str(changed), services=cfg.options["services"])
        third = run_sync(conn, cfg2, Open311ReportsAdapter(cfg2.options), store)
        assert (third.created, third.updated, third.unchanged) == (0, 1, 3)
        assert conn.execute("SELECT status, status_notes FROM asset_report WHERE external_id = '9001'").fetchone() == (
            "closed", "Ast entfernt.")
        assert conn.execute("SELECT count(*) FROM asset_report").fetchone()[0] == 4


def test_readings_are_upserted(db_url, store):
    with psycopg.connect(db_url) as conn:
        cfg = source("dwd", "dwd.soil_daily", **dwd_options())
        first = run_sync(conn, cfg, DwdSoilDailyAdapter(cfg.options), store)
        assert (first.record_count, first.created, first.updated) == (15, 15, 0)
        second = run_sync(conn, cfg, DwdSoilDailyAdapter(cfg.options), store)
        assert (second.created, second.updated, second.unchanged) == (0, 0, 15)
        value, unit, has_geom = conn.execute(
            "SELECT value, unit, geom IS NOT NULL FROM environment_reading"
            " WHERE metric = 'soil_moisture_grass_sand_0_60cm' ORDER BY measured_at DESC LIMIT 1").fetchone()
        assert unit == "%nFK" and has_geom and 0 <= value <= 120
        status, created = conn.execute(
            "SELECT status, assets_created FROM sync_run ORDER BY started_at LIMIT 1").fetchone()
        assert (status, created) == ("succeeded", 15)


def test_strassen_trees_with_area_enrichers(db_url, store):
    with psycopg.connect(db_url) as conn:
        cfg = source("nrw-trees", "de_nrw.strassen_trees", **strassen_options())
        enrichers = [
            ("geo.area_name", AreaNameEnricher({"file": str(DISTRICTS_SAMPLE), "name_field": "NAME_STADT", "attribute": "district"})),
            ("geo.area_name", AreaNameEnricher({"file": str(QUARTERS_SAMPLE), "name_field": "NAME_STATI", "attribute": "quarter"})),
        ]
        report = run_sync(conn, cfg, StrassenNrwTreesAdapter(cfg.options), store, enrichers=enrichers)
        assert report.created == 4
        rows = dict(conn.execute("SELECT attributes->>'genus_raw', attributes->>'district' FROM asset").fetchall())
        assert rows["Linde"] == "Mitte" and rows["Eiche"] == "Münster-Ost"
        key = conn.execute("SELECT snapshot_key FROM sync_run").fetchone()[0]
        assert {"enrichment:geo.area_name", "enrichment:geo.area_name:2", "boundary"} <= set(store.load(key).extras)


def test_natural_monuments_use_their_register_number(db_url, store):
    with psycopg.connect(db_url) as conn:
        cfg = source("monuments", "de_muenster.natural_monuments", **monument_options())
        report = run_sync(conn, cfg, MuensterNaturalMonumentsAdapter(cfg.options), store)
        assert report.created == 2
        rows = dict(conn.execute(
            "SELECT a.external_id, (a.attributes->>'height_m')::float FROM asset a"
            " JOIN asset_type t ON t.id = a.asset_type_id WHERE t.key = 'natural_monument'").fetchall())
        assert rows == {"901": 30.5, "902": 24.0}


def test_enrichers_are_refused_for_reports(db_url, store):
    with psycopg.connect(db_url) as conn:
        cfg = source("reports", "open311.reports", **open311_options())
        with pytest.raises(SyncError, match="Enrichers only work on assets"):
            run_sync(conn, cfg, Open311ReportsAdapter(cfg.options), store,
                     enrichers=[("geo.area_name", AreaNameEnricher({"file": str(QUARTERS_SAMPLE), "name_field": "NAME_STATI", "attribute": "quarter"}))])
