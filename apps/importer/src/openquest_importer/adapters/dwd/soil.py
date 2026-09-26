"""Daily soil moisture and evaporation of a DWD station (agrometeorological model AMBAV).

Source: https://opendata.dwd.de/climate_environment/CDC/derived_germany/soil/daily/recent/
(one gzipped ``;``-separated file per station, the current year and the past
months). Licence: GeoNutzV (attribution "Quelle: Deutscher Wetterdienst").
For Münster the nearest station is 1766 Münster/Osnabrück (airport, ~20 km).

Soil moisture is modelled for grass on sand and on loamy silt, 0–60 cm, in
percent of the usable field capacity (%nFK). Below ~30–40 %nFK young trees
need water, which is what "water this tree" quests can be triggered by.
"""

from __future__ import annotations

import gzip
import io
from datetime import datetime, timezone

from openquest_importer.adapters.base import (
    AdapterError,
    ParsedReadings,
    Reading,
    ReadingAdapter,
    Snapshot,
    SnapshotFile,
)
from openquest_importer.adapters.http import read_source

BASE_URL = "https://opendata.dwd.de/climate_environment/CDC/derived_germany/soil/daily/recent"
MISSING = -999.0

EXPECTED_FIELDS = frozenset({
    "Stationsindex", "Datum", "TS05", "TS10", "TS20", "TS50", "TS100", "TSLS05", "TSSL05", "ZFUMI", "ZTKMI", "ZTUMI",
    "BFGL01_AG", "BFGL02_AG", "BFGL03_AG", "BFGL04_AG", "BFGL05_AG", "BFGL06_AG", "BFGS_AG", "BFGL_AG", "BFWS_AG",
    "BFWL_AG", "BFMS_AG", "BFML_AG", "VPGFAO", "VPGH", "VRGS_AG", "VRGL_AG", "VRWS_AG", "VRWL_AG", "VRMS_AG",
    "VRML_AG", "eor",
})

#: Source column → (metric, unit). Override with the ``metrics`` option.
DEFAULT_METRICS: dict[str, tuple[str, str]] = {
    "BFGS_AG": ("soil_moisture_grass_sand_0_60cm", "%nFK"),
    "BFGL_AG": ("soil_moisture_grass_loam_0_60cm", "%nFK"),
    "VPGFAO": ("evapotranspiration_potential_fao", "mm"),
    "VRGS_AG": ("evapotranspiration_real_grass_sand", "mm"),
    "TS05": ("soil_temperature_5cm", "°C"),
}


class DwdSoilDailyAdapter(ReadingAdapter):
    """Options:

    ``station_id``
        DWD station, e.g. 1766 (required).
    ``metrics``
        Table source column → ``[metric, unit]``; default soil moisture, evaporation and soil temperature.
    ``base_url``
        Directory of the daily files, default the DWD open data server.
    ``file`` / ``stations_file``
        Local copies of the station file (``.txt.gz``) and the station list, instead of downloading.
    ``timeout_s``
        Download timeout in seconds, default 120.
    """

    expected_fields = EXPECTED_FIELDS

    @property
    def station_id(self) -> str:
        station = str(self.options.get("station_id", "")).strip()
        if not station:
            raise AdapterError("Option station_id is required")
        return station

    def _metrics(self) -> dict[str, tuple[str, str]]:
        configured = self.options.get("metrics")
        if not configured:
            return DEFAULT_METRICS
        return {column: (str(value[0]), str(value[1])) for column, value in dict(configured).items()}

    def fetch(self) -> Snapshot:
        base = str(self.options.get("base_url", BASE_URL)).rstrip("/")
        return Snapshot(
            content=read_source(self.options, "file", f"{base}/derived_germany_soil_daily_recent_v2_{self.station_id}.txt.gz"),
            extension="txt.gz",
            extras={"stations": SnapshotFile(
                content=read_source(self.options, "stations_file", f"{base}/derived_germany_soil_daily_recent_stations_list.txt"),
                extension="txt",
            )},
        )

    def parse(self, snapshot: Snapshot) -> ParsedReadings:
        try:
            text = gzip.decompress(snapshot.content).decode("latin-1")
        except (OSError, EOFError) as exc:
            raise AdapterError(f"Station file is not gzip: {exc}") from exc
        lines = [line for line in text.splitlines() if line.strip()]
        if not lines:
            raise AdapterError("Station file is empty")
        header = [h.strip() for h in lines[0].split(";")]
        fields = frozenset(header)
        metrics = self._metrics()
        missing_columns = set(metrics) - fields
        if missing_columns:
            return ParsedReadings(fields=fields)  # the sync reports the changed fields

        lon, lat = self._station_position(snapshot)
        readings: list[Reading] = []
        for number, line in enumerate(lines[1:], start=2):
            values = dict(zip(header, (v.strip() for v in line.split(";"))))
            if values.get("Stationsindex") != self.station_id:
                raise AdapterError(f"Line {number}: station {values.get('Stationsindex')!r}, expected {self.station_id}")
            try:
                day = datetime.strptime(values["Datum"], "%Y%m%d").replace(tzinfo=timezone.utc)
            except (KeyError, ValueError) as exc:
                raise AdapterError(f"Line {number}: invalid date {values.get('Datum')!r}") from exc
            for column, (metric, unit) in metrics.items():
                try:
                    value = float(values[column])
                except (KeyError, ValueError) as exc:
                    raise AdapterError(f"Line {number}: invalid value in {column}") from exc
                if value <= MISSING:
                    continue
                readings.append(Reading(station_id=self.station_id, metric=metric, value=value, unit=unit,
                                        measured_at=day, lon=lon, lat=lat))
        return ParsedReadings(fields=fields, readings=readings)

    def _station_position(self, snapshot: Snapshot) -> tuple[float | None, float | None]:
        stations = snapshot.extras.get("stations")
        if stations is None:
            return None, None
        for line in io.StringIO(stations.content.decode("latin-1")):
            parts = [p.strip() for p in line.split(";")]
            if len(parts) >= 4 and parts[0] == self.station_id:
                try:
                    return float(parts[3]), float(parts[2])  # columns: id; height; latitude; longitude; ...
                except ValueError:
                    return None, None
        return None, None
