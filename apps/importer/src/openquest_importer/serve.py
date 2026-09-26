"""``openquest-importer serve``: syncs on a schedule and whenever an admin asks for it.

The API records a request in ``sync_request`` and sends ``pg_notify('sync_requested', id)``. This process listens for it, runs the
sync and writes the outcome back into the request (status, the run it made, the error). Nothing polls: the wait for a notification is
the timer for the scheduled sync, and it is bounded so that a lost notification only delays a request.
"""

from __future__ import annotations

import logging
import time
from typing import Callable

import psycopg

from openquest_importer.config import Config
from openquest_importer.runner import sync_source
from openquest_importer.snapshots import SnapshotStore

log = logging.getLogger(__name__)

REQUEST_CHANNEL = "sync_requested"
#: Longest wait for a notification: a notification lost while this process was busy costs at most this long.
MAX_WAIT_SECONDS = 300.0
#: The key the API stores for "all enabled sources".
ALL_SOURCES = "*"


def fail_interrupted_requests(conn: psycopg.Connection) -> int:
    """Requests that were running when the importer stopped will never finish: mark them failed so that an admin can ask again."""
    n = conn.execute(
        "UPDATE sync_request SET status = 'failed', finished_at = now(),"
        " error = 'The importer stopped while this request was running.' WHERE status = 'running'"
    ).rowcount
    conn.commit()
    return n


def claim_request(conn: psycopg.Connection):
    """Takes the oldest pending request (marks it running) or returns None."""
    row = conn.execute(
        """
        UPDATE sync_request SET status = 'running', started_at = now()
        WHERE id = (SELECT id FROM sync_request WHERE status = 'pending' ORDER BY requested_at LIMIT 1 FOR UPDATE SKIP LOCKED)
        RETURNING id, data_source_key, force, accept_schema_change
        """
    ).fetchone()
    conn.commit()
    return row


def process_request(conn: psycopg.Connection, config: Config, store: SnapshotStore, request) -> None:
    """Runs one claimed request and records the outcome in it."""
    request_id, source_key, force, accept_schema_change = request
    keys = [k for k, s in config.sources.items() if s.enabled] if source_key == ALL_SOURCES else [source_key]
    run_id, errors = None, []
    for key in keys:
        if key not in config.sources:
            errors.append(f"{key}: unknown source (configured: {', '.join(config.sources) or 'none'})")
            continue
        try:
            report = sync_source(conn, config, store, key, force=force, accept_schema_change=accept_schema_change)
            run_id = report.run_id if len(keys) == 1 else None
        except Exception as exc:  # recorded in sync_run by run_sync; here it goes to the request, too
            log.error("%s: %s", key, exc)
            errors.append(f"{key}: {exc}")
            conn.rollback()
    conn.execute(
        "UPDATE sync_request SET status = %s, finished_at = now(), sync_run_id = %s, error = %s WHERE id = %s",
        ("failed" if errors else "succeeded", run_id, "; ".join(errors)[:2000] or None, request_id),
    )
    conn.commit()


def process_pending(conn: psycopg.Connection, config: Config, store: SnapshotStore) -> int:
    """Runs all pending requests, oldest first. Returns how many were handled."""
    handled = 0
    while (request := claim_request(conn)) is not None:
        process_request(conn, config, store, request)
        handled += 1
    return handled


def sync_all(conn: psycopg.Connection, config: Config, store: SnapshotStore) -> None:
    """The scheduled sync: every enabled source; a failing source is logged and does not stop the others."""
    for key, source in config.sources.items():
        if not source.enabled:
            continue
        try:
            sync_source(conn, config, store, key)
        except Exception as exc:
            log.error("%s: %s", key, exc)
            conn.rollback()


def serve(config: Config, store: SnapshotStore, *, interval_seconds: float,
          initial_sync: bool = True, stop: Callable[[], bool] = lambda: False) -> None:
    """Runs until ``stop()`` says so (only tests do that). ``interval_seconds`` <= 0 means: no scheduled syncs, requests only."""
    assert config.database_url
    with psycopg.connect(config.database_url) as work, psycopg.connect(config.database_url, autocommit=True) as listener:
        listener.execute(f"LISTEN {REQUEST_CHANNEL}")
        fail_interrupted_requests(work)
        next_scheduled = time.monotonic() + (0 if initial_sync else interval_seconds) if interval_seconds > 0 else None
        log.info("Serving: %s; listening for sync requests",
                 f"syncing every {interval_seconds:.0f} s" if interval_seconds > 0 else "no scheduled syncs")
        while not stop():
            process_pending(work, config, store)
            if next_scheduled is not None and time.monotonic() >= next_scheduled:
                sync_all(work, config, store)
                process_pending(work, config, store)
                next_scheduled = time.monotonic() + interval_seconds
            wait = MAX_WAIT_SECONDS if next_scheduled is None else max(0.0, min(MAX_WAIT_SECONDS, next_scheduled - time.monotonic()))
            for _ in listener.notifies(timeout=wait, stop_after=1):
                break
