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
