"""Runs one sync of a data source: download, check, match, write.

Every sync is recorded in ``sync_run``, also when it fails. Changes to assets
are written in a single transaction, so a failed sync leaves the assets as
they were.
"""

from __future__ import annotations

import hashlib
import json
import logging
from dataclasses import dataclass
from typing import Any, Sequence
from uuid import UUID, uuid4

import psycopg
from psycopg.types.json import Jsonb
from jsonschema import Draft202012Validator

from openquest_importer.adapters.base import DataSourceAdapter, NormalizedAsset, Snapshot
from openquest_importer.config import SourceConfig
from openquest_importer.enrichers.base import Enricher
from openquest_importer.matching import ExistingAsset, MatchResult, match
from openquest_importer.snapshots import SnapshotStore

log = logging.getLogger(__name__)


class SyncError(Exception):
    pass


class SchemaChangedError(SyncError):
    pass


class InvalidAttributesError(SyncError):
    pass


class RemovalGuardError(SyncError):
    pass


@dataclass(frozen=True)
class SyncReport:
    run_id: UUID
    source_key: str
    record_count: int
    created: int
    updated: int
    unchanged: int
    removed: int


def schema_hash(fields: frozenset[str]) -> str:
    return hashlib.sha256(json.dumps(sorted(fields)).encode()).hexdigest()


def run_sync(
    conn: psycopg.Connection,
    source: SourceConfig,
    adapter: DataSourceAdapter,
    store: SnapshotStore,
    *,
    enrichers: Sequence[tuple[str, Enricher]] = (),
    force: bool = False,
) -> SyncReport:
    """Sync one data source.

    ``enrichers`` are ``(name, enricher)`` pairs run after parsing, in order.
    ``force`` skips the removal guard.
    """
    source_id = _upsert_data_source(conn, source)
    type_row = conn.execute(
        "SELECT id, attribute_schema FROM asset_type WHERE key = %s", (adapter.asset_type,)
    ).fetchone()
    if type_row is None:
        raise SyncError(f"Asset type '{adapter.asset_type}' does not exist; run the migrations first")
    asset_type_id, attribute_schema = type_row
    conn.commit()

    locked = conn.execute("SELECT pg_try_advisory_lock(hashtext(%s))", (f"sync:{source.key}",)).fetchone()[0]
    conn.commit()
    if not locked:
        raise SyncError(f"A sync of '{source.key}' is already running")

    try:
        run_id = conn.execute(
            "INSERT INTO sync_run (data_source_id) VALUES (%s) RETURNING id", (source_id,)
        ).fetchone()[0]
        conn.commit()
        log.info("Sync %s of %s started", run_id, source.key)
        try:
            report = _run(conn, source, adapter, store, run_id, source_id,
                          asset_type_id, attribute_schema, enrichers, force)
        except Exception as exc:
            conn.rollback()
            conn.execute(
                "UPDATE sync_run SET status = 'failed', finished_at = now(), error = %s WHERE id = %s",
                (f"{type(exc).__name__}: {exc}", run_id),
            )
            conn.commit()
            log.error("Sync %s of %s failed: %s", run_id, source.key, exc)
            raise
        return report
    finally:
        conn.execute("SELECT pg_advisory_unlock(hashtext(%s))", (f"sync:{source.key}",))
        conn.commit()


def _run(conn, source, adapter, store, run_id, source_id, asset_type_id,
         attribute_schema, enrichers, force) -> SyncReport:
    snapshot = adapter.fetch()
    key = store.save(source.key, snapshot)
    conn.execute("UPDATE sync_run SET snapshot_key = %s WHERE id = %s", (key, run_id))
    conn.commit()

    parsed = adapter.parse(snapshot)
    conn.execute(
        "UPDATE sync_run SET schema_hash = %s, record_count = %s WHERE id = %s",
        (schema_hash(parsed.fields), len(parsed.assets), run_id),
    )
    conn.commit()

    if parsed.fields != adapter.expected_fields:
        unexpected = sorted(parsed.fields - adapter.expected_fields)
        missing = sorted(adapter.expected_fields - parsed.fields)
        raise SchemaChangedError(
            f"Source fields changed (unexpected: {unexpected or 'none'}, missing: {missing or 'none'}). "
            "Check the source and update the adapter."
        )

    if enrichers:
        # Store what every enricher used next to the download, so the sync
        # stays reproducible, and point the run to the extended snapshot.
        extras = dict(snapshot.extras)
        for name, enricher in enrichers:
            result = enricher.enrich(parsed.assets)
            if result is not None:
                extras[f"enrichment:{name}"] = result
        key = store.save(source.key, Snapshot(content=snapshot.content, extension=snapshot.extension, extras=extras))
        conn.execute("UPDATE sync_run SET snapshot_key = %s WHERE id = %s", (key, run_id))
        conn.commit()

    _validate(parsed.assets, attribute_schema)

    existing = _load_existing(conn, source_id)
    result = match(existing, parsed.assets, adapter.identity, source.sync.match_radius_m)

    if existing and not force and len(result.removed) > source.sync.max_removal_ratio * len(existing):
        raise RemovalGuardError(
            f"Sync would remove {len(result.removed)} of {len(existing)} assets "
            f"(limit {source.sync.max_removal_ratio:.0%}). "
            "Check the download; run with --force if the removal is real."
        )

    # All asset changes and the run's success are committed together.
    _apply(conn, run_id, source_id, asset_type_id, result)
    conn.execute(
        "UPDATE sync_run SET status = 'succeeded', finished_at = now(),"
        " assets_created = %s, assets_updated = %s, assets_removed = %s WHERE id = %s",
        (len(result.created), len(result.updated), len(result.removed), run_id),
    )
    conn.commit()

    report = SyncReport(
        run_id=run_id,
        source_key=source.key,
        record_count=len(parsed.assets),
        created=len(result.created),
        updated=len(result.updated),
        unchanged=len(result.unchanged),
        removed=len(result.removed),
    )
    log.info("Sync %s of %s succeeded: %s", run_id, source.key, report)
    return report


