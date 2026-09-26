"""Writes reports (``asset_report``) and environment readings (``environment_reading``).

Both come from feeds that only show a window of recent data (e.g. the last 90
days of the city's issue tracker, the last months of daily soil moisture). So
nothing is removed when it drops out of the window: reports keep their last
known status, readings stay as history.
"""

from __future__ import annotations

import json
import logging
from uuid import UUID

import psycopg

from openquest_importer.adapters.base import ParsedReadings, ParsedReports, ReportAdapter
from openquest_importer.config import SourceConfig
from openquest_importer.sync import SyncReport

log = logging.getLogger(__name__)

DEFAULT_LINK_RADIUS_M = 25.0

POINT = "ST_SetSRID(ST_MakePoint(i.lon, i.lat), 4326)::geography"


def apply_reports(conn: psycopg.Connection, source: SourceConfig, adapter: ReportAdapter, run_id: UUID,
                  source_id: UUID, parsed: ParsedReports, link_asset_type_ids: list[UUID]) -> SyncReport:
    ids = [r.external_id for r in parsed.reports]
    if len(set(ids)) != len(ids):
        raise ValueError("Duplicate report ids in the source")

    conn.execute(
        """
        CREATE TEMP TABLE incoming (
            external_id text, category text, status text, description text, status_notes text, address text,
            media_url text, lon float8, lat float8, reported_at timestamptz, source_updated_at timestamptz,
            raw jsonb, source_hash text
        ) ON COMMIT DROP
        """
    )
    with conn.cursor().copy(
        "COPY incoming (external_id, category, status, description, status_notes, address, media_url, lon, lat,"
        " reported_at, source_updated_at, raw, source_hash) FROM STDIN"
    ) as copy:
        for r in parsed.reports:
            copy.write_row((
                r.external_id, r.category, r.status, r.description, r.status_notes, r.address, r.media_url,
                r.lon, r.lat, r.reported_at, r.source_updated_at, json.dumps(r.raw, ensure_ascii=False),
                r.source_hash,
            ))

    unchanged = conn.execute(
        """
        UPDATE asset_report r SET last_seen_at = now()
        FROM incoming i
        WHERE r.data_source_id = %s AND r.external_id = i.external_id AND r.source_hash = i.source_hash
        """,
        (source_id,),
    ).rowcount
    written = conn.execute(
        f"""
        INSERT INTO asset_report (id, data_source_id, external_id, category, status, description, status_notes,
                                  address, media_url, geom, reported_at, source_updated_at, raw, source_hash,
                                  first_seen_at, last_seen_at)
        SELECT gen_random_uuid(), %s, i.external_id, i.category, i.status, i.description, i.status_notes,
               i.address, i.media_url, {POINT}, i.reported_at, i.source_updated_at, i.raw, i.source_hash,
               now(), now()
        FROM incoming i
        ON CONFLICT (data_source_id, external_id) DO UPDATE SET
            category = EXCLUDED.category, status = EXCLUDED.status, description = EXCLUDED.description,
            status_notes = EXCLUDED.status_notes, address = EXCLUDED.address, media_url = EXCLUDED.media_url,
            geom = EXCLUDED.geom, reported_at = EXCLUDED.reported_at,
            source_updated_at = EXCLUDED.source_updated_at, raw = EXCLUDED.raw,
            source_hash = EXCLUDED.source_hash, last_seen_at = now()
        WHERE asset_report.source_hash IS DISTINCT FROM EXCLUDED.source_hash
        RETURNING (xmax = 0) AS inserted
        """,
        (source_id,),
    ).fetchall()
    created = sum(1 for (inserted,) in written if inserted)
    updated = len(written) - created

    # (Re)link every report in the window to the nearest active asset: assets
    # may have been imported or removed since the report was first seen.
    radius = float(source.options.get("link_radius_m", DEFAULT_LINK_RADIUS_M))
    conn.execute(
        """
        WITH nearest AS (
            SELECT r.id AS report_id, n.asset_id, n.distance
            FROM asset_report r
            LEFT JOIN LATERAL (
                SELECT a.id AS asset_id, ST_Distance(a.geom, r.geom) AS distance
                FROM asset a
                WHERE a.asset_type_id = ANY(%s) AND a.status = 'active' AND ST_DWithin(a.geom, r.geom, %s)
                ORDER BY a.geom <-> r.geom
                LIMIT 1
            ) n ON true
            WHERE r.data_source_id = %s AND r.external_id = ANY(%s)
        )
        UPDATE asset_report r SET asset_id = nearest.asset_id, distance_m = round(nearest.distance::numeric, 1)
        FROM nearest
        WHERE r.id = nearest.report_id
          AND (r.asset_id IS DISTINCT FROM nearest.asset_id OR r.distance_m IS NULL)
        """,
        (link_asset_type_ids, radius, source_id, ids),
    )
    linked = conn.execute(
        "SELECT count(*) FROM asset_report WHERE data_source_id = %s AND external_id = ANY(%s) AND asset_id IS NOT NULL",
        (source_id, ids),
    ).fetchone()[0]

    _finish(conn, run_id, created, updated)
    report = SyncReport(run_id=run_id, source_key=source.key, record_count=len(ids),
                        created=created, updated=updated, unchanged=unchanged, removed=0)
    log.info("Sync %s of %s succeeded: %s; %d of %d reports linked to an asset within %.0f m",
             run_id, source.key, report, linked, len(ids), radius)
    return report


