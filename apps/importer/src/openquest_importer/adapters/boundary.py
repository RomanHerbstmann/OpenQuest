"""Clipping data sets to an area, e.g. a state-wide inventory to one city.

The boundary is any GeoJSON file of Polygon / MultiPolygon features in WGS84
(for Münster: the Stadtbezirke, which together cover the city).
"""

from __future__ import annotations

import json
from dataclasses import dataclass

from openquest_importer.adapters.base import AdapterError
from openquest_importer.geo import AreaIndex, to_utm


@dataclass(frozen=True)
class Boundary:
    index: AreaIndex
    #: Bounding box in UTM (min_x, min_y, max_x, max_y), for cheap pre-filtering.
    utm_bbox: tuple[float, float, float, float]

    def contains(self, lon: float, lat: float) -> bool:
        return self.index.find(lon, lat) is not None


def parse_boundary(content: bytes, utm_zone: int = 32) -> Boundary:
    try:
        collection = json.loads(content)
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AdapterError(f"Boundary is not valid GeoJSON: {exc}") from exc
    crs = (collection.get("crs") or {}).get("properties", {}).get("name", "")
    if crs and "CRS84" not in crs and not crs.endswith("4326"):
        raise AdapterError(f"Boundary: expected WGS84 lon/lat coordinates, got CRS {crs}")

    areas, xs, ys = [], [], []
    for index, feature in enumerate(collection.get("features") or []):
        geometry = feature.get("geometry") or {}
        areas.append((f"area-{index}", geometry))
        polygons = [geometry.get("coordinates", [])] if geometry.get("type") == "Polygon" else geometry.get("coordinates", [])
        for rings in polygons:
            for ring in rings:
                for lon, lat, *_ in ring:
                    x, y = to_utm(lat, lon, utm_zone)
                    xs.append(x)
                    ys.append(y)
    if not areas or not xs:
        raise AdapterError("Boundary contains no polygons")
    try:
        index = AreaIndex(areas)
    except ValueError as exc:
        raise AdapterError(f"Boundary: {exc}") from exc
    return Boundary(index=index, utm_bbox=(min(xs), min(ys), max(xs), max(ys)))


def parse_named_areas(content: bytes, name_field: str, label: str = "Area") -> AreaIndex:
    """Named areas (e.g. districts) from GeoJSON; the name is the feature property ``name_field``."""
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
