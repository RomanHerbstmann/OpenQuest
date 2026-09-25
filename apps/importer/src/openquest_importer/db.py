"""Database connection and a minimal SQL migration runner.

Migrations are plain SQL files in ``db/migrations`` at the repository root so
that other parts of OpenQuest (e.g. an API in another language) can share them.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path

import psycopg

log = logging.getLogger(__name__)


def connect(url: str) -> psycopg.Connection:
    return psycopg.connect(url)


def migrations_dir() -> Path:
    override = os.environ.get("OPENQUEST_MIGRATIONS_DIR")
    if override:
        return Path(override)
    # src/openquest_importer/db.py -> repository root
    return Path(__file__).resolve().parents[4] / "db" / "migrations"


def migrate(conn: psycopg.Connection, directory: Path | None = None) -> list[str]:
    """Apply all migrations that haven't run yet, each in its own transaction."""
    directory = directory or migrations_dir()
    if not directory.is_dir():
        raise FileNotFoundError(f"Migrations directory not found: {directory}")

    with conn.transaction():
        conn.execute(
            "CREATE TABLE IF NOT EXISTS schema_migrations ("
            " version text PRIMARY KEY,"
            " applied_at timestamptz NOT NULL DEFAULT now())"
        )
    applied = {row[0] for row in conn.execute("SELECT version FROM schema_migrations")}
    conn.commit()

    newly_applied: list[str] = []
    for path in sorted(directory.glob("*.sql")):
        version = path.stem
        if version in applied:
            continue
        log.info("Applying migration %s", version)
        with conn.transaction():
            conn.execute(path.read_text(encoding="utf-8"))
            conn.execute("INSERT INTO schema_migrations (version) VALUES (%s)", (version,))
        newly_applied.append(version)
    return newly_applied