def _upsert_data_source(conn: psycopg.Connection, source: SourceConfig) -> UUID:
    config: dict[str, Any] = {
        "options": source.options,
        "sync": {
            "match_radius_m": source.sync.match_radius_m,
            "max_removal_ratio": source.sync.max_removal_ratio,
        },
    }
    row = conn.execute(
        """
        INSERT INTO data_source (key, adapter_key, name, city, source_url, license, attribution, config)
        VALUES (%s, %s, %s, %s, %s, %s, %s, %s)
        ON CONFLICT (key) DO UPDATE SET
            adapter_key = EXCLUDED.adapter_key, name = EXCLUDED.name, city = EXCLUDED.city,
            source_url = EXCLUDED.source_url, license = EXCLUDED.license,
            attribution = EXCLUDED.attribution, config = EXCLUDED.config
        RETURNING id
        """,
        (source.key, source.adapter, source.name, source.city, source.source_url,
         source.license, source.attribution, Jsonb(config)),
    ).fetchone()
    return row[0]


def _validate(assets: list[NormalizedAsset], attribute_schema: dict[str, Any]) -> None:
    validator = Draft202012Validator(attribute_schema)
    problems: list[str] = []
    for index, asset in enumerate(assets):
        for error in validator.iter_errors(asset.attributes):
            problems.append(f"record {index}: {error.message}")
            if len(problems) >= 5:
                break
        if len(problems) >= 5:
            break
    if problems:
        raise InvalidAttributesError("Attributes don't match the asset type schema: " + "; ".join(problems))


def _load_existing(conn: psycopg.Connection, source_id: UUID) -> list[ExistingAsset]:
    rows = conn.execute(
        """
        SELECT id, ST_X(geom::geometry), ST_Y(geom::geometry), source_hash, external_id, attributes
        FROM asset WHERE data_source_id = %s AND status = 'active'
        """,
        (source_id,),
    ).fetchall()
    return [
        ExistingAsset(id=r[0], lon=r[1], lat=r[2], source_hash=r[3], external_id=r[4], attributes=r[5])
        for r in rows
    ]


def _apply(conn: psycopg.Connection, run_id: UUID, source_id: UUID, asset_type_id: UUID,
           result: MatchResult) -> None:
    conn.execute(
        """
        CREATE TEMP TABLE incoming (
            asset_id uuid, change_type text, external_id text, lon float8, lat float8,
            attributes jsonb, raw jsonb, source_hash text
        ) ON COMMIT DROP
        """
    )
    rows: list[tuple[UUID, str, NormalizedAsset]] = [(uuid4(), "created", a) for a in result.created]
    rows += [(old.id, "updated", new) for old, new in result.updated]
    with conn.cursor().copy(
        "COPY incoming (asset_id, change_type, external_id, lon, lat, attributes, raw, source_hash) FROM STDIN"
    ) as copy:
        for asset_id, change_type, a in rows:
            copy.write_row((
                str(asset_id), change_type, a.external_id, a.lon, a.lat,
                json.dumps(a.attributes, ensure_ascii=False), json.dumps(a.raw, ensure_ascii=False),
                a.source_hash,
            ))

    point = "ST_SetSRID(ST_MakePoint(i.lon, i.lat), 4326)::geography"
    conn.execute(
        f"""
        INSERT INTO asset (id, asset_type_id, data_source_id, external_id, geom, attributes, raw, source_hash)
        SELECT i.asset_id, %s, %s, i.external_id, {point}, i.attributes, i.raw, i.source_hash
        FROM incoming i WHERE i.change_type = 'created'
        """,
        (asset_type_id, source_id),
    )
    conn.execute(
        f"""
        UPDATE asset a SET geom = {point}, attributes = i.attributes, raw = i.raw,
            source_hash = i.source_hash, external_id = i.external_id,
            updated_at = now(), last_seen_at = now()
        FROM incoming i WHERE i.change_type = 'updated' AND a.id = i.asset_id
        """
    )
    conn.execute(
        "UPDATE asset SET last_seen_at = now() WHERE id = ANY(%s)",
        ([old.id for old, _ in result.unchanged],),
    )
    removed_ids = [old.id for old in result.removed]
    conn.execute(
        "UPDATE asset SET status = 'removed_at_source', updated_at = now() WHERE id = ANY(%s)",
        (removed_ids,),
    )

    conn.execute(
        f"""
        INSERT INTO asset_snapshot (asset_id, sync_run_id, change_type, geom, raw, source_hash)
        SELECT i.asset_id, %s, i.change_type, {point}, i.raw, i.source_hash FROM incoming i
        """,
        (run_id,),
    )
    conn.execute(
        """
        INSERT INTO asset_snapshot (asset_id, sync_run_id, change_type, geom, raw, source_hash)
        SELECT id, %s, 'removed', geom, raw, source_hash FROM asset WHERE id = ANY(%s)
        """,
        (run_id, removed_ids),
    )
