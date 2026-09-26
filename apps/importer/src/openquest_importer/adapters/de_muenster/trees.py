"""Münster tree inventory (Digitales Baumkataster).

Source and cleaning rules: docs/adr/0001-baumkataster-datenbezug-und-rueckkanal.md
and docs/research/0001-opendata-muenster-baumkataster.md.

The data comes live from the city's MapServer WFS (layer ``Baeume``). Each
record only has a point, ``str_schl`` (street key) and ``baumgruppe`` (genus).
There is no stable id, so assets are matched spatially (see ``Identity.SPATIAL``).

Trees are enriched with the street name (via ``str_schl``), the city district
and the quarter (point in polygon), see ``reference.py``. The reference files are part of the
snapshot, so a sync can be reproduced exactly.
"""

from __future__ import annotations

import json
import logging
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

from openquest_importer import __version__
from openquest_importer.adapters.base import (
    AdapterError,
    DataSourceAdapter,
    Identity,
    NormalizedAsset,
    ParsedSnapshot,
    Snapshot,
    SnapshotFile,
    record_hash,
)
from openquest_importer.adapters.de_muenster import reference
from openquest_importer.geo import AreaIndex, LocalProjection, pairs_within

log = logging.getLogger(__name__)

DEFAULT_WFS_URL = "https://geo.stadt-muenster.de/mapserv/odgruen_serv"
DEFAULT_LAYER = "Baeume"
USER_AGENT = f"OpenQuest-Importer/{__version__} (+https://github.com/RomanHerbstmann/OpenQuest)"

# Values used instead of a genus (research note, section 4.3).
PLACEHOLDER_GENERA = {"baum amt62", "baumgruppe", "standort", "unbekannt", ""}
PLACEHOLDER_PREFIXES = ("leerer",)  # "Leerer", "Leerer Standort", ...

# Known typos and mixed levels (research note, section 4.3).
GENUS_CORRECTIONS = {
    "Catalpha": "Catalpa",
    "Cladrastris": "Cladrastis",
    "Malus-Hybride": "Malus",
}

NEAR_DUPLICATE_RADIUS_M = 1.0


def normalize_genus(value: str | None) -> tuple[str | None, str | None, list[str]]:
    """Return ``(genus, species, quality_flags)`` for a raw ``baumgruppe`` value."""
    text = (value or "").strip()
    lowered = text.lower()
    if lowered in PLACEHOLDER_GENERA or lowered.startswith(PLACEHOLDER_PREFIXES):
        return None, None, ["placeholder_genus"]

    flags: list[str] = []
    if text in GENUS_CORRECTIONS:
        text = GENUS_CORRECTIONS[text]
        flags.append("typo_corrected")

    # Some values are a full species name, e.g. "Metasequoia glyptostroboides".
    parts = text.split()
    if len(parts) == 2 and parts[0][:1].isupper() and parts[1].isalpha() and parts[1].islower():
        return parts[0], text, flags
    return text, None, flags


def normalize_street_key(value: str | None) -> str | None:
    text = (value or "").strip()
    if not text:
        return None
    # A few keys lost their leading zero; keys have 5 digits.
    return text.zfill(5) if text.isdigit() else text


