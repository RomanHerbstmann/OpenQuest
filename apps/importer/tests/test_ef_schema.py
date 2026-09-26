"""Checks that the importer still fits the schema owned by the API (no database needed)."""

import re

from openquest_importer.adapters.de_muenster.trees import MuensterTreesAdapter
from openquest_importer.enrichers.de_nrw.ndom import NdomHeightEnricher
from tests.conftest import EF_MIGRATIONS, EF_SCHEMA, csharp_tree_schema, sample_options


def test_schema_fixture_contains_every_ef_migration():
    migrations = {p.stem for p in EF_MIGRATIONS.glob("*.cs")
                  if not p.stem.endswith(".Designer") and p.stem != "AppDbContextModelSnapshot"}
    in_fixture = set(re.findall(r"VALUES \('(\d{14}_\w+)'", EF_SCHEMA.read_text(encoding="utf-8-sig")))
    assert migrations == in_fixture, "tests/fixtures/ef_schema.sql is outdated, run scripts/update-ef-schema.sh"


def test_csharp_tree_schema_declares_every_attribute_the_importer_writes():
    declared = set(csharp_tree_schema()["properties"])
    adapter = MuensterTreesAdapter(sample_options())
    written = set(adapter.parse(adapter.fetch()).assets[0].attributes) | NdomHeightEnricher.attributes
    assert written <= declared, f"add {sorted(written - declared)} to AssetType.Tree in AssetType.cs"
