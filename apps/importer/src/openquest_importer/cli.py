"""Command line interface: ``openquest-importer check | sync | serve | adapters``."""

from __future__ import annotations

import argparse
import logging
import os
import sys
from pathlib import Path

from openquest_importer.adapters import available_adapters
from openquest_importer.adapters.base import DataSourceAdapter, ReportAdapter
from openquest_importer.config import ConfigError, load_config
from openquest_importer.db import SchemaNotReadyError, connect, wait_for_schema
from openquest_importer.enrichers import available_enrichers
from openquest_importer.runner import sync_source
from openquest_importer.serve import serve
from openquest_importer.snapshots import create_store

log = logging.getLogger("openquest_importer")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="openquest-importer", description=__doc__)
    parser.add_argument(
        "--config",
        type=Path,
        default=Path(os.environ.get("OPENQUEST_IMPORTER_CONFIG", "importer.toml")),
        help="config file (default: importer.toml or $OPENQUEST_IMPORTER_CONFIG)",
    )
    parser.add_argument("-v", "--verbose", action="store_true", help="debug logging")
    commands = parser.add_subparsers(dest="command", required=True)

    check = commands.add_parser("check", help="check that the API has set up the database schema")
    check.add_argument("--wait", type=float, default=0, metavar="SECONDS",
                       help="keep checking for up to SECONDS (e.g. while the API starts)")

    sync = commands.add_parser("sync", help="import data sources")
    sync.add_argument("sources", nargs="*", metavar="SOURCE", help="source keys from the config")
    sync.add_argument("--all", action="store_true", help="sync all enabled sources")
    sync.add_argument("--force", action="store_true",
                      help="apply the sync even if it removes more assets than max_removal_ratio")
    sync.add_argument("--accept-schema-change", action="store_true",
                      help="import even if the source's fields differ from what the adapter expects")

    serve_parser = commands.add_parser(
        "serve", help="sync on a schedule and whenever an admin requests it (POST /admin/sync in the API)")
    serve_parser.add_argument("--interval", type=float, default=86400, metavar="SECONDS",
                              help="seconds between scheduled syncs of all sources; 0 = only sync on request (default: 86400)")
    serve_parser.add_argument("--no-initial-sync", action="store_true", help="wait one interval before the first scheduled sync")

    commands.add_parser("adapters", help="list installed adapters and enrichers")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )

    if args.command == "adapters":
        for key, cls in sorted(available_adapters().items()):
            if issubclass(cls, DataSourceAdapter):
                produces = f"assets of type {cls.asset_type}"
            elif issubclass(cls, ReportAdapter):
                produces = "reports"
            else:
                produces = "readings"
            print(f"adapter   {key}\t{cls.__module__}.{cls.__qualname__}\t{produces}")
        for key, cls in sorted(available_enrichers().items()):
            print(f"enricher  {key}\t{cls.__module__}.{cls.__qualname__}\tsets: {', '.join(sorted(cls.attributes))}")
        return 0

    try:
        config = load_config(args.config)
    except ConfigError as exc:
        log.error("%s", exc)
        return 2
    if not config.database_url:
        log.error("No database configured. Set DATABASE_URL or [database] url in %s", args.config)
        return 2

    if args.command == "check":
        try:
            wait_for_schema(config.database_url, args.wait)
        except SchemaNotReadyError as exc:
            log.error("%s", exc)
            return 1
        print("Database schema ready")
        return 0

    try:
        wait_for_schema(config.database_url, 0)
    except SchemaNotReadyError as exc:
        log.error("%s", exc)
        return 2

    if args.command == "serve":
        serve(config, create_store(config), interval_seconds=args.interval, initial_sync=not args.no_initial_sync)
        return 0

    with connect(config.database_url) as conn:

        keys = [k for k, s in config.sources.items() if s.enabled] if args.all else args.sources
        if not keys:
            log.error("Name at least one source or use --all (configured: %s)", ", ".join(config.sources) or "none")
            return 2
        unknown = [k for k in keys if k not in config.sources]
        if unknown:
            log.error("Unknown source(s): %s", ", ".join(unknown))
            return 2

        store = create_store(config)
        failed = 0
        for key in keys:
            source = config.sources[key]
            if not source.enabled:
                log.warning("%s is disabled in the config; syncing it because it was named explicitly", key)
            try:
                report = sync_source(conn, config, store, key, force=args.force, accept_schema_change=args.accept_schema_change)
            except Exception as exc:  # report and continue with the other sources
                log.error("%s: %s", key, exc)
                failed += 1
                continue
            print(
                f"{key}: {report.record_count} records, {report.created} created, "
                f"{report.updated} updated, {report.unchanged} unchanged, {report.removed} removed"
            )
        return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
