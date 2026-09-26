"""Matches the records of a new snapshot to the assets already in the database.

This decides which assets keep their (our own) id, which are new and which
disappeared at the source. It is pure Python so it can be tested without a
database.
"""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field
from typing import Any
from uuid import UUID

from openquest_importer.adapters.base import Identity, NormalizedAsset
from openquest_importer.geo import LocalProjection, pairs_within


@dataclass(frozen=True)
class ExistingAsset:
    id: UUID
    lon: float
    lat: float
    source_hash: str
    external_id: str | None = None
    attributes: dict[str, Any] = field(default_factory=dict)


@dataclass
class MatchResult:
    unchanged: list[tuple[ExistingAsset, NormalizedAsset]] = field(default_factory=list)
    updated: list[tuple[ExistingAsset, NormalizedAsset]] = field(default_factory=list)
    created: list[NormalizedAsset] = field(default_factory=list)
    removed: list[ExistingAsset] = field(default_factory=list)


class MatchingError(ValueError):
    pass


def match(
    existing: list[ExistingAsset],
    incoming: list[NormalizedAsset],
    identity: Identity,
    radius_m: float = 1.0,
) -> MatchResult:
    if identity is Identity.EXTERNAL_ID:
        return _match_by_external_id(existing, incoming)
    return _match_spatially(existing, incoming, radius_m)


def _pair(result: MatchResult, old: ExistingAsset, new: NormalizedAsset) -> None:
    # Attributes can change without the raw record changing, e.g. when the
    # adapter's cleaning rules improve. Those assets are updated as well.
    if old.source_hash == new.source_hash and old.attributes == new.attributes:
        result.unchanged.append((old, new))
    else:
        result.updated.append((old, new))


def _match_by_external_id(existing: list[ExistingAsset], incoming: list[NormalizedAsset]) -> MatchResult:
    seen: set[str] = set()
    for asset in incoming:
        if not asset.external_id:
            raise MatchingError("Adapter uses external ids but a record has none")
        if asset.external_id in seen:
            raise MatchingError(f"Duplicate external id in source: {asset.external_id}")
        seen.add(asset.external_id)

    by_id = {e.external_id: e for e in existing}
    result = MatchResult()
    for asset in incoming:
        old = by_id.pop(asset.external_id, None)
        if old is None:
            result.created.append(asset)
        else:
            _pair(result, old, asset)
    result.removed.extend(by_id.values())
    return result


def _match_spatially(
    existing: list[ExistingAsset], incoming: list[NormalizedAsset], radius_m: float
) -> MatchResult:
    result = MatchResult()

    # 1. Identical records keep their asset. Covers nearly everything on a
    #    normal day and is independent of the radius.
    by_hash: dict[str, list[ExistingAsset]] = defaultdict(list)
    for old in existing:
        by_hash[old.source_hash].append(old)
    rest_new: list[NormalizedAsset] = []
    for asset in incoming:
        candidates = by_hash.get(asset.source_hash)
        if candidates:
            _pair(result, candidates.pop(), asset)
        else:
            rest_new.append(asset)
    rest_old = [old for olds in by_hash.values() for old in olds]

    # 2. Changed records (moved slightly or new attributes): nearest unmatched
    #    asset within the radius. Closest pairs are assigned first, each asset
    #    and each record at most once.
    if rest_new and rest_old:
        new_pts = [(a.lon, a.lat) for a in rest_new]
        old_pts = [(o.lon, o.lat) for o in rest_old]
        projection = LocalProjection.around(new_pts + old_pts)
        pairs = sorted(pairs_within(new_pts, old_pts, radius_m, projection), key=lambda p: p[2])
        used_new: set[int] = set()
        used_old: set[int] = set()
        for i, j, _ in pairs:
            if i in used_new or j in used_old:
                continue
            used_new.add(i)
            used_old.add(j)
            _pair(result, rest_old[j], rest_new[i])
        rest_new = [a for i, a in enumerate(rest_new) if i not in used_new]
        rest_old = [o for j, o in enumerate(rest_old) if j not in used_old]

    # 3. Whatever is left is new or gone.
    result.created.extend(rest_new)
    result.removed.extend(rest_old)
    return result
