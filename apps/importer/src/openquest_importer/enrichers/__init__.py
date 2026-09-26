"""Enricher registry (entry point group ``openquest.enrichers``), see ``base.py``."""

from __future__ import annotations

from importlib.metadata import entry_points
from pathlib import Path
from typing import Any, Mapping

from openquest_importer.enrichers.base import Enricher

ENTRY_POINT_GROUP = "openquest.enrichers"


class UnknownEnricherError(LookupError):
    pass


def available_enrichers() -> dict[str, type[Enricher]]:
    return {ep.name: ep.load() for ep in entry_points(group=ENTRY_POINT_GROUP)}


def create_enricher(name: str, options: Mapping[str, Any] | None = None, cache_dir: Path | None = None) -> Enricher:
    matches = entry_points(group=ENTRY_POINT_GROUP, name=name)
    if not matches:
        known = ", ".join(sorted(available_enrichers())) or "none"
        raise UnknownEnricherError(f"No enricher '{name}' installed (available: {known})")
    enricher_cls = next(iter(matches)).load()
    if not issubclass(enricher_cls, Enricher):
        raise UnknownEnricherError(f"Entry point '{name}' is not an Enricher")
    return enricher_cls(options, cache_dir)
