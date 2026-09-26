"""Reports from an Open311 GeoReport v2 endpoint, e.g. the Münster "Mängelmelder".

Münster runs its issue tracker on Beteiligung NRW
(https://beteiligung.nrw.de/api/rest/public/open311/v2/beteiligung/1003255),
licence dl-de/by-2.0 (city portal dataset "Daten des Mängelmelder Münster").
Tree related services: 1001742 "Baum" and 1001746 "Eichenprozessionsspinner".
The endpoint only returns the last 90 days, whatever window is requested.

Reports are free text from citizens. E-mail addresses and phone numbers are
removed from description and status notes before anything is stored; names
in free text can't be detected reliably, so the texts are only shown to admins.
"""

from __future__ import annotations

import json
import logging
import re
import urllib.parse
from datetime import datetime, timedelta, timezone
from typing import Any

from openquest_importer.adapters.base import (
    AdapterError,
    NormalizedReport,
    ParsedReports,
    ReportAdapter,
    Snapshot,
    record_hash,
)
from openquest_importer.adapters.http import download

log = logging.getLogger(__name__)

EXPECTED_FIELDS = frozenset({
    "service_request_id", "status", "status_notes", "service_name", "service_code", "description",
    "requested_datetime", "updated_datetime", "address", "zipcode", "lat", "long", "media_url",
})

_EMAIL = re.compile(r"[\w.+-]+@[\w-]+(\.[\w-]+)+")
# Phone numbers: +49 / 0 prefix, then digits with spaces, slashes, dashes or brackets.
_PHONE = re.compile(r"(?<![\w.])(?:\+\d{1,3}|0)[\d ()/-]{6,}\d")


def redact(text: str | None) -> str | None:
    """Remove e-mail addresses and phone numbers from citizen text."""
    if text is None:
        return None
    return _PHONE.sub("[Telefon entfernt]", _EMAIL.sub("[E-Mail entfernt]", text))


def _timestamp(value: str | None) -> datetime | None:
    if not value:
        return None
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise AdapterError(f"Invalid timestamp {value!r}") from exc


class Open311ReportsAdapter(ReportAdapter):
    """Options:

    ``base_url``
        Open311 endpoint up to (not including) ``/requests.json``.
    ``services``
        Table service code → category, e.g. ``{ "1001742" = "tree_damage" }``.
    ``days``
        How far back to request, default 90 (the Beteiligung NRW maximum).
    ``file``
        Read a saved snapshot (``{"<service code>": [requests...]}``) instead of the API.
    ``link_radius_m``
        Link a report to the nearest active asset within this distance, default 25.
    ``timeout_s``
        Per request, default 120.
    """

    link_asset_types = ("tree",)
    expected_fields = EXPECTED_FIELDS

    def _services(self) -> dict[str, str]:
        services = {str(code): str(category) for code, category in dict(self.options.get("services", {})).items()}
        if not services:
            raise AdapterError("Option services (service code → category) is required")
        return services

    def fetch(self) -> Snapshot:
        path = self.options.get("file")
        if path:
            with open(path, "rb") as f:
                return Snapshot(content=f.read(), extension="json")
        base = str(self.options.get("base_url", "")).rstrip("/")
        if not base:
            raise AdapterError("Option base_url is required")
        end = datetime.now(timezone.utc).replace(microsecond=0)
        start = end - timedelta(days=int(self.options.get("days", 90)))
        bundle: dict[str, Any] = {}
        for code in sorted(self._services()):
            query = urllib.parse.urlencode({
                "service_code": code,
                "start_date": start.isoformat().replace("+00:00", "Z"),
                "end_date": end.isoformat().replace("+00:00", "Z"),
            })
            content = download(f"{base}/requests.json?{query}", float(self.options.get("timeout_s", 120)))
            try:
                bundle[code] = json.loads(content)
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                raise AdapterError(f"Service {code}: response is not JSON: {exc}") from exc
        return Snapshot(content=json.dumps(bundle, ensure_ascii=False, sort_keys=True).encode(), extension="json")

    def parse(self, snapshot: Snapshot) -> ParsedReports:
        try:
            bundle = json.loads(snapshot.content)
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise AdapterError(f"Snapshot is not valid JSON: {exc}") from exc
        services = self._services()

        fields: set[str] = set()
        reports: list[NormalizedReport] = []
        for code, requests in sorted(bundle.items()):
            if code not in services:
                continue
            if not isinstance(requests, list):
                raise AdapterError(f"Service {code}: expected a list of requests, got {type(requests).__name__}")
            for request in requests:
                fields.update(request)
                status = str(request.get("status") or "").lower()
                if status not in ("open", "closed"):
                    log.warning("Report %s has unknown status %r, treated as open", request.get("service_request_id"), status)
                    status = "open"
                lat, lon = request.get("lat"), request.get("long")
                if lat is None or lon is None:
                    log.warning("Report %s has no position, skipped", request.get("service_request_id"))
                    continue
                raw = {**request, "description": redact(request.get("description")),
                       "status_notes": redact(request.get("status_notes"))}
                reports.append(NormalizedReport(
                    external_id=str(request["service_request_id"]),
                    category=services[code],
                    status=status,
                    lon=float(lon),
                    lat=float(lat),
                    reported_at=_timestamp(request.get("requested_datetime")) or datetime.now(timezone.utc),
                    source_updated_at=_timestamp(request.get("updated_datetime")),
                    description=raw["description"],
                    status_notes=raw["status_notes"],
                    address=request.get("address"),
                    media_url=request.get("media_url"),
                    raw=raw,
                    source_hash=record_hash(raw),
                ))
        return ParsedReports(fields=frozenset(fields) if reports else self.expected_fields, reports=reports)
