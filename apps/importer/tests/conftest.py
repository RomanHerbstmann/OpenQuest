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


REPO = Path(__file__).resolve().parents[3]
EF_SCHEMA = FIXTURES / "ef_schema.sql"
CSHARP_ASSET_TYPES = REPO / "packages" / "core" / "OpenQuest.Core" / "Domain" / "AssetType.cs"
EF_MIGRATIONS = REPO / "apps" / "api" / "OpenQuest.Api" / "Data" / "Migrations"


def csharp_tree_schema() -> dict:
    """The tree attribute schema as defined in C# (the API seeds it from there)."""
    import re

    source = CSHARP_ASSET_TYPES.read_text(encoding="utf-8")
    match = re.search(r'new\(\s*"tree",\s*"[^"]*",\s*"[^"]*",\s*"""(.*?)"""', source, re.S)
    if not match:
        raise AssertionError(f"Tree schema not found in {CSHARP_ASSET_TYPES}")
    return json.loads(match.group(1))


@pytest.fixture
def db_url():
    """A fresh database with the API's schema (EF migrations) and the tree asset type.

    Skips without a test server. The schema comes from tests/fixtures/ef_schema.sql
    (regenerate with scripts/update-ef-schema.sh), the tree schema from AssetType.cs,
    just like the API seeds it on start.
    """
    server_url = os.environ.get("OPENQUEST_TEST_DATABASE_URL")
    if not server_url:
        pytest.skip("OPENQUEST_TEST_DATABASE_URL not set")

    import psycopg
    from psycopg.conninfo import make_conninfo
    from psycopg.types.json import Jsonb

    name = f"openquest_test_{uuid.uuid4().hex[:12]}"
    with psycopg.connect(server_url, autocommit=True) as admin:
        admin.execute(f'CREATE DATABASE "{name}"')
    url = make_conninfo(server_url, dbname=name)
    try:
        with psycopg.connect(url, autocommit=True) as conn:
            conn.execute(EF_SCHEMA.read_text(encoding="utf-8-sig"))
            conn.execute(
                "INSERT INTO asset_type (id, key, name, icon, attribute_schema)"
                " VALUES (gen_random_uuid(), 'tree', 'asset_type.tree', 'tree', %s)",
                (Jsonb(csharp_tree_schema()),),
            )
        yield url
    finally:
        with psycopg.connect(server_url, autocommit=True) as admin:
            admin.execute(f'DROP DATABASE IF EXISTS "{name}" WITH (FORCE)')
