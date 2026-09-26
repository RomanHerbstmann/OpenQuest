"""Names an area (district, quarter, ...) for each asset from any polygon GeoJSON.

Generic: configure the file, the property that holds the name and the
attribute to set, e.g. Münster's Stadtbezirke → ``district``.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Any, Mapping

from openquest_importer.adapters.base import AdapterError, NormalizedAsset, SnapshotFile
from openquest_importer.adapters.boundary import parse_named_areas
from openquest_importer.adapters.http import read_source
from openquest_importer.enrichers.base import Enricher, EnricherError

log = logging.getLogger(__name__)


class AreaNameEnricher(Enricher):
    """Options (all required except ``file``):

    ``url`` / ``file``
        GeoJSON with Polygon / MultiPolygon features in WGS84.
    ``name_field``
        Feature property that holds the name, e.g. ``NAME_STADT``.
    ``attribute``
        Attribute to set, e.g. ``district``.
    """

    # Set per instance from the options; the class default is for listing only.
    attributes = frozenset({"<attribute option>"})

    def __init__(self, options: Mapping[str, Any] | None = None, cache_dir: Path | None = None) -> None:
        super().__init__(options, cache_dir)
        missing = [k for k in ("name_field", "attribute") if not self.options.get(k)]
        if missing or not (self.options.get("url") or self.options.get("file")):
            raise EnricherError(f"geo.area_name needs url or file, name_field and attribute (missing: {missing or ['url']})")
        self.attribute = str(self.options["attribute"])
        self.attributes = frozenset({self.attribute})

    def enrich(self, assets: list[NormalizedAsset]) -> SnapshotFile | None:
        try:
            content = read_source(self.options, "file", str(self.options.get("url", "")))
            index = parse_named_areas(content, str(self.options["name_field"]), self.attribute)
        except AdapterError as exc:
            raise EnricherError(str(exc)) from exc
        found = 0
        for asset in assets:
            name = index.find(asset.lon, asset.lat)
            asset.attributes[self.attribute] = name
            found += name is not None
        log.info("Areas for %s: %d of %d assets", self.attribute, found, len(assets))
        return SnapshotFile(content=content, extension="geojson")
