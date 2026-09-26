"""Runs a configured source: builds its adapter and enrichers and syncs it. Shared by the ``sync`` and ``serve`` commands."""

from __future__ import annotations

import psycopg

from openquest_importer.adapters import create_adapter
from openquest_importer.config import Config
from openquest_importer.enrichers import create_enricher
from openquest_importer.snapshots import SnapshotStore
from openquest_importer.sync import SyncReport, run_sync


def sync_source(conn: psycopg.Connection, config: Config, store: SnapshotStore, key: str, *,
                force: bool = False, accept_schema_change: bool = False) -> SyncReport:
    """Syncs one source of the configuration. Raises what ``run_sync`` raises (also recorded in ``sync_run``)."""
    source = config.sources[key]
    enrichers = [(e.name, create_enricher(e.name, e.options, config.cache_dir)) for e in source.enrichers]
    return run_sync(conn, source, create_adapter(source.adapter, source.options), store,
                    enrichers=enrichers, force=force, accept_schema_change=accept_schema_change)
