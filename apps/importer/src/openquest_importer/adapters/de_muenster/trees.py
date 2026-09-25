"""Münster tree inventory (Digitales Baumkataster).

Source and cleaning rules: docs/adr/0001-baumkataster-datenbezug-und-rueckkanal.md
and docs/research/0001-opendata-muenster-baumkataster.md.

The data comes live from the city's MapServer WFS (layer ``Baeume``). Each
record only has a point, ``str_schl`` (street key) and ``baumgruppe`` (genus).
There is no stable id, so assets are matched spatially (see ``Identity.SPATIAL``).
"""

from __future__ import annotations

import json
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
    record_hash,
)
from openquest_importer.geo import LocalProjection, pairs_within

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

    ``wfs_url``
        WFS endpoint, defaults to the city's ``odgruen_serv``.
    ``layer``
        WFS layer, defaults to ``Baeume``.
    ``file``
        Read a local GeoJSON file instead of the WFS (offline development, tests).
    ``timeout_s``
        Download timeout in seconds, default 120.
    """

    asset_type = "tree"
    identity = Identity.SPATIAL
    expected_fields = frozenset({"str_schl", "baumgruppe"})

    def fetch(self) -> Snapshot:
        path = self.options.get("file")
        if path:
            return Snapshot(content=Path(path).read_bytes(), extension="geojson")

        query = urllib.parse.urlencode({
            "SERVICE": "WFS",
            "VERSION": "1.1.0",
            "REQUEST": "GetFeature",
            "TYPENAME": self.options.get("layer", DEFAULT_LAYER),
            "OUTPUTFORMAT": "geojson",
        })
        url = f"{self.options.get('wfs_url', DEFAULT_WFS_URL)}?{query}"
        request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(request, timeout=float(self.options.get("timeout_s", 120))) as response:
                return Snapshot(content=response.read(), extension="geojson")
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
            raw: dict[str, Any] = {"properties": properties, "geometry": geometry}
            assets.append(NormalizedAsset(
                lon=lon,
                lat=lat,
                attributes={
                    "genus": genus,
                    "species": species,
                    "street_key": normalize_street_key(properties.get("str_schl")),
                    "quality_flags": flags,
                },
                raw=raw,
                source_hash=record_hash(raw),
            ))

        _flag_near_duplicates(assets)
        return ParsedSnapshot(fields=frozenset(fields), assets=assets)


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
