"""Command line interface: ``openquest-importer migrate | sync | adapters``."""

from __future__ import annotations

import argparse
import logging
import os
import sys
from pathlib import Path

from openquest_importer.adapters import available_adapters, create_adapter
from openquest_importer.config import ConfigError, load_config
from openquest_importer.db import connect, migrate
from openquest_importer.snapshots import LocalSnapshotStore
from openquest_importer.sync import run_sync

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

    commands.add_parser("migrate", help="apply database migrations")

    sync = commands.add_parser("sync", help="import data sources")
    sync.add_argument("sources", nargs="*", metavar="SOURCE", help="source keys from the config")
    sync.add_argument("--all", action="store_true", help="sync all configured sources")
    sync.add_argument("--force", action="store_true",
                      help="apply the sync even if it removes more assets than max_removal_ratio")

    commands.add_parser("adapters", help="list installed adapters")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )

    if args.command == "adapters":
        for key, cls in sorted(available_adapters().items()):
            print(f"{key}\t{cls.__module__}.{cls.__qualname__}\tasset type: {cls.asset_type}")
        return 0

    try:
        config = load_config(args.config)
    except ConfigError as exc:
        log.error("%s", exc)
        return 2
    if not config.database_url:
        log.error("No database configured. Set DATABASE_URL or [database] url in %s", args.config)
        return 2

    with connect(config.database_url) as conn:
        if args.command == "migrate":
            applied = migrate(conn)
            print(f"Applied {len(applied)} migration(s)" + (f": {', '.join(applied)}" if applied else ""))
            return 0

        keys = list(config.sources) if args.all else args.sources
        if not keys:
            log.error("Name at least one source or use --all (configured: %s)", ", ".join(config.sources) or "none")
            return 2
        unknown = [k for k in keys if k not in config.sources]
        if unknown:
            log.error("Unknown source(s): %s", ", ".join(unknown))
            return 2

        store = LocalSnapshotStore(config.snapshot_dir)
        failed = 0
        for key in keys:
            source = config.sources[key]
            try:
                report = run_sync(conn, source, create_adapter(source.adapter, source.options), store,
                                  force=args.force)
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
