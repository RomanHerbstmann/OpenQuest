# MS Hack – Stadtbaum Quest

> **Status:** Konzept v0.1 · **Datum:** 25.09.2026  
> **Arbeitstitel:** Stadtbaum Quest (Name noch offen)  
> **Mission:** Die offenen Baumdaten der Stadt Münster durch spielerische Beiträge von Bürger:innen aktualisieren und verbessern.

## 1. Kurzidee

**Pokémon Go trifft Bürgerwissenschaft:** Menschen erkunden Münster, entdecken echte Stadtbäume und vervollständigen dabei ein digitales Baumbuch. Auf der Karte erscheinen Bäume als Fundorte und kleine Missionen. Wer einen Baum besucht, kann beispielsweise bestätigen, dass er noch existiert, seine Art bestimmen oder einen fehlenden Baum erfassen. Für hilfreiche, überprüfte Beiträge gibt es Erfahrungspunkte, Abzeichen und Fortschritt für das eigene Viertel.

**Produktprinzip:** Das aktive Erlebnis fühlt sich wie ein Entdeckungs- und Sammelspiel an. Schon beim Einstieg kommuniziert die App aber transparent, dass die Beobachtungen zur Verbesserung öffentlicher Baumdaten genutzt werden sollen.

## 2. Ausgangslage und Problem

- Der städtische Open-Data-Baumbestand kann veraltete, unvollständige oder ungenaue Einträge enthalten. **Welche Daten in Münster tatsächlich vorliegen, in welcher Qualität und unter welcher Lizenz, wird zuerst geprüft.**
- Eine flächendeckende Aktualisierung kostet Zeit und Personal.
- Bürger:innen haben vor Ort wertvolle Beobachtungen, aber bisher wenig Anreiz und keinen einfachen, spielerischen Weg, sie strukturiert beizutragen.
- Reine Meldungen sind nicht automatisch zuverlässig. Eine nachvollziehbare Prüfung ist Teil des Produkts.

**Erster Scope:** Öffentlich zugängliche Stadt- und Straßenbäume in einem abgegrenzten Münsteraner Pilotgebiet. Private Grundstücke, sensible Standorte und fachliche Baumkontrollen bleiben zunächst außen vor.

## 3. Zielbild

**Für Spieler:innen:** Draußen etwas entdecken, Arten kennenlernen, ein Baumbuch füllen, Quests lösen und gemeinsam Fortschritte im eigenen Viertel erzielen.

**Für die Stadt:** Aktuellere Baumstandorte, bestätigte oder korrigierte Baumarten, nachvollziehbare Änderungsbelege und ein prüfbarer Export, der sich später in die städtischen Prozesse integrieren lässt.

**Für MS Hack:** Ein in wenigen Minuten vorführbarer End-to-End-Prototyp: Baum auf der Karte finden → Mission abschließen → Beobachtung speichern → im Prüf-Dashboard sehen → exportieren.

## 4. Kernzielgruppen

1. **Gelegenheitsspieler:innen:** Spaziergang, Weg zur Arbeit, spontane 1–2-Minuten-Missionen.
2. **Naturinteressierte und Familien:** Baumarten entdecken, kleine Lernmomente und Sammelziele.
3. **Engagierte Bürger:innen:** Daten gezielt verbessern und lokale Fortschritte sichtbar machen.
4. **Städtische Mitarbeitende / Datenverantwortliche:** Beiträge prüfen, freigeben und für bestehende Datenbestände nutzbar machen.

## 5. Spielmechanik

### 5.1 Der zentrale Game Loop

1. **Entdecken:** Die Karte zeigt nahegelegene Baum-Fundorte und offene Missionen.
2. **Hingehen:** Am Baum wird die Mission über Standortnähe freigeschaltet. Bei ungenauem GPS kann ein alternativer, plausibler Nachweis angeboten werden.
3. **Interagieren:** Eine kleine spielerische Aufgabe entspricht einer klaren Datenaktion: „Steht dieser Baum noch hier?“, „Welche Blattform passt?“ oder „Entdecke einen fehlenden Baum“.
4. **Sammeln:** Der Fund landet im digitalen Baumbuch. XP werden für einen eingereichten Beitrag zunächst vorgemerkt und nach Qualitätsprüfung bestätigt.
5. **Weiterspielen:** Die App schlägt eine nahe Mission, eine neue Art oder ein Viertelziel vor.

### 5.2 Missionsarten

