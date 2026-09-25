from __future__ import annotations

import copy
import json
import os
import uuid
from pathlib import Path

import pytest

FIXTURES = Path(__file__).parent / "fixtures"
SAMPLE = FIXTURES / "muenster_trees_sample.geojson"
STREETS_SAMPLE = FIXTURES / "muenster_streets_sample.csv"
DISTRICTS_SAMPLE = FIXTURES / "muenster_districts_sample.geojson"
QUARTERS_SAMPLE = FIXTURES / "muenster_quarters_sample.geojson"
NDOM_CELL = FIXTURES / "ndom_cell_404682_5759122.tif"


def sample_options(trees: Path = SAMPLE) -> dict:
    """Adapter options that read everything from fixtures (no network)."""
    return {"file": str(trees), "streets_file": str(STREETS_SAMPLE), "districts_file": str(DISTRICTS_SAMPLE),
            "quarters_file": str(QUARTERS_SAMPLE)}


@pytest.fixture
def sample_collection() -> dict:
    return json.loads(SAMPLE.read_text(encoding="utf-8"))


@pytest.fixture
def write_collection(tmp_path):
    """Writes a (modified) GeoJSON collection to a temp file and returns its path."""

    def write(collection: dict, name: str = "snapshot.geojson") -> Path:
        path = tmp_path / name
        path.write_text(json.dumps(copy.deepcopy(collection)), encoding="utf-8")
        return path

    return write


@pytest.fixture
def db_url():
    """A fresh database with all migrations applied. Skips without a test server."""
    server_url = os.environ.get("OPENQUEST_TEST_DATABASE_URL")
    if not server_url:
        pytest.skip("OPENQUEST_TEST_DATABASE_URL not set")

    import psycopg
    from psycopg.conninfo import make_conninfo

    from openquest_importer.db import migrate

    name = f"openquest_test_{uuid.uuid4().hex[:12]}"
    with psycopg.connect(server_url, autocommit=True) as admin:
        admin.execute(f'CREATE DATABASE "{name}"')
    url = make_conninfo(server_url, dbname=name)
    try:
        with psycopg.connect(url) as conn:
            migrate(conn)
        yield url
    finally:
        with psycopg.connect(server_url, autocommit=True) as admin:
            admin.execute(f'DROP DATABASE IF EXISTS "{name}" WITH (FORCE)')