def apply_readings(conn: psycopg.Connection, source: SourceConfig, run_id: UUID, source_id: UUID,
                   parsed: ParsedReadings) -> SyncReport:
    conn.execute(
        """
        CREATE TEMP TABLE incoming (
            station_id text, metric text, value float8, unit text, measured_at timestamptz, lon float8, lat float8
        ) ON COMMIT DROP
        """
    )
    with conn.cursor().copy(
        "COPY incoming (station_id, metric, value, unit, measured_at, lon, lat) FROM STDIN"
    ) as copy:
        for r in parsed.readings:
            copy.write_row((r.station_id, r.metric, r.value, r.unit, r.measured_at, r.lon, r.lat))

    written = conn.execute(
        f"""
        INSERT INTO environment_reading (id, data_source_id, station_id, metric, value, unit, measured_at, geom,
                                         imported_at)
        SELECT gen_random_uuid(), %s, i.station_id, i.metric, i.value, i.unit, i.measured_at,
               CASE WHEN i.lon IS NULL THEN NULL ELSE {POINT} END, now()
        FROM incoming i
        ON CONFLICT (data_source_id, station_id, metric, measured_at) DO UPDATE SET
            value = EXCLUDED.value, unit = EXCLUDED.unit, geom = EXCLUDED.geom, imported_at = now()
        WHERE environment_reading.value IS DISTINCT FROM EXCLUDED.value
           OR environment_reading.unit IS DISTINCT FROM EXCLUDED.unit
        RETURNING (xmax = 0) AS inserted
        """,
        (source_id,),
    ).fetchall()
    created = sum(1 for (inserted,) in written if inserted)
    updated = len(written) - created

    _finish(conn, run_id, created, updated)
    report = SyncReport(run_id=run_id, source_key=source.key, record_count=len(parsed.readings),
                        created=created, updated=updated,
                        unchanged=len(parsed.readings) - created - updated, removed=0)
    log.info("Sync %s of %s succeeded: %s", run_id, source.key, report)
    return report


def _finish(conn: psycopg.Connection, run_id: UUID, created: int, updated: int) -> None:
    conn.execute(
        "UPDATE sync_run SET status = 'succeeded', finished_at = now(),"
        " assets_created = %s, assets_updated = %s, assets_removed = 0 WHERE id = %s",
        (created, updated, run_id),
    )
    conn.commit()
