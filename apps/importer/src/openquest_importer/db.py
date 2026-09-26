"""Database connection and schema check.

The database schema belongs to the API: its EF Core migrations create the
tables when the API starts, and it seeds the asset types. The importer only
writes the open data tables (``data_source``, ``sync_run``, ``asset``,
``asset_snapshot``) and reads ``asset_type``.
"""

from __future__ import annotations

import logging
import time

import psycopg

log = logging.getLogger(__name__)

#: Tables and columns the importer writes or reads.
REQUIRED_COLUMNS: dict[str, set[str]] = {
    "data_source": {"id", "key", "adapter_key", "name", "city", "source_url", "license", "attribution",
                    "config", "is_active", "created_at"},
    "sync_run": {"id", "data_source_id", "started_at", "finished_at", "status", "snapshot_key", "schema_hash",
                 "record_count", "assets_created", "assets_updated", "assets_removed", "error"},
    "asset_type": {"id", "key", "attribute_schema"},
    "asset": {"id", "asset_type_id", "data_source_id", "external_id", "geom", "attributes", "raw", "source_hash",
              "status", "first_seen_at", "last_seen_at", "updated_at"},
    "asset_snapshot": {"id", "asset_id", "sync_run_id", "change_type", "geom", "raw", "source_hash"},
}


class SchemaNotReadyError(RuntimeError):
    pass


def connect(url: str) -> psycopg.Connection:
    return psycopg.connect(url)


def schema_problems(conn: psycopg.Connection) -> list[str]:
    """What is missing for the importer; empty when the API has set up the database."""
    rows = conn.execute(
        "SELECT table_name, column_name FROM information_schema.columns "
        "WHERE table_schema = current_schema() AND table_name = ANY(%s)",
        (list(REQUIRED_COLUMNS),),
    ).fetchall()
    conn.commit()
    found: dict[str, set[str]] = {}
    for table, column in rows:
        found.setdefault(table, set()).add(column)

    problems = []
    for table, columns in REQUIRED_COLUMNS.items():
        if table not in found:
            problems.append(f"table {table} missing")
        elif missing := columns - found[table]:
            problems.append(f"{table}: columns {sorted(missing)} missing")
    if not problems and conn.execute("SELECT count(*) FROM asset_type").fetchone()[0] == 0:
        problems.append("no asset types seeded")
    conn.commit()
    return problems


def wait_for_schema(url: str, timeout_s: float) -> None:
    """Wait until the API has migrated and seeded the database."""
    deadline = time.monotonic() + timeout_s
    while True:
        try:
            with connect(url) as conn:
                problems = schema_problems(conn)
        except psycopg.OperationalError as exc:
            problems = [f"database not reachable: {exc}".strip()]
        if not problems:
            return
        if time.monotonic() >= deadline:
            raise SchemaNotReadyError(
                "Database schema not ready (" + "; ".join(problems) + "). "
                "The API creates it: start the API once (it migrates and seeds on start)."
            )
        log.info("Waiting for the API to set up the database: %s", "; ".join(problems))
        time.sleep(3)