| Mission | Spielerisches Erlebnis | Datengewinn | Priorität |
|---|---|---|---|
| **Baum-Check** | Bekannten Baum vor Ort wiederfinden | Existenz und Beobachtungsdatum bestätigen | MVP |
| **Arten-Detektiv** | Zwischen wenigen plausiblen Arten wählen; „weiß nicht“ ist erlaubt | Baumart bestätigen oder Korrektur vorschlagen | MVP, wenn Referenzdaten ausreichen |
| **Neuer Fund** | Unbekannten Baum auf der Karte entdecken | Fehlenden Baum als neuen Kandidaten anlegen | MVP, vereinfacht |
| **Standort-Fuchs** | Kartenposition am realen Baum prüfen | Ungenaue Koordinaten zur Prüfung markieren | Später |
| **Jahreszeiten-Safari** | Denselben Baum zu verschiedenen Jahreszeiten besuchen | Zeitlich verteilte Beobachtungen | Später |
| **Viertel-Quest** | Gemeinsam eine lokale Karte vervollständigen | Gezielt Lücken im Datenbestand schließen | Später |

### 5.3 Belohnungen

- Ein **persönliches Baumbuch** mit entdeckten Arten und Fundorten.
- **XP und Abzeichen** für geprüfte Beiträge, unterschiedliche Baumarten und abgeschlossene Lernmissionen.
- **Viertel-Fortschritt** statt ausschließlich individueller Ranglisten.
- **Qualität vor Menge:** Keine hohen Belohnungen für massenhafte ungeprüfte Meldungen; wiederholte Falschmeldungen bringen keinen Vorteil.
- Optional später: zeitlich begrenzte Community-Quests oder Schulaktionen, ohne riskante Anreize wie nächtliche Erkundung oder Betreten privater Flächen.

## 6. Datenmodell und Datenqualität

### 6.1 Drei unterschiedliche Dinge

- **Referenzbaum:** Ein bestehender Eintrag aus dem städtischen Datensatz, möglichst mit stabiler ID.
- **Beobachtung:** Eine Bürger:innen-Meldung zu einem bestimmten Zeitpunkt; sie verändert die Referenzdaten nicht unmittelbar.
- **Änderungsvorschlag:** Ein aus einer oder mehreren Beobachtungen abgeleiteter Vorschlag, der geprüft und exportiert werden kann.

### 6.2 Minimales Beobachtungsschema

| Feld | Beschreibung |
|---|---|
| `observation_id` | Eindeutige Meldungs-ID |
| `tree_id` | Bestehende Referenz-ID oder neue vorläufige Baum-ID |
| `location` | GPS-Position mit ausgewiesener Genauigkeit |
| `observed_at` | Beobachtungszeitpunkt |
| `action` | `exists`, `missing`, `species_suggestion` oder `new_tree` |
| `species` | Optionale Baumart; „unbekannt“ ist zulässig |
| `photo` | Optionales Belegfoto, falls für Prüfung nötig |
| `source` | Import, Nutzerbeobachtung oder städtische Prüfung |
| `review_status` | `pending`, `needs_review`, `verified`, `rejected` |
| `dataset_version` | Version des zugrunde liegenden Referenzdatensatzes |

**MVP-Prüfregeln:** GPS-Genauigkeit berücksichtigen; nahe Dubletten und überlappende Bäume erkennen; bei strittigen Angaben unabhängige Bestätigungen oder manuelle Prüfung verlangen. „Baum fehlt“ zunächst nur als Prüfhinweis werten, nicht als automatisches Löschen. Fachliche Aussagen zur Verkehrssicherheit oder Baumgesundheit sind **nicht** Bestandteil des MVP.

## 7. Hackathon-MVP: bewusste Begrenzung

**Eine Kartenansicht, drei Aktionen, ein Prüf-Dashboard.** Der Pilot startet in **einem** gut zugänglichen Münsteraner Gebiet, dessen Umfang sich aus dem verfügbaren Datensatz und den Testmöglichkeiten ergibt.

### Muss im MVP funktionieren

- Karte mit importierten Referenzbäumen, Standort und sichtbaren offenen Missionen.
- Einen vorhandenen Baum auswählen und vor Ort oder im Demo-Modus seine Existenz bestätigen.
- Optional eine Baumart bestätigen oder „weiß nicht“ auswählen.
- Einen noch nicht erfassten Baum als Kandidaten melden.
- Meldung speichern und als **ungeprüft** kennzeichnen; für die Demo optional einen einfachen Prüf-/Freigabeschritt anbieten.
- Persönliches Baumbuch und einfache XP-Rückmeldung.
- Minimaler Admin-Bereich mit Beobachtungsliste, Kartenbezug und CSV-/GeoJSON-Export.
- Eine nachvollziehbare Vorher-Nachher-Demo des Datenstands.

### Bewusst **nicht** im MVP

