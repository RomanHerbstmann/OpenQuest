"""Small geometry helpers for matching points at metre scale.

Positions are WGS84 lon/lat. For distances of a few metres a local
equirectangular projection is accurate to well below a centimetre, so no
projection library is needed.
"""

from __future__ import annotations

import math
from collections import defaultdict
from typing import Iterator, Sequence

METRES_PER_DEGREE_LAT = 111_320.0


class LocalProjection:
    """Projects lon/lat to metres around a reference latitude."""

    def __init__(self, ref_lat: float) -> None:
        self._mx = METRES_PER_DEGREE_LAT * math.cos(math.radians(ref_lat))

    @classmethod
    def around(cls, points: Sequence[tuple[float, float]]) -> "LocalProjection":
        lats = [lat for _, lat in points]
        return cls(sum(lats) / len(lats) if lats else 0.0)

    def xy(self, lon: float, lat: float) -> tuple[float, float]:
        return lon * self._mx, lat * METRES_PER_DEGREE_LAT


def pairs_within(
    a: Sequence[tuple[float, float]],
    b: Sequence[tuple[float, float]],
    radius_m: float,
    projection: LocalProjection,
) -> Iterator[tuple[int, int, float]]:
    """Yield ``(index_in_a, index_in_b, distance_m)`` for all pairs closer than ``radius_m``.

    Uses a grid with cell size ``radius_m``, so only the 3x3 neighbouring cells
    are compared. Linear in the number of points for evenly spread data.
    """
    if radius_m <= 0:
        raise ValueError("radius_m must be positive")
    grid: dict[tuple[int, int], list[tuple[int, float, float]]] = defaultdict(list)
    for j, (lon, lat) in enumerate(b):
        x, y = projection.xy(lon, lat)
        grid[(math.floor(x / radius_m), math.floor(y / radius_m))].append((j, x, y))
    for i, (lon, lat) in enumerate(a):
        x, y = projection.xy(lon, lat)
        cx, cy = math.floor(x / radius_m), math.floor(y / radius_m)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for j, bx, by in grid.get((cx + dx, cy + dy), ()):
                    d = math.hypot(x - bx, y - by)
                    if d <= radius_m:
                        yield i, j, d


def to_utm(lat: float, lon: float, zone: int = 32) -> tuple[float, float]:
    """WGS84 lat/lon → ETRS89 / UTM (EPSG:258xx, e.g. 25832 for zone 32), metres.

    Transverse Mercator series (Snyder) on GRS80, accurate to millimetres within
    the zone. ETRS89 and WGS84 differ by less than a metre, which doesn't matter
    for sampling rasters.
    """
    a = 6378137.0
    f = 1 / 298.257222101
    k0 = 0.9996
    e2 = f * (2 - f)
    ep2 = e2 / (1 - e2)
    e4, e6 = e2 * e2, e2 * e2 * e2
    phi = math.radians(lat)
    lam = math.radians(lon)
    lam0 = math.radians(zone * 6 - 183)

    sin, cos, tan = math.sin(phi), math.cos(phi), math.tan(phi)
    n = a / math.sqrt(1 - e2 * sin * sin)
    t = tan * tan
    c = ep2 * cos * cos
    big_a = cos * (lam - lam0)
    m = a * (
        (1 - e2 / 4 - 3 * e4 / 64 - 5 * e6 / 256) * phi
        - (3 * e2 / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * math.sin(2 * phi)
        + (15 * e4 / 256 + 45 * e6 / 1024) * math.sin(4 * phi)
        - (35 * e6 / 3072) * math.sin(6 * phi)
    )
    x = k0 * n * (
        big_a
        + (1 - t + c) * big_a**3 / 6
        + (5 - 18 * t + t * t + 72 * c - 58 * ep2) * big_a**5 / 120
    ) + 500_000
    y = k0 * (
        m
        + n * tan * (
            big_a**2 / 2
            + (5 - t + 9 * c + 4 * c * c) * big_a**4 / 24
            + (61 - 58 * t + t * t + 600 * c - 330 * ep2) * big_a**6 / 720
        )
    )
    return x, y


class AreaIndex:
    """Finds the named area (e.g. a city district) that contains a point.

    Areas are GeoJSON Polygon or MultiPolygon geometries in lon/lat. Holes are
    handled by the even-odd rule. Each polygon's edges are bucketed into
    horizontal strips, so a lookup only tests the few edges at the point's
    latitude instead of every vertex.
    """

    def __init__(self, areas: Sequence[tuple[str, dict]], strips: int = 256) -> None:
        self._polygons: list[_IndexedPolygon] = []
        for name, geometry in areas:
            if geometry.get("type") == "Polygon":
                polygons = [geometry["coordinates"]]
            elif geometry.get("type") == "MultiPolygon":
                polygons = geometry["coordinates"]
            else:
                raise ValueError(f"Area '{name}': expected Polygon or MultiPolygon, got {geometry.get('type')}")
            for rings in polygons:
                self._polygons.append(_IndexedPolygon(name, rings, strips))

    def __len__(self) -> int:
        return len({p.name for p in self._polygons})

    def find(self, lon: float, lat: float) -> str | None:
        for polygon in self._polygons:
            if polygon.contains(lon, lat):
                return polygon.name
        return None


class _IndexedPolygon:
    def __init__(self, name: str, rings: Sequence[Sequence[Sequence[float]]], strips: int) -> None:
        self.name = name
        edges = []
        for ring in rings:
            points = list(ring)
            if points and points[0] != points[-1]:
                points.append(points[0])  # GeoJSON rings are closed, but be lenient
            edges += [(a[0], a[1], b[0], b[1]) for a, b in zip(points, points[1:])]
        if not edges:
            raise ValueError(f"Area '{name}' has an empty polygon")
        xs = [c for e in edges for c in (e[0], e[2])]
        ys = [c for e in edges for c in (e[1], e[3])]
        self.min_x, self.max_x, self.min_y, self.max_y = min(xs), max(xs), min(ys), max(ys)
        self._count = strips
        self._height = (self.max_y - self.min_y) / strips or 1.0
        self._strips: list[list[tuple[float, float, float, float]]] = [[] for _ in range(strips)]
        for edge in edges:
            low, high = sorted((edge[1], edge[3]))
            for s in range(self._strip(low), self._strip(high) + 1):
                self._strips[s].append(edge)

    def _strip(self, y: float) -> int:
        return min(self._count - 1, max(0, int((y - self.min_y) / self._height)))

    def contains(self, x: float, y: float) -> bool:
        if not (self.min_x <= x <= self.max_x and self.min_y <= y <= self.max_y):
            return False
        inside = False
        for x1, y1, x2, y2 in self._strips[self._strip(y)]:
            if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
                inside = not inside
        return inside
