"""Münster reference data used to enrich trees: street names, districts and quarters.

Sources (research note 0001, section 4.5):

- Street directory WFS ``odstrasseserv``, layer ``ms:Strassen``: ``STR_SCHL`` → ``NAME``.
  Joined to the trees via ``str_schl`` (matches ~99.4 % of trees).
- Stadtbezirke (6 city districts) as GeoJSON from the open data portal, name in ``NAME_STADT``.
- Stadtteile (45 statistical districts / quarters) as GeoJSON from the portal, name in ``NAME_STATI``.
"""

from __future__ import annotations

import csv
import io
import json

from openquest_importer.adapters.base import AdapterError
from openquest_importer.geo import AreaIndex

STREETS_URL = "https://www.stadt-muenster.de/ows/mapserv706/odstrasseserv"
STREETS_LAYER = "ms:Strassen"
DISTRICTS_URL = "https://opendata.stadt-muenster.de/sites/default/files/stadtbezirke-muenster.geojson"
QUARTERS_URL = "https://opendata.stadt-muenster.de/sites/default/files/stadtteile-statistische-bezirke-muenster.geojson"

STREET_FIELDS = ("STR_SCHL", "NAME")
DISTRICT_NAME_FIELD = "NAME_STADT"
QUARTER_NAME_FIELD = "NAME_STATI"


def parse_street_names(content: bytes) -> dict[str, str]:
    """``STR_SCHL`` (5 digits) → street name, from the WFS CSV export."""
    try:
        text = content.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        raise AdapterError(f"Street list is not UTF-8: {exc}") from exc
    reader = csv.DictReader(io.StringIO(text))
    missing = [f for f in STREET_FIELDS if f not in (reader.fieldnames or [])]
    if missing:
        raise AdapterError(f"Street list is missing fields {missing} (got {reader.fieldnames})")

    names: dict[str, str] = {}
    for row in reader:
        key = (row["STR_SCHL"] or "").strip()
        name = (row["NAME"] or "").strip()
        if key and name:
            names[key.zfill(5) if key.isdigit() else key] = name
    if not names:
        raise AdapterError("Street list contains no streets")
    return names


def parse_districts(content: bytes) -> AreaIndex:
    return _parse_areas(content, DISTRICT_NAME_FIELD, "District")


def parse_quarters(content: bytes) -> AreaIndex:
    return _parse_areas(content, QUARTER_NAME_FIELD, "Quarter")


def _parse_areas(content: bytes, name_field: str, label: str) -> AreaIndex:
    try:
        collection = json.loads(content)
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AdapterError(f"{label} file is not valid GeoJSON: {exc}") from exc
    crs = (collection.get("crs") or {}).get("properties", {}).get("name", "")
    if crs and "CRS84" not in crs and not crs.endswith("4326"):
        raise AdapterError(f"{label} file: expected WGS84 lon/lat coordinates, got CRS {crs}")

    areas = []
    for index, feature in enumerate(collection.get("features") or []):
        name = (feature.get("properties") or {}).get(name_field)
        if not name:
            raise AdapterError(f"{label} feature {index} has no {name_field}")
        areas.append((str(name).strip(), feature.get("geometry") or {}))
    if not areas:
        raise AdapterError(f"{label} file contains no areas")
    try:
        return AreaIndex(areas)
    except ValueError as exc:
        raise AdapterError(f"{label} file: {exc}") from exc