Automatische Baumartenerkennung per KI, flächendeckende Münster-Abdeckung, komplexe soziale Netzwerke, umfassende Offline-Synchronisierung, echte amtliche Freigabeprozesse und eine automatische Änderung des offiziellen Stadt-Datensatzes.

## 8. Vorschlag für die technische Architektur

- **Frontend:** Mobile-first Web-App / PWA, damit die Demo ohne App-Store funktioniert.
- **Karte:** MapLibre oder vergleichbare Webkarte; Kartenanbieter, Nutzungsbedingungen und erforderliche Quellenangaben prüfen.
- **Geolocation:** Browser-Standort mit transparenter Einwilligung und sichtbarer Genauigkeit.
- **Backend:** Ein leichtgewichtiges API mit Datenbank und räumlicher Suche (z. B. PostgreSQL/PostGIS oder für den Hackathon ein gehosteter Dienst).
- **Import:** Städtische Baumdaten in ein internes Referenzschema überführen, inklusive ursprünglicher ID, Quelle, Version und Lizenz.
- **Export:** Änderungs-/Beobachtungsvorschläge getrennt vom ursprünglichen Datensatz als CSV und GeoJSON bereitstellen.
- **Dashboard:** Einfache Liste und Karte für Dubletten, Konflikte, Prüfstatus und Freigabe.

Die konkrete Technologieentscheidung folgt den vorhandenen Teamfähigkeiten und der überprüften Datenquelle. Für die Hackathon-Demo kann ein kleiner Testdatensatz genutzt werden, falls die amtliche Quelle technische Probleme bereitet; er muss klar als Testdatensatz gekennzeichnet sein.

## 9. Datenschutz, Fairness und Sicherheit

- Die Datenerhebung und beabsichtigte Weitergabe an die Stadt im Onboarding klar erklären.
- Standort nur für die jeweilige Aktion verwenden; keine dauerhafte Hintergrund-Ortung im MVP.
- Öffentlich sichtbare Daten nicht mit persönlichen Bewegungsprofilen verbinden; für die Demo ggf. pseudonyme Spielkonten oder lokale Spielstände.
- Fotos nur optional hochladen; erkennbare Menschen, Kennzeichen und sensible Bildmetadaten vermeiden bzw. entfernen.
- Bestehende Open-Data-Lizenzen und mögliche Rechte an Nutzerfotos vor Nutzung und Export prüfen.
- Barrierearme Alternativen zu zeitkritischen Aufgaben und Standortzwang vorsehen; Missionsorte dürfen keine gefährlichen Wege oder Privatgrundstücke erfordern.
- Keine tatsächliche Pflanzung oder amtliche Datenänderung suggerieren, wenn lediglich eine virtuelle Spielaktion bzw. ein Änderungsvorschlag erfolgt.

## 10. Erfolgsmessung

**Leitmetrik:** Zahl der nach Prüfung verwertbaren, neuen oder aktualisierten Baumdatensätze – nicht die Zahl der App-Klicks.

Begleitmetriken: Anteil prüfbarer Beobachtungen; bestätigte versus abgelehnte Meldungen; entdeckte Dubletten; Abdeckung des Pilotgebiets; Medianzeit pro abgeschlossener Mission; freiwillige Wiederkehr; Aufwand für manuelle Prüfung.

**Demo-Ziel (noch keine Prognose):** Das Team kann mit Testpersonen mehrere echte oder simulierte Missionen durchführen und mindestens einen nachvollziehbaren Änderungsvorschlag bis zum Export vorführen.

## 11. Grober Ablauf für MS Hack

| Phase | Ergebnis |
|---|---|
| **Vorbereitung** | Datensatz prüfen, Pilotgebiet festlegen, einseitige User Journey und Datenmodell abstimmen |
| **Build 1** | Datenimport, Kartenansicht, Baumdetail und Standortnähe |
| **Build 2** | Missionen, Meldungsformular, Speichern und Baumbuch |
| **Build 3** | Einfacher Prüfbereich, Export, Basis-Datenqualitätsregeln |
| **Finale** | Vor-Ort-Test, Fehlerkorrektur, 3-Minuten-Pitch mit Vorher-Nachher-Demo |

## 12. Priorisierte TODOs – unser Arbeits-Backlog

Jede Aufgabe bekommt später eine eigene Detailbeschreibung mit **Recherche/Quellen, Entscheidung, Verantwortlichen, Akzeptanzkriterien und Status**. Die Nummern bleiben stabil, damit wir einzelne Punkte im Chat gezielt vertiefen können.

### P0 – vor und während des Hackathons

