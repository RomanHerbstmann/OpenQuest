# ADR-0006: Eigene Asset-ID und räumliches Matching beim Import

| Feld | Wert |
|---|---|
| Status | Angenommen |
| Datum | 2026-09-25 |
| Entscheider | Team OpenQuest (Hackathon) |
| Ersetzt | Abschnitt 2, Punkt „Eigene deterministische `tree_id`“ aus [ADR-0001](0001-baumkataster-datenbezug-und-rueckkanal.md) |

## Kontext

Das Baumkataster Münster hat keine stabile ID (siehe [Research Note 0001](../research/0001-opendata-muenster-baumkataster.md)). ADR-0001 schlägt eine deterministische `tree_id` vor: ein Hash aus Koordinate, `str_schl` und `baumgruppe`.

Für OpenQuest reicht das nicht. Quests, Fotos und Änderungsvorschläge hängen an einem Baum. Sobald sich ein Hash-Bestandteil ändert, entsteht eine neue ID, und der Baum verliert alles, was an ihm hängt. Genau das passiert, wenn die Stadt eine von Spielern korrigierte Gattung übernimmt, also im Erfolgsfall des Projekts.

## Entscheidung

1. **Wir vergeben die ID selbst.** `asset.id` ist eine UUID, die beim ersten Import entsteht und sich danach nie ändert. Sie hängt nicht von Werten der Quelle ab.
2. **Beim erneuten Import ordnen wir die Datensätze bestehenden Assets zu:**
   1. Ein identischer Datensatz (gleicher `source_hash` aus Geometrie und Eigenschaften) behält sein Asset.
   2. Übrige Datensätze werden dem nächstgelegenen noch freien Asset im Umkreis von `match_radius_m` zugeordnet (Standard 1 m). Die nächsten Paare werden zuerst vergeben, jedes Asset und jeder Datensatz höchstens einmal.
   3. Übrig gebliebene Datensätze werden neue Assets. Übrig gebliebene Assets werden als `removed_at_source` markiert, nicht gelöscht.
3. **Quellen mit eigenen stabilen IDs** werden über `asset.external_id` zugeordnet. Jeder Adapter legt fest, welche Strategie gilt (`Identity.SPATIAL` oder `Identity.EXTERNAL_ID`). Quellen ohne eigene IDs (Münster) bekommen unsere eigene ID auch als `external_id`: Die Spalte ist im Schema der API Pflicht und pro Datenquelle eindeutig, und Exporte an die Stadt referenzieren Bäume darüber. Der Wert ist genauso stabil wie `asset.id`.
4. **Schutz vor fehlerhaften Downloads:** Würde ein Import mehr als `max_removal_ratio` (Standard 20 %) der aktiven Assets entfernen, bricht er ab. Mit `--force` lässt er sich bewusst durchführen.

Umgesetzt im Importer unter `apps/importer` (`matching.py`), beschrieben in [docs/data-model/erd.md](../data-model/erd.md).

## Betrachtete Alternativen

| Alternative | Warum nicht |
|---|---|
| Hash aus Koordinate + `str_schl` + `baumgruppe` (ADR-0001) | Neue ID bei jeder Korrektur von Gattung oder Straßenschlüssel |
| Hash nur aus der Koordinate | Neue ID, sobald die Stadt einen Baum auch nur um Zentimeter verschiebt |
| Interne Baumnummer der Stadt | Ist im Export nicht enthalten; wir fragen bei der Stadt danach. Sobald es sie gibt, wechselt der Adapter auf `Identity.EXTERNAL_ID` |

## Konsequenzen

**Positiv**
- Quests, Fotos und Änderungsvorschläge bleiben am Baum, auch wenn die Stadt Daten korrigiert.
- Unabhängig von der Quelle: dasselbe Verfahren funktioniert für andere Städte und Datensätze.
- Entfernte Bäume bleiben mit Verlauf erhalten (`asset_snapshot`).

**Negativ**
- Die ID ist nur innerhalb unserer Datenbank stabil. Wer Daten mit uns austauscht, braucht unsere ID; Exporte enthalten sie als `external_id`.
- Wird ein Baum mehr als 1 m verschoben, gilt er als entfernt und neu angelegt. Der Radius ist pro Datenquelle einstellbar.
- Stehen zwei Bäume dicht beieinander und ändern sich beide gleichzeitig, kann die Zuordnung vertauschen. Die Research Note zählt 48 solcher Paare unter 1 m; sie tragen das Flag `near_duplicate`.
