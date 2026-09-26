"""Natural monuments (Naturdenkmale) of the city of Münster.

The register is only published as a WMS (``naturschutz_serv``, layer
``naturschutz2``), not as a WFS or in the open data portal. **Its licence is
not stated**: ask the Amt für Grünflächen before publishing the data; the
source is disabled in ``importer.toml`` until then.

Harvesting with WMS GetFeatureInfo on a grid over the boundary: queries at
~68 m per pixel (just inside the layer's 1:250,000 scale limit) cover a radius
of well over 400 m, so a 500 m grid finds every monument (~2,800 cells for
Münster). Each cell is queried twice with identical parameters:

- GML returns the bounding box of every monument, but no attributes;
- HTML returns the attributes (Nummer, Beschreibung, Höhe, Umfang, Krone, Lage,
  two history texts), but no position.

MapServer returns both in the same order, so records and boxes are paired by
position in the response (checked when the adapter was written). The snapshot
is a JSON bundle of the raw responses of all cells that contain monuments. Monuments in the outer areas
(Landschaftspläne, layers ``LP*_13/15`` of ``landschaftsplanung_serv``) are only
visible up to 1:25,000 and would need >100,000 requests; they are not covered.
"""

from __future__ import annotations

import json
import logging
import math
import re
import urllib.parse
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor
from html.parser import HTMLParser
from typing import Any

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
from openquest_importer.adapters.boundary import parse_boundary
from openquest_importer.adapters.de_common.genera import genera_in_text
from openquest_importer.adapters.http import download, read_source
from openquest_importer.geo import from_utm

log = logging.getLogger(__name__)

WMS_URL = "https://geo.stadt-muenster.de/mapserv/naturschutz_serv"
LAYER = "naturschutz2"
DISTRICTS_URL = "https://opendata.stadt-muenster.de/sites/default/files/stadtbezirke-muenster.geojson"

# Labels in the HTML response → attribute.
LABELS = {
    "Nummer": "monument_number",
    "Beschreibung": "description",
    "Höhe": "height_m",
    "Umfang": "circumference_m",
    "Krone": "crown_diameter_m",
    "Lage": "location",
    "Historischer Bezug": "historical_context",
    "Städtebaulicher und landschaftlicher Bezug": "landscape_context",
}
NUMERIC = {"height_m", "circumference_m", "crown_diameter_m"}


