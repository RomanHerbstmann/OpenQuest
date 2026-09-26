"""Checks that the importer still fits the schema owned by the API (no database needed)."""

import re

from openquest_importer.adapters.de_muenster.natural_monuments import MuensterNaturalMonumentsAdapter
from openquest_importer.adapters.de_muenster.trees import MuensterTreesAdapter
from openquest_importer.adapters.de_nrw.strassen_trees import StrassenNrwTreesAdapter
from openquest_importer.enrichers.de_nrw.alleen import AlleenEnricher
from openquest_importer.enrichers.de_nrw.ndom import NdomHeightEnricher
from tests.conftest import EF_MIGRATIONS, EF_SCHEMA, csharp_asset_types, sample_options
from tests.test_more_sources import monument_options, strassen_options


def test_schema_fixture_contains_every_ef_migration():
    migrations = {p.stem for p in EF_MIGRATIONS.glob("*.cs")
                  if not p.stem.endswith(".Designer") and p.stem != "AppDbContextModelSnapshot"}
    in_fixture = set(re.findall(r"VALUES \('(\d{14}_\w+)'", EF_SCHEMA.read_text(encoding="utf-8-sig")))
    assert migrations == in_fixture, "tests/fixtures/ef_schema.sql is outdated, run scripts/update-ef-schema.sh"


def _written(adapter, enrichers=()) -> set[str]:
    written: set[str] = set()
    for asset in adapter.parse(adapter.fetch()).assets:
        written |= set(asset.attributes)
    for enricher in enrichers:
        written |= enricher.attributes
    return written


def test_csharp_schemas_declare_every_attribute_the_importer_writes():
    types = csharp_asset_types()
    cases = [
        ("tree", MuensterTreesAdapter(sample_options()), [NdomHeightEnricher, AlleenEnricher]),
        ("tree", StrassenNrwTreesAdapter(strassen_options()), [NdomHeightEnricher, AlleenEnricher]),
        ("natural_monument", MuensterNaturalMonumentsAdapter(monument_options()), [AlleenEnricher]),
    ]
    for key, adapter, enrichers in cases:
        declared = set(types[key][2]["properties"])
        written = _written(adapter, enrichers) | {"district", "quarter"} if key == "tree" else _written(adapter, enrichers)
        assert written <= declared, f"add {sorted(written - declared)} to the '{key}' asset type in AssetType.cs"


def test_quality_flags_are_declared():
    types = csharp_asset_types()
    tree_flags = set(types["tree"][2]["properties"]["quality_flags"]["items"]["enum"])
    assert {"placeholder_genus", "near_duplicate", "typo_corrected", "ambiguous_genus"} <= tree_flags