- [ ] **T01 · Datenlage Münster prüfen:** Offizielle Quelle, API/Download, Aktualität, Spalten, Geometrie, ID, Lizenz und bekannte Qualitätsprobleme dokumentieren.
- [ ] **T02 · Pilotgebiet definieren:** Ein kleines, zugängliches Gebiet mit sinnvoller Baumdichte und Testmöglichkeiten auswählen.
- [ ] **T03 · Nutzerreise skizzieren:** Erster App-Start → Karte → erste Mission → Baumbuch; als 5–7 Screens wireframen.
- [ ] **T04 · Missionen präzisieren:** Baum-Check, Arten-Detektiv und neuer Fund mit genauen Fragen, „weiß nicht“-Optionen und XP-Regeln beschreiben.
- [ ] **T05 · Datenmodell & Qualität festlegen:** Import-Mapping, Beobachtungsschema, Distanzlogik, Dubletten-Check und Prüfstatus definieren.
- [ ] **T06 · Stack & Repository aufsetzen:** Frontend, Backend, Kartenprovider, Datenbank, Deployment und Teamaufgaben festlegen.
- [ ] **T07 · Karten-Prototyp bauen:** Referenzbäume importieren, anzeigen, filtern und ein Baumdetail öffnen.
- [ ] **T08 · Mission-End-to-End bauen:** Standortprüfung, Aktion ausführen, Beobachtung speichern, Rückmeldung und Baumbuch.
- [ ] **T09 · Mini-Dashboard & Export bauen:** Beobachtungen prüfen und als CSV/GeoJSON mit Herkunftsnachweis exportieren.
- [ ] **T10 · Datenschutz-/Lizenz-Check:** Stadt-Datenlizenz, Kartenlizenz, Fotohandhabung, Standort-Einwilligung und Demo-Hinweise klären.
- [ ] **T11 · Pilot testen & Pitch vorbereiten:** Vor-Ort- oder Demo-Test, messbarer Vorher-Nachher-Fall und kurze Live-Demo.

### P1 – nach dem ersten funktionierenden Prototyp

- [ ] **T12 · Stadt Münster einbeziehen:** Geeignete Ansprechpersonen, gewünschtes Übergabeformat und realen Prüfprozess ermitteln.
- [ ] **T13 · Gamification vertiefen:** Abzeichen, Arten-Sammlungen, Viertelziele und motivierende, nicht manipulative Fortschrittsanzeige testen.
- [ ] **T14 · Qualitätsmechanismen testen:** Unabhängige Bestätigungen, Vertrauensregeln, Konflikte und Missbrauchsschutz empirisch bewerten.
- [ ] **T15 · UX und Barrierefreiheit verbessern:** Tests mit Gelegenheitsnutzer:innen, Familien und Personen mit unterschiedlichen Zugänglichkeitsbedürfnissen.
- [ ] **T16 · Pilot-KPIs erfassen:** Tatsächlichen Datengewinn, Korrektheit und Prüfaufwand gegen die ursprüngliche Datenbasis messen.

### P2 – mögliche Weiterentwicklung

- [ ] **T17 · Saisonale und thematische Quests:** Wiederholungsbeobachtungen und lokale Community-Aktionen.
- [ ] **T18 · Offlinefähigkeit:** Missionen ohne Mobilfunk absolvieren und später kontrolliert synchronisieren.
- [ ] **T19 · Assistierte Artbestimmung:** Bildbasierte Vorschläge mit Unsicherheitsanzeige und expliziter menschlicher Bestätigung evaluieren.
- [ ] **T20 · Weitere Open-Data-Themen:** Spielkonzept erst nach belastbarem Baumpilot auf andere städtische Datensätze übertragen.

## 13. Offene Produktentscheidungen

- Gibt es in Münster einen geeigneten offiziellen **Einzelbaum-Datensatz** mit stabilen IDs und nutzbarer Lizenz?
- Sollen zunächst ausschließlich bestehende Stadtbäume bestätigt werden oder direkt neue Funde erlaubt sein?
- Wie funktioniert eine gerechte Belohnung, wenn ein Beitrag erst später geprüft wird?
- Wird der Stadt ein frei exportierbarer Änderungsvorschlag übergeben oder ist perspektivisch eine direkte Datenschnittstelle möglich?
- Welche Qualität ist für eine **nützliche Meldung** ausreichend, ohne Nutzer:innen mit Fachwissen zu überfordern?

---

**Nächster Arbeitsschritt:** **T01** mit echten Münsteraner Quellen und einem beispielhaften Baumdatensatz ausarbeiten; danach **T02–T04** so konkretisieren, dass das Team direkt Screens und technische Tickets ableiten kann.
