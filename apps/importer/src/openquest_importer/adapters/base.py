"""The contract between the importer core and city-specific adapters.

An adapter knows everything about one data set of one city: where to download
it, what the raw records look like and how to turn them into normalized
assets. The core knows nothing about any city. It stores snapshots, checks the
schema, matches records to existing assets and writes the database.
"""

from __future__ import annotations

import hashlib
import json
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, ClassVar, Mapping


class Identity(Enum):
    """How records of a source are matched to existing assets on re-sync."""

    #: The source has stable ids of its own (stored in ``asset.external_id``).
    EXTERNAL_ID = "external_id"
    #: The source has no stable ids. Records are matched by an identical
    #: ``source_hash`` first and then by nearest position within a radius.
    SPATIAL = "spatial"


@dataclass(frozen=True)
class Snapshot:
    """A raw download from the source, stored unchanged."""

    content: bytes
    extension: str  # file extension without dot, e.g. "geojson"


@dataclass(frozen=True)
class NormalizedAsset:
    """One source record, normalized to the core asset model."""

    lon: float  # WGS84
    lat: float  # WGS84
    attributes: dict[str, Any]
    raw: dict[str, Any]
    source_hash: str
    external_id: str | None = None


@dataclass(frozen=True)
class ParsedSnapshot:
    #: Field names found in the source records, used for the schema check.
    fields: frozenset[str]
    assets: list[NormalizedAsset] = field(default_factory=list)


class AdapterError(Exception):
    """Raised by adapters when the source cannot be read or parsed."""


class DataSourceAdapter(ABC):
    """Base class for all adapters.

    Subclasses set the class attributes and implement :meth:`fetch` and
    :meth:`parse`. ``options`` comes from the data source configuration.
    """

    #: Asset type key the adapter produces, e.g. "tree". Must exist in ``asset_type``.
    asset_type: ClassVar[str]
    #: Matching strategy, see :class:`Identity`.
    identity: ClassVar[Identity]
    #: Field names the adapter was written for. If the source's fields differ,
    #: the sync fails instead of importing data the adapter doesn't understand.
    expected_fields: ClassVar[frozenset[str]]

    def __init__(self, options: Mapping[str, Any] | None = None) -> None:
        self.options: Mapping[str, Any] = options or {}

    @abstractmethod
    def fetch(self) -> Snapshot:
        """Download the complete data set."""

    @abstractmethod
    def parse(self, snapshot: Snapshot) -> ParsedSnapshot:
        """Turn a snapshot into normalized assets."""


def record_hash(record: Mapping[str, Any]) -> str:
    """Stable hash of a raw record; identical records give identical hashes."""
    canonical = json.dumps(record, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()