class _RecordParser(HTMLParser):
    """Collects 'Label:' / value cell pairs from the WMS HTML template, one record per 'Nummer'."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.records: list[dict[str, str]] = []
        self._cells: list[str] = []
        self._in_cell = False
        self._text: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag in ("td", "th"):
            self._in_cell, self._text = True, []
        elif tag == "tr":
            self._cells = []

    def handle_endtag(self, tag: str) -> None:
        if tag in ("td", "th") and self._in_cell:
            self._in_cell = False
            self._cells.append(" ".join("".join(self._text).split()))
        elif tag == "tr":
            cells = [c for c in self._cells if c]
            if len(cells) >= 2 and cells[0].endswith(":"):
                label = cells[0][:-1].strip()
                if label == "Nummer" or not self.records:
                    self.records.append({})
                self.records[-1][label] = cells[1]

    def handle_data(self, data: str) -> None:
        if self._in_cell:
            self._text.append(data)


def parse_records(html: str) -> list[dict[str, str]]:
    parser = _RecordParser()
    parser.feed(html)
    return [r for r in parser.records if r.get("Nummer")]


def parse_measure(text: str | None) -> float | None:
    """'30,5' → 30.5; ranges like '14,5 - 24,0' (groups) → the largest value; no number → None."""
    numbers = [float(n.replace(",", ".")) for n in re.findall(r"\d+(?:[.,]\d+)?", text or "")]
    return max(numbers) if numbers else None


def parse_boxes(gml: str) -> list[tuple[float, float, float, float]]:
    try:
        root = ET.fromstring(gml)
    except ET.ParseError as exc:
        raise AdapterError(f"GetFeatureInfo GML is not valid XML: {exc}") from exc
    boxes = []
    for element in root.iter():
        if element.tag.endswith("coordinates") and element.text:
            (x1, y1), (x2, y2) = (tuple(map(float, pair.split(","))) for pair in element.text.split())
            boxes.append((x1, y1, x2, y2))
    return boxes


class MuensterNaturalMonumentsAdapter(DataSourceAdapter):
    """Options:

    ``wms_url``, ``layer``
        Default the city's ``naturschutz_serv`` / ``naturschutz2``.
    ``boundary_url`` / ``boundary_file``
        Area to scan (default: Münster's Stadtbezirke GeoJSON).
    ``grid_m``
        Distance of the grid cells, default 500.
    ``concurrency``
        Parallel requests, default 4.
    ``file``
        Read a saved harvest bundle (JSON) instead of querying the WMS.
    """

    asset_type = "natural_monument"
    identity = Identity.EXTERNAL_ID
    expected_fields = frozenset(LABELS)

    def fetch(self) -> Snapshot:
        boundary_bytes = read_source(self.options, "boundary_file", self.options.get("boundary_url", DISTRICTS_URL))
        path = self.options.get("file")
        if path:
            with open(path, "rb") as f:
                content = f.read()
        else:
            content = json.dumps(self._harvest(boundary_bytes), ensure_ascii=False, sort_keys=True).encode()
        return Snapshot(content=content, extension="json",
                        extras={"boundary": SnapshotFile(content=boundary_bytes, extension="geojson")})

    def _url(self, **params: Any) -> str:
        base = {"SERVICE": "WMS", "VERSION": "1.3.0", "REQUEST": "GetFeatureInfo", "CRS": "EPSG:25832",
                "LAYERS": self.options.get("layer", LAYER), "QUERY_LAYERS": self.options.get("layer", LAYER),
                "STYLES": "", "FEATURE_COUNT": 1000}
        return f"{self.options.get('wms_url', WMS_URL)}?{urllib.parse.urlencode({**base, **params})}"

    def _query(self, x: float, y: float, half_m: float, pixels: int, info_format: str) -> str:
        url = self._url(BBOX=f"{x - half_m},{y - half_m},{x + half_m},{y + half_m}", WIDTH=pixels, HEIGHT=pixels,
                        I=pixels // 2, J=pixels // 2, INFO_FORMAT=info_format)
        return download(url, float(self.options.get("timeout_s", 60))).decode("utf-8", "replace")

    def _harvest(self, boundary_bytes: bytes) -> dict[str, Any]:
        boundary = parse_boundary(boundary_bytes)
        min_x, min_y, max_x, max_y = boundary.utm_bbox
        step = float(self.options.get("grid_m", 500))
        centres = [(min_x + (i + 0.5) * step, min_y + (j + 0.5) * step)
                   for i in range(math.ceil((max_x - min_x) / step))
                   for j in range(math.ceil((max_y - min_y) / step))]
        log.info("Natural monuments: querying %d grid cells", len(centres))

        def cell(centre: tuple[float, float]) -> dict[str, Any] | None:
            # 68 m per pixel: 100 px over 6.8 km, just inside the 1:250,000 scale limit.
            gml = self._query(centre[0], centre[1], 3400, 100, "application/vnd.ogc.gml")
            if not parse_boxes(gml):
                return None
            html = self._query(centre[0], centre[1], 3400, 100, "text/html")
            return {"centre": list(centre), "gml": gml, "html": html}

        with ThreadPoolExecutor(max_workers=int(self.options.get("concurrency", 4))) as pool:
            cells = [c for c in pool.map(cell, centres) if c is not None]
        return {"source": self.options.get("wms_url", WMS_URL), "layer": self.options.get("layer", LAYER),
                "grid_m": step, "cells": cells}

    def parse(self, snapshot: Snapshot) -> ParsedSnapshot:
        try:
            bundle = json.loads(snapshot.content)
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise AdapterError(f"Snapshot is not valid JSON: {exc}") from exc
        boundary = parse_boundary(snapshot.extras["boundary"].content) if "boundary" in snapshot.extras else None

        fields: set[str] = set()
        assets: dict[str, NormalizedAsset] = {}
        for cell in bundle.get("cells", []):
            boxes = parse_boxes(cell.get("gml", ""))
            records = parse_records(cell.get("html", ""))
            if len(boxes) != len(records):
                raise AdapterError(
                    f"Cell {cell.get('centre')}: {len(boxes)} locations but {len(records)} records; "
                    "the WMS no longer returns GML and HTML in the same order"
                )
            for box, record in zip(boxes, records):
                fields.update(record)
                number = record["Nummer"]
                if number in assets:
                    continue  # neighbouring cells overlap
                x1, y1, x2, y2 = box
                lat, lon = from_utm((x1 + x2) / 2, (y1 + y2) / 2)
                if boundary is not None and not boundary.contains(lon, lat):
                    continue
                attributes: dict[str, Any] = {}
                for label, key in LABELS.items():
                    value = record.get(label) or None
                    attributes[key] = parse_measure(value) if key in NUMERIC else value
                genera = genera_in_text(attributes["description"])
                attributes["genus"] = next(iter(genera)) if len(genera) == 1 else None
                attributes["quality_flags"] = ["ambiguous_genus"] if len(genera) > 1 or None in genera else []
                raw = {"bbox": list(box), "record": record}
                assets[number] = NormalizedAsset(lon=lon, lat=lat, attributes=attributes, raw=raw,
                                                 source_hash=record_hash(raw), external_id=number)
        return ParsedSnapshot(fields=frozenset(fields) if assets else self.expected_fields,
                              assets=sorted(assets.values(), key=lambda a: a.external_id or ""))
