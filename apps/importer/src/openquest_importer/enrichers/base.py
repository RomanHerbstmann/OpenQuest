"""Enrichers add attributes to assets from data sets other than the source.

An adapter reads one city's data set. An enricher adds data that is keyed by
location instead, usually from a larger area than one city (e.g. tree heights
from the NRW surface model). Enrichers run after the adapter has parsed the
snapshot and before the attributes are validated, and are configured per data
source, so any city in the enricher's area can switch one on.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from pathlib import Path
from typing import Any, ClassVar, Mapping

from openquest_importer.adapters.base import NormalizedAsset, SnapshotFile


class EnricherError(Exception):
    """Raised when an enricher can't provide its data. The sync fails."""


class Enricher(ABC):
    #: Attribute keys the enricher sets; they must exist in the asset type's schema.
    attributes: ClassVar[frozenset[str]]

    def __init__(self, options: Mapping[str, Any] | None = None, cache_dir: Path | None = None) -> None:
        self.options: Mapping[str, Any] = options or {}
        #: Directory for data kept between syncs (e.g. already downloaded values).
        self.cache_dir = cache_dir

    @abstractmethod
    def enrich(self, assets: list[NormalizedAsset]) -> SnapshotFile | None:
        """Set this enricher's attributes on every asset (in place).

        Return a file with the values used, or ``None``. The file is stored in
        the sync's snapshot, so the result can be reproduced later.
        """
