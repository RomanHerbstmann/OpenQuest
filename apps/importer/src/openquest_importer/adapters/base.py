"""The contract between the importer core and city-specific adapters.

An adapter knows everything about one data set: where to download it, what the
raw records look like and how to turn them into normalized records. The core
knows nothing about any city. It stores snapshots, checks the schema, matches
records to what is already in the database and writes it.

There are three kinds of adapters, by what they produce:

- :class:`DataSourceAdapter`: assets (trees, natural monuments, ...), table ``asset``;
- :class:`ReportAdapter`: reports about the real world from external feeds
  (e.g. a citizen reporting a broken branch), table ``asset_report``;
- :class:`ReadingAdapter`: measured or modelled values of the environment
  (e.g. daily soil moisture), table ``environment_reading``.
"""

from __future__ import annotations

import hashlib
import json
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import datetime
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
class SnapshotFile:
    content: bytes
    extension: str  # file extension without dot, e.g. "csv"


@dataclass(frozen=True)
class Snapshot:
    """A raw download from the source, stored unchanged."""

    content: bytes
    extension: str  # file extension without dot, e.g. "geojson"
    #: Further files the adapter needs to parse the main file, e.g. reference
    #: data for enrichment. They are stored with the snapshot so every sync can
    #: be reproduced exactly.
    extras: Mapping[str, SnapshotFile] = field(default_factory=dict)


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


@dataclass(frozen=True)
class NormalizedReport:
    """One report from an external feed, e.g. the city's issue tracker."""

    external_id: str
    category: str  # normalized, e.g. "tree_damage"
    status: str  # "open" or "closed"
    lon: float  # WGS84
    lat: float
    reported_at: datetime
    raw: dict[str, Any]
    source_hash: str
    description: str | None = None
    status_notes: str | None = None
    address: str | None = None
    media_url: str | None = None
    source_updated_at: datetime | None = None


@dataclass(frozen=True)
class ParsedReports:
    fields: frozenset[str]
    reports: list[NormalizedReport] = field(default_factory=list)


@dataclass(frozen=True)
class Reading:
    """One value of one metric at one station and time."""

    station_id: str
    metric: str  # e.g. "soil_moisture_grass_sand_0_60cm"
    value: float
    unit: str  # e.g. "%nFK"
    measured_at: datetime  # start of the period (UTC)
    lon: float | None = None  # station position, WGS84
    lat: float | None = None


@dataclass(frozen=True)
class ParsedReadings:
    fields: frozenset[str]
    readings: list[Reading] = field(default_factory=list)


class AdapterError(Exception):
    """Raised by adapters when the source cannot be read or parsed."""


class BaseAdapter(ABC):
    """What all adapters share. ``options`` comes from the data source configuration."""

    #: Field names the adapter was written for. If the source's fields differ,
    #: the sync fails instead of importing data the adapter doesn't understand.
    expected_fields: ClassVar[frozenset[str]]

    def __init__(self, options: Mapping[str, Any] | None = None) -> None:
        self.options: Mapping[str, Any] = options or {}

    @abstractmethod
    def fetch(self) -> Snapshot:
        """Download the complete data set (or the complete window the source offers)."""

    @abstractmethod
    def parse(self, snapshot: Snapshot) -> ParsedSnapshot | ParsedReports | ParsedReadings:
        """Turn a snapshot into normalized records."""


class DataSourceAdapter(BaseAdapter):
    """Adapter that produces assets.

    Subclasses set the class attributes and implement :meth:`fetch` and :meth:`parse`.
    """

    #: Asset type key the adapter produces, e.g. "tree". Must exist in ``asset_type``.
    asset_type: ClassVar[str]
    #: Matching strategy, see :class:`Identity`.
    identity: ClassVar[Identity]

    @abstractmethod
    def parse(self, snapshot: Snapshot) -> ParsedSnapshot:
        """Turn a snapshot into normalized assets."""


class ReportAdapter(BaseAdapter):
    """Adapter that produces reports. Reports are matched by ``external_id`` and
    linked to the nearest active asset of ``link_asset_types`` within
    ``link_radius_m`` (option, default 25 m)."""

    #: Asset types a report can be linked to, e.g. ("tree",).
    link_asset_types: ClassVar[tuple[str, ...]]

    @abstractmethod
    def parse(self, snapshot: Snapshot) -> ParsedReports:
        """Turn a snapshot into normalized reports."""


class ReadingAdapter(BaseAdapter):
    """Adapter that produces environment readings, matched by station, metric and time."""

    @abstractmethod
    def parse(self, snapshot: Snapshot) -> ParsedReadings:
        """Turn a snapshot into readings."""


def record_hash(record: Mapping[str, Any]) -> str:
    """Stable hash of a raw record; identical records give identical hashes."""
    canonical = json.dumps(record, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()
