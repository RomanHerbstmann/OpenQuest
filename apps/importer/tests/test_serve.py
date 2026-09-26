"""Sync requests from the API: the importer claims them, runs the sync and writes the outcome back."""

import threading
import time
import uuid
from pathlib import Path

import psycopg
import pytest

from openquest_importer.config import Config, SourceConfig, SyncOptions
from openquest_importer.serve import (
    ALL_SOURCES, claim_request, fail_interrupted_requests, process_pending, serve,
)
from openquest_importer.snapshots import LocalSnapshotStore
from tests.conftest import sample_options


def make_config(db_url, tmp_path, sample_collection, write_collection, **overrides) -> Config:
    def source(key, options, enabled=True):
        return SourceConfig(key=key, adapter="de_muenster.trees", name=key, options=options, enabled=enabled,
                            sync=SyncOptions(match_radius_m=1.0, max_removal_ratio=0.2))

    main = sample_options(write_collection(sample_collection, "main.geojson"))
    off = sample_options(write_collection(sample_collection, "off.geojson"))
    return Config(database_url=db_url, snapshot_dir=tmp_path / "snaps",
                  sources={"trees": source("trees", main), "other": source("other", off, enabled=False)}, **overrides)


@pytest.fixture
def store(tmp_path):
    return LocalSnapshotStore(tmp_path / "snaps")


def add_request(conn, key="trees", force=False, accept=False, status="pending"):
    admin = uuid.uuid4()
    conn.execute("INSERT INTO \"user\" (id, username, password_hash, role, locale, total_points, created_at) VALUES (%s, %s, 'x', 'admin', 'de', 0, now())",
                 (admin, "admin-" + admin.hex[:8]))
    request_id = uuid.uuid4()
    conn.execute(
        "INSERT INTO sync_request (id, data_source_key, force, accept_schema_change, requested_by, requested_at, status)"
        " VALUES (%s, %s, %s, %s, %s, now(), %s)", (request_id, key, force, accept, admin, status))
    conn.commit()
    return request_id


def state(conn, request_id):
    row = conn.execute("SELECT status, sync_run_id, error, started_at IS NOT NULL, finished_at IS NOT NULL FROM sync_request WHERE id = %s",
                       (request_id,)).fetchone()
    conn.commit()
    return row


def test_a_request_for_one_source_is_run_and_records_its_run(db_url, tmp_path, sample_collection, write_collection, store):
    config = make_config(db_url, tmp_path, sample_collection, write_collection)
    with psycopg.connect(db_url) as conn:
        request_id = add_request(conn)
        assert process_pending(conn, config, store) == 1

        status, run_id, error, started, finished = state(conn, request_id)
        assert (status, error, started, finished) == ("succeeded", None, True, True)
        assert conn.execute("SELECT status, assets_created FROM sync_run WHERE id = %s", (run_id,)).fetchone() == ("succeeded", 12)
        assert process_pending(conn, config, store) == 0   # nothing left


def test_a_request_for_all_runs_the_enabled_sources_only(db_url, tmp_path, sample_collection, write_collection, store):
    config = make_config(db_url, tmp_path, sample_collection, write_collection)
    with psycopg.connect(db_url) as conn:
        request_id = add_request(conn, key=ALL_SOURCES)
        process_pending(conn, config, store)
        status, run_id, _, _, _ = state(conn, request_id)
        assert status == "succeeded"
        assert conn.execute("SELECT count(*) FROM sync_run").fetchone()[0] == 1   # "other" is disabled
        assert conn.execute("SELECT d.key FROM sync_run r JOIN data_source d ON d.id = r.data_source_id").fetchone()[0] == "trees"
        assert run_id is not None   # only one source ran, so the request points to its run


def test_a_named_source_is_run_even_when_it_is_disabled(db_url, tmp_path, sample_collection, write_collection, store):
    config = make_config(db_url, tmp_path, sample_collection, write_collection)
    with psycopg.connect(db_url) as conn:
        request_id = add_request(conn, key="other")
        process_pending(conn, config, store)
        assert state(conn, request_id)[0] == "succeeded"


def test_an_unknown_source_fails_the_request_and_says_which(db_url, tmp_path, sample_collection, write_collection, store):
    config = make_config(db_url, tmp_path, sample_collection, write_collection)
    with psycopg.connect(db_url) as conn:
        request_id = add_request(conn, key="nowhere")
        process_pending(conn, config, store)
        status, run_id, error, _, finished = state(conn, request_id)
        assert (status, run_id, finished) == ("failed", None, True)
        assert "nowhere: unknown source" in error and "trees" in error


def test_a_sync_that_the_guard_stops_fails_the_request_and_force_lets_the_next_one_through(db_url, tmp_path, sample_collection, write_collection, store):
    config = make_config(db_url, tmp_path, sample_collection, write_collection)
    with psycopg.connect(db_url) as conn:
        add_request(conn)
        process_pending(conn, config, store)
        # the source now delivers only two of the trees: the removal guard stops it
        shrunk = {**sample_collection, "features": sample_collection["features"][:2]}
        config = make_config(db_url, tmp_path, shrunk, write_collection)
        stopped = add_request(conn)
        process_pending(conn, config, store)
        status, _, error, _, _ = state(conn, stopped)
        assert status == "failed" and "would remove" in error
        assert conn.execute("SELECT count(*) FROM asset WHERE status = 'active'").fetchone()[0] == 12

        forced = add_request(conn, force=True)
        process_pending(conn, config, store)
        assert state(conn, forced)[0] == "succeeded"
        assert conn.execute("SELECT count(*) FROM asset WHERE status = 'active'").fetchone()[0] == 2


def test_requests_are_claimed_once_and_oldest_first(db_url):
    with psycopg.connect(db_url) as conn:
        first = add_request(conn, key="a")
        second = add_request(conn, key="b")
        claimed = claim_request(conn)
        assert claimed[0] == first and claimed[1] == "a"
        assert state(conn, first)[0] == "running"
        assert claim_request(conn)[0] == second
        assert claim_request(conn) is None


def test_requests_that_were_running_when_the_importer_stopped_are_failed(db_url):
    with psycopg.connect(db_url) as conn:
        stuck = add_request(conn, key="a", status="running")
        waiting = add_request(conn, key="b")
        assert fail_interrupted_requests(conn) == 1
        assert state(conn, stuck)[0] == "failed" and "stopped" in state(conn, stuck)[2]
        assert state(conn, waiting)[0] == "pending"


def test_serve_answers_a_request_as_soon_as_the_notification_arrives(db_url, tmp_path, sample_collection, write_collection, store):
    config = make_config(db_url, tmp_path, sample_collection, write_collection)
    stop = threading.Event()
    worker = threading.Thread(target=serve, args=(config, store), kwargs={"interval_seconds": 0, "stop": stop.is_set}, daemon=True)
    worker.start()
    try:
        time.sleep(1)   # it waits for a notification now; nothing is scheduled
        with psycopg.connect(db_url) as conn:
            request_id = add_request(conn)
            conn.execute("SELECT pg_notify('sync_requested', %s)", (str(request_id),))
            conn.commit()
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline and state(conn, request_id)[0] != "succeeded":
                time.sleep(0.2)
            assert state(conn, request_id)[0] == "succeeded"
    finally:
        stop.set()
        # the loop ends after its current wait; a notification wakes it up
        with psycopg.connect(db_url, autocommit=True) as conn:
            conn.execute("SELECT pg_notify('sync_requested', 'stop')")
        worker.join(timeout=15)
    assert not worker.is_alive()
