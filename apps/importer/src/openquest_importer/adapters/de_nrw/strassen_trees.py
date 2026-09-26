"""Trees along federal and state roads, Straßen.NRW "Fachschale Baum".

Source: https://opendata.strassen.nrw.de/FS_Baum/ (open.nrw record "Fachschale
Baum"), licence dl-de/zero-2.0. One CSV for all of NRW (~416,000 trees), ``;``
separated, decimal commas, UTF-8 with BOM, columns ``Baumart`` (German common
name, e.g. "Linde"), ``Rechtswert (m)`` and ``Hochwert (m)`` in EPSG:25832.

The adapter keeps the trees inside a boundary (for Münster: the Stadtbezirke)
and maps the German names to Latin genera. The file has no ids, so trees are
matched spatially like the Münster inventory. It complements the city's
inventory, which only covers trees owned by the city.
"""

from __future__ import annotations

import csv
import io
import logging
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
from openquest_importer.adapters.de_common.genera import genus_for_common_name
from openquest_importer.adapters.http import read_source
from openquest_importer.geo import from_utm

log = logging.getLogger(__name__)

DEFAULT_URL = "https://opendata.strassen.nrw.de/FS_Baum/OpenData2025BaumDaten.csv"
FIELD_GENUS, FIELD_X, FIELD_Y = "Baumart", "Rechtswert (m)", "Hochwert (m)"


class StrassenNrwTreesAdapter(DataSourceAdapter):
    """Options:

    ``url`` / ``file``
        CSV of Straßen.NRW, or a local copy.
    ``boundary_url`` / ``boundary_file``
        GeoJSON polygons (WGS84) of the area to keep. Required: the CSV covers all of NRW.
    ``timeout_s``
        Download timeout in seconds, default 120.
    """

    asset_type = "tree"
    identity = Identity.SPATIAL
    expected_fields = frozenset({FIELD_GENUS, FIELD_X, FIELD_Y})

    def fetch(self) -> Snapshot:
        if not (self.options.get("boundary_url") or self.options.get("boundary_file")):
            raise AdapterError("Option boundary_url or boundary_file is required (the CSV covers all of NRW)")
        return Snapshot(
            content=read_source(self.options, "file", self.options.get("url", DEFAULT_URL)),
            extension="csv",
            extras={"boundary": SnapshotFile(
                content=read_source(self.options, "boundary_file", self.options.get("boundary_url", "")),
                extension="geojson",
            )},
        )

    def parse(self, snapshot: Snapshot) -> ParsedSnapshot:
        if "boundary" not in snapshot.extras:
            raise AdapterError("Snapshot lacks the boundary file")
        boundary = parse_boundary(snapshot.extras["boundary"].content)
        min_x, min_y, max_x, max_y = boundary.utm_bbox

        try:
            text = snapshot.content.decode("utf-8-sig")
        except UnicodeDecodeError as exc:
            raise AdapterError(f"CSV is not UTF-8: {exc}") from exc
        reader = csv.DictReader(io.StringIO(text), delimiter=";")
        fields = frozenset(reader.fieldnames or [])
        if not self.expected_fields <= fields:
            return ParsedSnapshot(fields=fields)  # the sync reports the changed fields

        assets: list[NormalizedAsset] = []
        unmapped: dict[str, int] = {}
        for line, row in enumerate(reader, start=2):
            try:
                x = float(row[FIELD_X].replace(",", "."))
                y = float(row[FIELD_Y].replace(",", "."))
            except (TypeError, ValueError) as exc:
                raise AdapterError(f"Line {line}: invalid coordinates {row[FIELD_X]!r}, {row[FIELD_Y]!r}") from exc
            if not (min_x <= x <= max_x and min_y <= y <= max_y):
                continue
            lat, lon = from_utm(x, y)
            if not boundary.contains(lon, lat):
                continue

            name = (row[FIELD_GENUS] or "").strip()
            genus, known = genus_for_common_name(name)
            if not known:
                unmapped[name] = unmapped.get(name, 0) + 1
            raw: dict[str, Any] = {key: row[key] for key in (FIELD_GENUS, FIELD_X, FIELD_Y)}
            assets.append(NormalizedAsset(
                lon=lon,
                lat=lat,
                attributes={
                    "genus": genus,
                    "genus_raw": name or None,
                    "species": None,
                    "quality_flags": [] if genus else ["ambiguous_genus"],
                },
                raw=raw,
                source_hash=record_hash(raw),
            ))
        if unmapped:
            log.warning("Straßen.NRW: no genus for common names %s; add them to de_common/genera.py", unmapped)
        return ParsedSnapshot(fields=fields, assets=assets)
