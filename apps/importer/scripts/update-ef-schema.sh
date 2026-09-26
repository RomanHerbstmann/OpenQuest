#!/bin/sh
# Regenerates tests/fixtures/ef_schema.sql from the API's EF Core migrations.
# The importer's database tests build their database from this file, because the
# schema is owned by the API. Run it after adding an EF migration (needs the
# .NET SDK and dotnet-ef); tests/test_ef_schema.py fails while it is outdated.
set -eu
root="$(cd "$(dirname "$0")/../../.." && pwd)"
dotnet ef migrations script --idempotent \
    --project "$root/apps/api/OpenQuest.Api" \
    --output "$root/apps/importer/tests/fixtures/ef_schema.sql"
echo "Updated apps/importer/tests/fixtures/ef_schema.sql"
