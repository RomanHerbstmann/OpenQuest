# ADR-0001: Baumkataster direkt vom WFS beziehen, Rückkanal über GitHub und Open Data Koordination

| Feld | Wert |
|---|---|
| Status | Vorgeschlagen |
| Datum | 2026-09-25 |
| Entscheider | Team OpenQuest (Hackathon) |
| Grundlage | [Research Note 0001](../research/0001-opendata-muenster-baumkataster.md) (Recherche mit Belegen) |

## Kontext

Wir wollen das Baumkataster der Stadt Münster auf eine Karte bringen, in einer Datenbank ablegen, aufbereiten und das Ergebnis der Stadt zurückgeben. Die Recherche (Research Note 0001) hat ergeben:

- opendata.stadt-muenster.de ist **DKAN 7 auf Drupal 7** (kein CKAN), mit eingeschränkter, langsamer Lese-API, **ohne Schreib-API und ohne Selbstregistrierung**. Inhalte pflegt ausschließlich die Redaktion der citeq.
- Das Baumkataster liegt nicht im Portal, sondern wird **live aus dem MapServer-WFS** `https://geo.stadt-muenster.de/mapserv/odgruen_serv` (Layer `Baeume`) ausgeliefert. CORS ist offen.
- Die Daten umfassen 43.114 Punkte mit nur `str_schl` und `baumgruppe`, **ohne stabile ID**, mit ca. 8 % Platzhaltern statt Gattung und Datenstand 2017/2020.
- Lizenz dl-de/by-2.0: Namensnennung Pflicht, kein Share-Alike.
- Die Stadt bindet externe Ergebnisse heute schon per Link ein (50 Ressourcen auf GitHub, Community-Apps unter „Anwendungen“).

## Entscheidung

### 1. Datenbezug

- Quelle ist der **WFS** `odgruen_serv`, nicht die DKAN-API.
  - Karte (Frontend): GeoJSON `...&OUTPUTFORMAT=geojson` (WGS84, lon/lat) oder WMS-Layer `Baeume`.
  - Datenbank (Backend): WFS mit `SRSNAME=EPSG:25832` (metrisch, Quell-CRS).
- Metadaten des Portals, falls benötigt, über `https://opendata.stadt-muenster.de/data.json`.
- Begleitdaten: Straßen (`odstrasseserv`, Join über `str_schl`), Stadtbezirke/-teile (GeoJSON im Portal), Grünflächen (`odgruen_serv`, Layer `Gruenflaechen`).

### 2. Speicherung und Pipeline

- **PostGIS** als Datenbank.
- Loader zieht **Snapshots** (manuell bzw. täglich) und speichert sie versioniert (`snapshot_id`, `fetched_at`).
- Eigene deterministische **`tree_id`**: Hash aus Koordinate (EPSG:25832, auf 0,1 m gerundet), aufgefülltem `str_schl` und Roh-`baumgruppe`. Die Rohwerte bleiben unverändert in einer Raw-Tabelle, Bereinigung erfolgt in einer abgeleiteten Tabelle/View.

### 3. Aufbereitung

- `str_schl` als String, auf 5 Stellen mit führenden Nullen auffüllen.
- `baumgruppe`: Platzhalter (`Baum Amt62`, `Baumgruppe`, `Standort`, `Leerer*`, `Unbekannt`, leer) auf `null` mit Qualitäts-Flag; Tippfehler per Mapping-Tabelle korrigieren; Gattung und Art getrennt ablegen.
- Flag für Beinahe-Dubletten (< 1 m Abstand).
- Anreicherung: Straßenname, Stadtbezirk, Stadtteil.

### 4. Rückkanal

Ein direktes Zurückschreiben ins Portal ist nicht möglich. Wir geben zurück über:

1. **Öffentliches GitHub-Repo** mit Code, bereinigtem GeoJSON/CSV unter stabiler URL und Karte auf GitHub Pages.
2. **Mail an opendata@citeq.de** mit Link, Beschreibung, Screenshot und Liste der gefundenen Datenfehler; Bitte um Aufnahme unter „Anwendungen“ und Verlinkung des bereinigten Datensatzes als Ressource.
3. **PR an `codeformuenster/muensterhack`** (Projekteintrag 2026).
4. Datenfehler zusätzlich als **Kommentar am Datensatz** bzw. über `/daten/anfragen`.

Nicht gemacht werden: OSM-Massenimport, eigener DCAT-Katalog zum Harvesting durch Open.NRW/GovData.

### 5. Attribution

In README, Karte und jeder veröffentlichten Datei:

> Datenquelle: Stadt Münster, Digitales Baumkataster, dl-de/by-2-0 (https://www.govdata.de/dl-de/by-2-0), https://opendata.stadt-muenster.de/dataset/digitales-baumkataster-m%C3%BCnster. Daten bereinigt und angereichert durch Team OpenQuest.

Keine Logos oder Wappen der Stadt, kein amtlich wirkender Auftritt. Werden OSM-Daten (ODbL) eingemischt, steht das kombinierte Werk unter ODbL.

## Betrachtete Alternativen

| Alternative | Warum nicht |
|---|---|
| Daten über DKAN-API beziehen | API ist nur Metadaten-Hülle, langsam (> 60 s), Suche fehlt; Daten liegen ohnehin im WFS |
| Direkt ins Portal schreiben | keine Schreib-API, keine Registrierung |
| Eigener CKAN/DCAT-Katalog, von Open.NRW/GovData geharvestet | nur Behörden sind als Datenbereitsteller vorgesehen, am Hackathon nicht machbar |
| OSM-Import der Bäume | erfordert Import-Prozess mit Community-Zustimmung (Wochen); Daten zu alt und dünn für Blind-Import |
| Nur Karte, keine Datenbank | Snapshots/Diffs und Analysen (Joins, Dichte) wären nicht möglich |

## Konsequenzen

**Positiv**
- Unabhängig von der End-of-Life-Plattform Drupal 7/DKAN; wir nutzen das eigentliche Quellsystem.
- Rückkanal folgt dem Muster, das die Stadt selbst nutzt, und hat Präzedenzfälle („Wo stehen die Birken?“, MeineWaermeplanung.de #MSHACK23).
- Rohdaten bleiben unverändert erhalten, Bereinigung ist nachvollziehbar.

**Negativ**
- Ob unsere Daten im Portal verlinkt werden, entscheidet die citeq-Redaktion.
- `tree_id` ist heuristisch: verschiebt die Stadt einen Baum oder korrigiert die Gattung, entsteht eine neue ID.
- Der WFS kann sich ohne Ankündigung ändern; der Loader muss Schemaabweichungen erkennen und laut fehlschlagen.
- Datenbestand deckt nur ca. die Hälfte der städtischen Bäume ab (Stand 2017/2020); Karte und Analysen müssen das kenntlich machen.

## Offene Punkte

- Sichtung von `od-ms/converter-scripts` (Commit 08.09.2026 „beta version of tree data generation“): arbeitet die Stadt an einem neuen Baumdatensatz? Ggf. Kontakt aufnehmen, bevor wir die Bereinigung ausbauen.
- Repo-Sichtbarkeit (öffentlich) und Hosting der Karte klären.
