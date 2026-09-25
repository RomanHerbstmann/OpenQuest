"""Importer configuration (TOML file; DATABASE_URL and OPENQUEST_SNAPSHOT_DIR override it)."""

from __future__ import annotations

import os
import tomllib
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


class ConfigError(ValueError):
    pass


@dataclass(frozen=True)
class SyncOptions:
    #: Maximum distance in metres for matching a changed record to an existing asset.
    match_radius_m: float = 1.0
    #: Abort if more than this share of the active assets would be removed.
    #: Protects against a broken or truncated download wiping the data set.
    max_removal_ratio: float = 0.2


@dataclass(frozen=True)
class SourceConfig:
    key: str
    adapter: str
    name: str
    city: str | None = None
    source_url: str | None = None
    license: str | None = None
    attribution: str | None = None
    options: dict[str, Any] = field(default_factory=dict)
    sync: SyncOptions = field(default_factory=SyncOptions)


@dataclass(frozen=True)
class Config:
    database_url: str | None
    snapshot_dir: Path
    sources: dict[str, SourceConfig]


def load_config(path: Path) -> Config:
    try:
        data = tomllib.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ConfigError(f"Config file not found: {path}") from exc
    except tomllib.TOMLDecodeError as exc:
        raise ConfigError(f"Invalid config file {path}: {exc}") from exc

    base = path.parent
    database_url = os.environ.get("DATABASE_URL") or data.get("database", {}).get("url")
    snapshot_dir = Path(
        os.environ.get("OPENQUEST_SNAPSHOT_DIR") or data.get("snapshots", {}).get("dir", "data/snapshots")
    )
    if not snapshot_dir.is_absolute():
        snapshot_dir = (base / snapshot_dir).resolve()

    sources: dict[str, SourceConfig] = {}
    for entry in data.get("sources", []):
        try:
            source = SourceConfig(
                key=entry["key"],
                adapter=entry["adapter"],
                name=entry["name"],
                city=entry.get("city"),
                source_url=entry.get("source_url"),
                license=entry.get("license"),
                attribution=entry.get("attribution"),
                options=dict(entry.get("options", {})),
                sync=SyncOptions(**entry.get("sync", {})),
            )
        except KeyError as exc:
            raise ConfigError(f"Source is missing required field {exc}") from exc
        except TypeError as exc:
            raise ConfigError(f"Invalid sync options for source {entry.get('key')}: {exc}") from exc
        if source.key in sources:
            raise ConfigError(f"Duplicate source key: {source.key}")
        sources[source.key] = source

    return Config(database_url=database_url, snapshot_dir=snapshot_dir, sources=sources)
