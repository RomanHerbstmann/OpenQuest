"""Legally protected avenues from the Alleenkataster NRW (LINFOS, LANUK NRW).

WFS https://www.wfs.nrw.de/umwelt/linfos, feature type ``wfs_linfos:alleen_polyline``
(GML 3.2 only, EPSG:25832), licence dl-de/zero-2.0. Each avenue is a line with
``kennung`` (e.g. AL-MS-9004), ``objektbezeichnung``, ``liste_baumarten`` and a
link to its report. Trees within ``distance_m`` of an avenue line get its id and
name; avenues are protected by § 41 LNatSchG NRW.
"""

from __future__ import annotations

import json
import logging
import urllib.parse
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Mapping

from openquest_importer.adapters.base import NormalizedAsset, SnapshotFile
from openquest_importer.adapters.http import download
from openquest_importer.enrichers.base import Enricher, EnricherError
from openquest_importer.geo import SegmentIndex, to_utm

log = logging.getLogger(__name__)

WFS_URL = "https://www.wfs.nrw.de/umwelt/linfos"
TYPE_NAME = "wfs_linfos:alleen_polyline"


def parse_avenues(gml: bytes) -> list[dict[str, Any]]:
    """Avenues from a WFS GetFeature response: id, name, species and lines in EPSG:25832."""
    try:
        root = ET.fromstring(gml)
    except ET.ParseError as exc:
        raise EnricherError(f"Alleenkataster response is not valid XML: {exc}") from exc
    if root.tag.endswith("ExceptionReport"):
        raise EnricherError(f"Alleenkataster WFS error: {ET.tostring(root, encoding='unicode')[:300]}")
    avenues = []
    for member in root:
        if not member.tag.endswith("member") or not len(member):
            continue
        feature = member[0]
        props = {child.tag.split("}")[-1]: (child.text or "").strip() for child in feature}
        lines = []
        for pos_list in feature.iter():
            if pos_list.tag.endswith("posList") and pos_list.text:
                values = [float(v) for v in pos_list.text.split()]
                lines.append(list(zip(values[0::2], values[1::2])))
        if props.get("kennung") and lines:
            avenues.append({"id": props["kennung"], "name": props.get("objektbezeichnung") or None,
                            "species": props.get("liste_baumarten") or None, "lines": lines})
    return avenues


class AlleenEnricher(Enricher):
    """Sets ``avenue_id`` and ``avenue_name``. Options:

    ``wfs_url``, ``type_name``
        Default the LINFOS WFS and ``wfs_linfos:alleen_polyline``.
    ``distance_m``
        Maximum distance of a tree from the avenue line, default 10.
    ``file``
        Read a saved GetFeature response (GML) instead of querying the WFS.
    """

    attributes = frozenset({"avenue_id", "avenue_name"})

    def __init__(self, options: Mapping[str, Any] | None = None, cache_dir: Path | None = None) -> None:
        super().__init__(options, cache_dir)
        self.distance_m = float(self.options.get("distance_m", 10))

    def _fetch(self, assets: list[NormalizedAsset]) -> bytes:
        path = self.options.get("file")
        if path:
            return Path(path).read_bytes()
        xs, ys = zip(*(to_utm(a.lat, a.lon) for a in assets))
        margin = self.distance_m + 1
        query = urllib.parse.urlencode({
            "SERVICE": "WFS", "VERSION": "2.0.0", "REQUEST": "GetFeature",
            "TYPENAMES": self.options.get("type_name", TYPE_NAME), "COUNT": 100000,
            "BBOX": f"{min(xs) - margin:.0f},{min(ys) - margin:.0f},{max(xs) + margin:.0f},{max(ys) + margin:.0f},"
                    "urn:ogc:def:crs:EPSG::25832",
        })
        try:
            return download(f"{self.options.get('wfs_url', WFS_URL)}?{query}", float(self.options.get("timeout_s", 120)))
        except Exception as exc:
            raise EnricherError(f"Alleenkataster download failed: {exc}") from exc

    def enrich(self, assets: list[NormalizedAsset]) -> SnapshotFile | None:
        if not assets:
            return None
        content = self._fetch(assets)
        avenues = {a["id"]: a for a in parse_avenues(content)}
        index = SegmentIndex([(avenue_id, line) for avenue_id, a in avenues.items() for line in a["lines"]],
                             self.distance_m)
        matched = 0
        for asset in assets:
            hit = index.nearest(*to_utm(asset.lat, asset.lon))
            avenue = avenues[hit[0]] if hit else None
            asset.attributes["avenue_id"] = avenue["id"] if avenue else None
            asset.attributes["avenue_name"] = avenue["name"] if avenue else None
            matched += avenue is not None
        log.info("Alleenkataster: %d of %d assets in one of %d protected avenues", matched, len(assets), len(avenues))
        return SnapshotFile(content=content, extension="gml")