class MuensterTreesAdapter(DataSourceAdapter):
    """Options:

    ``wfs_url``, ``layer``
        Tree WFS endpoint and layer, default the city's ``odgruen_serv`` / ``Baeume``.
    ``file``
        Read the trees from a local GeoJSON file instead of the WFS.
    ``enrich``
        Add street names, city districts and quarters (default ``true``).
    ``streets_url`` / ``streets_file``
        Street directory WFS, or a local CSV export of it.
    ``districts_url`` / ``districts_file``
        City district (Stadtbezirk) GeoJSON, or a local copy.
    ``quarters_url`` / ``quarters_file``
        Quarter (Stadtteil) GeoJSON, or a local copy.
    ``timeout_s``
        Download timeout in seconds, default 120.
    """

    asset_type = "tree"
    identity = Identity.SPATIAL
    expected_fields = frozenset({"str_schl", "baumgruppe"})

    @property
    def enrich(self) -> bool:
        return bool(self.options.get("enrich", True))

    def fetch(self) -> Snapshot:
        trees = self._read("file", _wfs_url(
            self.options.get("wfs_url", DEFAULT_WFS_URL), self.options.get("layer", DEFAULT_LAYER), "geojson"))
        extras: dict[str, SnapshotFile] = {}
        if self.enrich:
            extras["streets"] = SnapshotFile(
                content=self._read("streets_file", _wfs_url(
                    self.options.get("streets_url", reference.STREETS_URL), reference.STREETS_LAYER, "csv")),
                extension="csv",
            )
            extras["districts"] = SnapshotFile(
                content=self._read("districts_file", self.options.get("districts_url", reference.DISTRICTS_URL)),
                extension="geojson",
            )
            extras["quarters"] = SnapshotFile(
                content=self._read("quarters_file", self.options.get("quarters_url", reference.QUARTERS_URL)),
                extension="geojson",
            )
        return Snapshot(content=trees, extension="geojson", extras=extras)

    def _read(self, file_option: str, url: str) -> bytes:
        path = self.options.get(file_option)
        if path:
            return Path(path).read_bytes()
        request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(request, timeout=float(self.options.get("timeout_s", 120))) as response:
                return response.read()
        except OSError as exc:
            raise AdapterError(f"Download from {url} failed: {exc}") from exc

    def parse(self, snapshot: Snapshot) -> ParsedSnapshot:
        try:
            collection = json.loads(snapshot.content)
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise AdapterError(f"Snapshot is not valid GeoJSON: {exc}") from exc
        if collection.get("type") != "FeatureCollection":
            raise AdapterError("Snapshot is not a GeoJSON FeatureCollection")
        crs = (collection.get("crs") or {}).get("properties", {}).get("name", "")
        if crs and "CRS84" not in crs and not crs.endswith("4326"):
            raise AdapterError(f"Expected WGS84 lon/lat coordinates, got CRS {crs}")

        streets: dict[str, str] = {}
        districts: AreaIndex | None = None
        quarters: AreaIndex | None = None
        if self.enrich:
            missing = {"streets", "districts", "quarters"} - set(snapshot.extras)
            if missing:
                raise AdapterError(f"Snapshot lacks reference data {sorted(missing)}; fetch again or set enrich = false")
            streets = reference.parse_street_names(snapshot.extras["streets"].content)
            districts = reference.parse_districts(snapshot.extras["districts"].content)
            quarters = reference.parse_quarters(snapshot.extras["quarters"].content)

        fields: set[str] = set()
        assets: list[NormalizedAsset] = []
        for index, feature in enumerate(collection.get("features") or []):
            properties = feature.get("properties") or {}
            geometry = feature.get("geometry") or {}
            if geometry.get("type") != "Point":
                raise AdapterError(f"Feature {index}: expected a Point, got {geometry.get('type')}")
            lon, lat = (float(c) for c in geometry["coordinates"][:2])
            fields.update(properties)

            genus, species, flags = normalize_genus(properties.get("baumgruppe"))
            street_key = normalize_street_key(properties.get("str_schl"))
            raw: dict[str, Any] = {"properties": properties, "geometry": geometry}
            assets.append(NormalizedAsset(
                lon=lon,
                lat=lat,
                attributes={
                    "genus": genus,
                    "genus_raw": properties.get("baumgruppe"),
                    "species": species,
                    "street_key": street_key,
                    "street_name": streets.get(street_key) if street_key else None,
                    "district": districts.find(lon, lat) if districts else None,
                    "quarter": quarters.find(lon, lat) if quarters else None,
                    "quality_flags": flags,
                },
                raw=raw,
                source_hash=record_hash(raw),
            ))

        _flag_near_duplicates(assets)
        if self.enrich and assets:
            log.info(
                "Enriched %d trees: %d with street name, %d with district, %d with quarter",
                len(assets),
                sum(1 for a in assets if a.attributes["street_name"]),
                sum(1 for a in assets if a.attributes["district"]),
                sum(1 for a in assets if a.attributes["quarter"]),
            )
        return ParsedSnapshot(fields=frozenset(fields), assets=assets)


def _wfs_url(base: str, layer: str, output_format: str) -> str:
    query = urllib.parse.urlencode({
        "SERVICE": "WFS",
        "VERSION": "1.1.0",
        "REQUEST": "GetFeature",
        "TYPENAME": layer,
        "OUTPUTFORMAT": output_format,
    })
    return f"{base}?{query}"


def _flag_near_duplicates(assets: list[NormalizedAsset]) -> None:
    points = [(a.lon, a.lat) for a in assets]
    if not points:
        return
    projection = LocalProjection.around(points)
    for i, j, _ in pairs_within(points, points, NEAR_DUPLICATE_RADIUS_M, projection):
        if i < j:
            for k in (i, j):
                flags = assets[k].attributes["quality_flags"]
                if "near_duplicate" not in flags:
                    flags.append("near_duplicate")
