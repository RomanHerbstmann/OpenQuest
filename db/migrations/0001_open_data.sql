-- Open data part of the data model (see docs/data-model/erd.md):
-- data sources, sync runs (snapshots), asset types, assets and their history.

CREATE EXTENSION IF NOT EXISTS postgis;

CREATE TABLE data_source (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    key           text NOT NULL UNIQUE,
    adapter_key   text NOT NULL,
    name          text NOT NULL,
    city          text,
    source_url    text,
    license       text,
    attribution   text,
    config        jsonb NOT NULL DEFAULT '{}',
    is_active     boolean NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE sync_run (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    data_source_id  uuid NOT NULL REFERENCES data_source (id),
    started_at      timestamptz NOT NULL DEFAULT now(),
    finished_at     timestamptz,
    status          text NOT NULL DEFAULT 'running'
                    CHECK (status IN ('running', 'succeeded', 'failed')),
    snapshot_key    text,
    schema_hash     text,
    record_count    int,
    assets_created  int,
    assets_updated  int,
    assets_removed  int,
    error           text
);

CREATE INDEX sync_run_source_started_idx ON sync_run (data_source_id, started_at DESC);

CREATE TABLE asset_type (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    key               text NOT NULL UNIQUE,
    name              text NOT NULL,
    icon              text,
    attribute_schema  jsonb NOT NULL DEFAULT '{}'
);

CREATE TABLE asset (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    asset_type_id   uuid NOT NULL REFERENCES asset_type (id),
    data_source_id  uuid NOT NULL REFERENCES data_source (id),
    -- Only set when the source has its own stable ids. Münster trees have none;
    -- they are matched spatially and identified by our own id.
    external_id     text,
    geom            geography (Geometry, 4326) NOT NULL,
    attributes      jsonb NOT NULL DEFAULT '{}',
    raw             jsonb NOT NULL,
    source_hash     text NOT NULL,
    status          text NOT NULL DEFAULT 'active'
                    CHECK (status IN ('active', 'removed_at_source')),
    first_seen_at   timestamptz NOT NULL DEFAULT now(),
    last_seen_at    timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX asset_external_id_uq ON asset (data_source_id, external_id)
    WHERE external_id IS NOT NULL;
CREATE INDEX asset_geom_idx ON asset USING gist (geom);
CREATE INDEX asset_source_status_idx ON asset (data_source_id, status);
CREATE INDEX asset_attributes_idx ON asset USING gin (attributes jsonb_path_ops);

CREATE TABLE asset_snapshot (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    asset_id     uuid NOT NULL REFERENCES asset (id),
    sync_run_id  uuid NOT NULL REFERENCES sync_run (id),
    change_type  text NOT NULL CHECK (change_type IN ('created', 'updated', 'removed')),
    geom         geography (Geometry, 4326) NOT NULL,
    raw          jsonb NOT NULL,
    source_hash  text NOT NULL,
    UNIQUE (asset_id, sync_run_id)
);

CREATE INDEX asset_snapshot_run_idx ON asset_snapshot (sync_run_id);

INSERT INTO asset_type (key, name, icon, attribute_schema) VALUES (
    'tree',
    'asset_type.tree',
    'tree',
    '{
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "genus":                  { "type": ["string", "null"], "description": "Latin genus, e.g. Tilia" },
        "species":                { "type": ["string", "null"], "description": "Latin species, e.g. Metasequoia glyptostroboides" },
        "street_key":             { "type": ["string", "null"], "description": "5 digits, zero-padded" },
        "street_name":            { "type": ["string", "null"] },
        "district":               { "type": ["string", "null"], "description": "Stadtbezirk" },
        "trunk_circumference_cm": { "type": ["number", "null"] },
        "condition":              { "enum": ["good", "damaged", "dead", "gone", null] },
        "photo_url":              { "type": ["string", "null"] },
        "quality_flags": {
          "type": "array",
          "items": { "enum": ["placeholder_genus", "near_duplicate", "typo_corrected"] },
          "uniqueItems": true
        }
      }
    }'
);
