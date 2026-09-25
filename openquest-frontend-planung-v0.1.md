# OpenQuest – Frontend-Planung

> **Status:** Arbeitsentwurf v0.1 · **Datum:** 25.09.2026  
> **Basis:** `ms-hack-stadtbaum-quest-konzept-1.md` · **Fokus:** spielende Bürger:innen; Admin-Dashboard nur als kleine separate Oberfläche  
> **Ziel:** Ein überzeugender, mobil nutzbarer Hackathon-MVP, der echte Baumbeobachtungen als spielerische Missionen erfasst.  
> **Nicht entschieden:** endgültiger App-Name, CI, Pilotgebiet, Karten- und Datenprovider, finales Backend.

## 1. Produkt- und UX-Leitlinien

1. **Karte zuerst:** Eine zentrale, interaktive Karte mit den nächsten erreichbaren Missionen. Keine Formular-Startseite.
2. **Eine Aufgabe pro Schritt:** Nutzer:innen beantworten kurze, verständliche Fragen. Die Datenstruktur bleibt im Hintergrund.
3. **Transparent statt irreführend:** Im Onboarding erklären, dass Beobachtungen zur Verbesserung öffentlicher Baumdaten beitragen. XP für Einreichungen als *vorgemerkt* zeigen; erst nach Prüfung als bestätigt.
4. **Zwei Einstiegswege:** Standort freigeben und direkt in der Nähe starten **oder** Karte manuell erkunden; Demo-Modus für Präsentation und fehlende Berechtigungen.
5. **Draußen bedienbar:** Hoher Kontrast bei Tageslicht, große Touch-Ziele, wenige Texteingaben, klare Rückmeldung bei schlechtem GPS.
6. **Sicher und fair:** Keine Anreize, Privatgrundstücke, gefährliche Straßen oder unzugängliche Flächen zu betreten. Beobachtungen nicht mit amtlicher Freigabe verwechseln.

## 2. Informationsarchitektur

### Primäre Bottom-Navigation (MVP)

| Tab | Funktion | MVP |
|---|---|---|
| **Karte** | Fundorte und offene Missionen entdecken | Ja |
| **Baumbuch** | Entdeckte Arten, Fundorte und Sammelfortschritt | Ja |
| **Profil** | Pseudonym, Level, XP und eigene Beiträge | Reduziert |

**Kontextuelle Screens:** Onboarding, Baumdetail-Bottom-Sheet, Missionsdialog, Erfolgsscreen, „Neuer Baum“-Flow. Das **Admin-Dashboard** ist eine separate Route (`/admin`) und kein Tab in der Spieler-App.

## 3. Screen-Planung und Wireframes

### S01 – Onboarding / erster Start

**Ziel:** In weniger als einer Minute verständlich machen, worum es geht und die erste Entdeckung ermöglichen.

- Logo, kurze Botschaft: „Entdecke Münsters Bäume. Hilf mit, die Karte aktuell zu halten.“
- Zwei knappe Illustrationen/Karten: „Draußen entdecken“ und „Im Baumbuch sammeln“.
- Standortfreigabe erst durch bewussten Klick auf **„Bäume in meiner Nähe finden“** auslösen, nicht beim ersten Rendern.
- Alternative **„Ohne Standort auf Karte stöbern“**.
- Für die Hackathon-Demo optional **„Demo-Modus starten“**; stets sichtbar als Testmodus kennzeichnen.
- Hinweis auf Datenbeiträge, optionales Foto und keine dauerhafte Hintergrundortung.

**Zustände:** Neu / Berechtigung zugelassen / abgelehnt / GPS nicht verfügbar / Demo.

### S02 – Entdeckerkarte (Hauptscreen)

```text
┌─────────────────────────────────┐
│ OpenQuest              Lv 03    │
│ Entdecke Münster       140 XP   │
├─────────────────────────────────┤
│                                 │
│    🌳             🌳             │
│           📍                    │
│                 🌳              │
│     🌳                    🌳     │
│                          ◎      │
│                                 │
│ ┌─────────────────────────────┐ │
│ │ 3 Missionen in deiner Nähe  │ │
│ │ [Nächsten Baum entdecken →] │ │
│ └─────────────────────────────┘ │
├─────────────────────────────────┤
│    Karte     Baumbuch   Profil  │
└─────────────────────────────────┘
```

**Elemente:** Karte, Zoom und Recenter, eigener Standort mit Genauigkeitsanzeige, Baummarker, einfacher Missionsfilter („Alle“ / „Offen“), ggf. Cluster bei dichter Baumlage, unaufdringliches HUD mit XP, Bottom-Navigation. Ein **„+ Baum entdeckt“**-Button startet die Meldung eines neuen Baums.

**Markerzustände:** Noch nicht besucht; Mission verfügbar; bereits eingereicht (Prüfung offen); bestätigter Fund; bereits geprüfter Referenzbaum ohne offene Mission. Visuelle Unterscheidung nicht allein über Farbe.

**Interaktion:** Tippen auf Marker öffnet S03, ohne die Karte zu verlassen. Der nächste sinnvolle Baum kann als Karte/Bottom-Sheet vorgeschlagen werden, ohne automatisches GPS-Tracking.

**Leere/Fehlerzustände:** Keine Bäume im sichtbaren Ausschnitt; keine offenen Missionen; Standort nicht verfügbar; Kartenkacheln nicht geladen; langsame Datenverbindung. Immer manuelle Kartennavigation ermöglichen.

### S03 – Baumdetail als Bottom Sheet

- Kurzer Baumname, z. B. „Linde“ oder „Unbekannte Art“, mit optionaler Illustration.
- „Schon bekannt“ / „Noch zu bestätigen“ / „Deine Meldung wird geprüft“.
- Bekannte Art nur als **Angabe im Ausgangsdatensatz** beschriften, wenn noch nicht überprüft.
- Mission-CTA: **„Baum überprüfen“**. Optional zweiter CTA: **„Art bestimmen“**.
- Abstand als grobe Orientierung und GPS-Genauigkeit anzeigen; keine Scheingenauigkeit.
- Optional: Standort näher ansehen, aber keine Gamification für gefährliche Wege.
- Wenn eine Meldung bereits eingereicht wurde, Einreichungsstatus anzeigen statt dieselbe Mission unbegrenzt erneut zu belohnen.

### S04 – Mission: Baum-Check und Arten-Detektiv

**Flow A – Baum-Check (Pflicht-MVP)**

1. Vor-Ort-Hinweis und Standortprüfung; GPS nur nach Einwilligung.
2. Große Frage: **„Ist dieser Baum noch hier?“** mit **„Ja“**, **„Nicht sicher“**, **„Nein“**.
3. Bei „Ja“ Beobachtung `exists`; bei „Nein“ Beobachtung `missing` **als Prüfhinweis**, niemals automatische Löschung. „Nicht sicher“ erzeugt keine definitive Existenzbehauptung.
4. Optionales Foto als Beleg; beim Fotografieren auf erkennbare Menschen, Kennzeichen und Metadaten hinweisen.
5. Bestätigungsansicht mit zusammengefasster Beobachtung; **„Beitrag einreichen“**. Nach Erfolg S05.

**Flow B – Arten-Detektiv (MVP nur bei ausreichenden Referenzdaten)**

1. „Welche Blattform passt?“ oder wenige plausible Arten mit Bild/Text anbieten.
2. **„Weiß nicht“** ist immer möglich. Keine erzwungene oder vorgetäuschte Bestimmung.
3. Artvorschlag als `species_suggestion` speichern, bei Widerspruch prüfpflichtig.
4. Kurzer Lernmoment statt langer Artenenzyklopädie.

**Standort-Fallback:** Ungenaue GPS-Position nicht als harte Fehlermeldung interpretieren. Bei unzureichender Genauigkeit Hinweis anzeigen, erneuten Versuch oder manuell eingeordneten Beitrag mit entsprechendem Prüfstatus zulassen. Konkrete Metergrenze ist eine offene Produktentscheidung.

**Formzustände:** Idle, Standort wird ermittelt, Standort ungenau, Bildverarbeitung, Senden, erfolgreich, fehlgeschlagen. Doppelte Einreichungen durch deaktivierten Submit und serverseitige Idempotenz vermeiden.

### S05 – Missionsabschluss und Belohnung

- Kleine Erfolgsmicrointeraction, nicht von einer schon erfolgten amtlichen Änderung sprechen.
- Text: **„Entdeckung eingereicht!“**
- Status: **„Wird geprüft“** und **„+20 XP vorgemerkt“** (20 ist nur ein UI-Testwert, finale Spielregeln noch offen).
- Falls Art neu entdeckt: neue Karte fürs Baumbuch. Bei nicht bestätigter Art als **„Vorgemerkt“** markieren.
- CTA: **„Nächsten Baum entdecken“** oder **„Zum Baumbuch“**.
- Nach tatsächlicher Freigabe wird der Status in **„Bestätigt“** geändert und XP bestätigt.

### S06 – Neuer Baum / fehlenden Baum melden

1. Auf Karte **„+ Baum entdeckt“** auswählen.
2. Position über aktuellen Standort oder manuell gesetzten Karten-Pin bestimmen; GPS-Genauigkeit festhalten.
3. Einfache Frage: „Steht hier ein Baum, der noch nicht auf der Karte ist?“
4. Optional Baumart und Foto; „Unbekannt“ zulassen.
5. Nahe mögliche Dubletten anzeigen: „Meinst du vielleicht diesen Baum?“
6. Neue Meldung als **Kandidat** `new_tree` mit `pending`-Status übermitteln; niemals sofort als offizieller Baum auf der Karte ausgeben.

### S07 – Digitales Baumbuch

- Sammelraster mit Artenkarten, Anzahl entdeckter Arten und bestätigten Fundorten.
- Filter: „Alle“ / „Entdeckt“ / „Noch offen“.
- Kartenstatus: **bestätigt**, **in Prüfung**, **noch nicht entdeckt**.
- Antippen einer Karte zeigt kurze Lerninfo und die eigenen Beobachtungen, ohne öffentliche Bewegungsprofile zu erzeugen.
- Für den MVP reichen lokal/Pseudonym zugeordnete persönliche Funde; ein ausgereiftes Sozialprofil ist nicht nötig.

### S08 – Minimalprofil

- Pseudonym/Guest-Name; Level; vorgemerkte versus bestätigte XP; Anzahl persönlicher Entdeckungen.
- Liste eigener Beiträge mit Datum und Status.
- Einstellungen: Standort-Hinweis, Datenschutzhinweis, Demo-Modus, Feedback.
- Kein öffentliches Ranking im MVP.

### S09 – Admin-Prüfbereich (separat)

- Einfacher Zugriff nur für Demo-/Projektverantwortliche; nicht öffentlich zugänglich.
- Beobachtungsliste mit `pending`, `needs_review`, `verified`, `rejected`.
- Auswahl zeigt Referenzbaum, vorgeschlagene Änderung, Datum, GPS-Genauigkeit und optionales Foto auf der Karte.
- Freigabe/Ablehnung nur als **Projekt-Prüfschritt**, nicht als automatische Änderung offizieller Stadt-Daten.
- CSV-/GeoJSON-Export der nachvollziehbaren Beobachtungen/Änderungsvorschläge.

## 4. Durchgängige MVP-User-Journey

```text
App öffnen
  ↓
Kurzes Onboarding + Zweck transparent machen
  ↓
Standort erlauben ODER Karte manuell erkunden
  ↓
Baummarker antippen → Baumdetail
  ↓
„Baum überprüfen“ → kurze Vor-Ort-Mission
  ↓
„Ja, steht hier“ → optionales Foto → Einreichen
  ↓
„Eingereicht / wird geprüft“ + vorgemerkte XP
  ↓
Baumbuch aktualisiert → nächster Baum vorgeschlagen
  ↓
Separates Admin-Dashboard: Beobachtung prüfen → exportieren
```

**Live-Demo:** Zusätzlich einen klar gekennzeichneten Demo-Modus mit kleiner Testbaumgruppe bereitstellen. Er darf kein realer Vor-Ort-Nachweis und kein amtlich bestätigter Beitrag sein.

## 5. Visuelle Richtung (Vorschlag, noch kein beschlossenes CI)

- **Look & Feel:** Freundliches urbanes Entdeckungsspiel; natürliche, kontrastreiche Farben und klare Illustrationen. Keine direkte Kopie bekannter Spiele.
- **Farbpalette:** Waldgrün `#225A42` (Primär), helles Blattgrün `#D7EEA5` (Akzent), Creme `#F7F5EC` (Hintergrund), dunkles Anthrazit `#20302A` (Text), warmes Gold `#F4C75A` (XP). Kontrastwerte vor Festlegung prüfen.
- **Typografie:** Gut lesbare, moderne Sans-Serif; starke Hierarchie; keine verspielte Display-Schrift für wichtige UI-Texte.
- **Formensprache:** Abgerundete Karten und Bottom Sheets, organische Details sparsam; Illustration und kleine Animationen als Belohnung.
- **Kartenlesbarkeit:** Wenige Markerarten, nachvollziehbare Legende, Marker-Cluster bei Überlappung, hoher Kontrast; Status zusätzlich mit Form/Icon/Text.
- **Mobile zuerst:** Einhand-Bedienung, Bottom-Navigation und gut erreichbare primäre Aktion, Safe-Area-Inset für Smartphones.
- **Barrierearm:** Mindestens 44 × 44 CSS-Pixel große Interaktionsziele als Designziel; sichtbarer Fokus, Reduced Motion, kontrastreiche Texte, zugängliche Formlabels, Alternative zur reinen Standortbedienung.

## 6. Frontend-Stack (Empfehlung, Entscheidung offen)

| Bereich | Vorschlag | Warum |
|---|---|---|
| Grundgerüst | **React + TypeScript**, bei Bedarf **Next.js** | Wiederverwendbare Screens und klar typisierte API-Daten |
| Styling | **Tailwind CSS + CSS-Variablen** | Schnelles Mobile-first-UI, konsistente Tokens |
| Kartenansicht | **MapLibre GL JS** | Interaktive Bäume, Marker und räumliche Exploration |
| Datenabruf | Einfache Fetch-Schicht, bei Bedarf TanStack Query | Lade-, Fehler- und Aktualisierungszustände kontrollieren |
| App-State | React Context / kleine Store-Lösung | Mission, Session und UI-Zustände; nicht zu früh komplex werden |
| Geolocation | Browser Geolocation API | Bewusste Standortfreigabe und Genauigkeitsanzeige |
| Optional PWA | Webmanifest + begrenztes Caching | Auf dem Smartphone installierbar; keine vollständige Offline-Synchronisierung im MVP |
| Icons | Einheitliche SVG-Iconbibliothek | Lesbare Marker und konsistente Bedienelemente |

**Kartenhinweis:** Kartenanbieter, Tile-Limits, erforderliche Attribution und Nutzungslizenz vor Implementation prüfen. MapLibre ist die Rendering-Bibliothek, nicht selbst der Kartendatenanbieter.

## 7. Beispielhafte Projektstruktur

```text
src/
  app/
    page.tsx                 # Einstieg / Karte
    collection/page.tsx      # Baumbuch
    profile/page.tsx         # Profil
    admin/page.tsx           # Demo-Prüfoberfläche
  components/
    layout/BottomNavigation.tsx
    layout/AppHeader.tsx
    ui/Button.tsx
    ui/BottomSheet.tsx
    ui/StatusBadge.tsx
  features/
    map/TreeMap.tsx
    map/TreeMarker.tsx
    map/MapControls.tsx
    trees/TreeDetailSheet.tsx
    missions/TreeCheckFlow.tsx
    missions/SpeciesDetective.tsx
    missions/NewTreeFlow.tsx
    missions/MissionSuccess.tsx
    collection/TreeCollection.tsx
  lib/
    api.ts                   # Backend-Abstraktion
    geolocation.ts
    demo-data.ts
    models.ts
  styles/
    tokens.css
```

**Wichtig:** Map-Komponenten bei SSR erst clientseitig initialisieren; Browser-APIs wie `navigator.geolocation` nicht während des Server-Renderings aufrufen.

## 8. UI-Datenverträge zwischen Frontend und Backend

Die Feldbezeichnungen orientieren sich am übergeordneten Konzept. Dies sind **vorgeschlagene Schnittstellen**, noch kein implementierter Serververtrag.

```ts
type ReviewStatus = 'pending' | 'needs_review' | 'verified' | 'rejected';
type ObservationAction = 'exists' | 'missing' | 'species_suggestion' | 'new_tree';

type Tree = {
  id: string;                  // Interne ID mit Referenz zur städtischen Original-ID
  location: { lat: number; lng: number };
  species?: string | null;
  datasetVersion: string;
};

type ObservationInput = {
  treeId?: string;             // Fehlt bei neuem Baum-Kandidaten
  action: ObservationAction;
  observedAt: string;
  location: { lat: number; lng: number; accuracyM?: number };
  species?: string | null;
  photoId?: string;            // Wenn Foto-Upload umgesetzt wurde
  datasetVersion: string;
  clientRequestId: string;     // Doppelte Einreichungen vermeiden
  demo: boolean;
};

type ObservationResult = {
  observationId: string;
  reviewStatus: ReviewStatus;
  pendingXp: number;
};
```

**Vorgeschlagene API-Oberfläche:** `GET /trees?bbox=...`, `GET /missions?bbox=...`, `POST /observations`, `GET /me/collection`, `GET /me/observations`; für Admin zusätzlich `GET /admin/observations`, `PATCH /admin/observations/:id` und Export. Auth, Uploads, tatsächliche Routen und die Trennung von Referenz- und Beobachtungsdaten werden mit dem Backend-Team abgestimmt.

**Frontend-Regel:** Aus einer eingereichten Beobachtung nie automatisch einen amtlich bestätigten Baum ableiten. Spielstatus und Prüfstatus getrennt halten.

## 9. Frontend-Backlog mit Akzeptanzkriterien

### P0 – Hackathon: durchgängiger Spielablauf

- [ ] **FE-01 · UI-Kit + Mobile-Shell:** Design-Tokens, Header, drei Tabs, Buttons, Status-Badges und Bottom Sheet; bedienbar auf kleinen Smartphone-Displays.
- [ ] **FE-02 · Onboarding + Standort:** Zweck erklärt; Browser fragt Standort erst nach Nutzeraktion; manuelle Kartennavigation funktioniert bei Ablehnung.
- [ ] **FE-03 · Karten-Prototyp:** Baum-Mockdaten auf einer Münster-Karte; Marker sind antippbar; Baumdetail öffnet sich; Fehlermeldung ohne Daten ist verständlich.
- [ ] **FE-04 · Baumdetail + Markerstatus:** Art (oder unbekannt), Referenz-/Missionsstatus, klare Hauptaktion und Einreichungsstatus sichtbar.
- [ ] **FE-05 · Baum-Check-Mission:** Standort-Hinweis, Ja/Nein/Unsicher, Review-Schritt, asynchroner Submit; keine doppelte Einreichung durch Doppeltipp.
- [ ] **FE-06 · Erfolgsrückmeldung:** Eindeutig „eingereicht / in Prüfung“, vorgemerkte XP, Übergang ins Baumbuch oder zum nächsten Fund.
- [ ] **FE-07 · Einfaches Baumbuch:** Entdeckte Einträge sichtbar; `pending`/`verified` unterscheidbar; nach Mission aktualisiert.
- [ ] **FE-08 · Neuen Baum melden:** Standort/Pin, Dublettenhinweis und Einreichung als neuer Kandidat; keine automatische Aufnahme in offizielle Referenzdaten.
- [ ] **FE-09 · Backend-Integration:** Tatsächliche Beobachtung speichern; Lade-/Fehlerzustände; App kann neu geöffnet werden, ohne Einreichungen zu verlieren.
- [ ] **FE-10 · Präsentationssicherer Demo-Modus:** Sichtbar gekennzeichnete Testdaten und simulierte Standortfreigabe; End-to-End-Demo auch ohne echte GPS-Position vorführbar.

### P1 – Bei ausreichender Zeit

- [ ] **FE-11 · Arten-Detektiv:** Kleine Arten-Auswahl, Bilder oder Blattmerkmale, „Weiß nicht“, prüfpflichtige Korrekturvorschläge.
- [ ] **FE-12 · Profil + XP-Details:** Level, Beiträge und vorgemerkte/bestätigte Belohnungen; keine künstlich bestätigten XP.
- [ ] **FE-13 · Kartenqualität:** Marker-Cluster, Filter für offene Missionen, Hinweise auf GPS-Genauigkeit, Karten-Fehlerzustände.
- [ ] **FE-14 · Accessibility + Feldtest:** Mobilgerät bei Tageslicht, Screenreader-Basics, Tastatur, Touch-Ziele, Reduced Motion und echte Testpersonen.
- [ ] **FE-15 · Mini-Admin-UI:** Liste mit Standortbezug und Prüfstatus, exemplarische Freigabe, CSV-/GeoJSON-Download zusammen mit dem Backend.

### P2 – Nach dem Hackathon

- [ ] **FE-16 · Jahreszeiten- und Viertelquests:** Langfristige Progression auf Basis tatsächlicher Qualitätsziele.
- [ ] **FE-17 · Offline-Funktion:** Gespeicherte Missionen, sichere Warteschlange und kontrollierte Konfliktbehandlung.
- [ ] **FE-18 · Assistierte Artbestimmung:** Nur mit Unsicherheitsanzeige und bewusster menschlicher Bestätigung.

## 10. Empfohlene Reihenfolge und Schnittstellen

| Etappe | Frontend-Lieferobjekt | Abhängigkeit |
|---|---|---|
| 1 | Klickbare Mobile-Shell + Mockkarte | Kein Backend erforderlich |
| 2 | Baumdetail und Baum-Check mit Mockdaten | Festgelegtes Datenmodell |
| 3 | Einreichung und Erfolg mit Test-API | `POST /observations`, Prüfstatus |
| 4 | Baumbuch und neuer Baum-Kandidat | Abfrage eigener Beiträge, Dublettenhinweis |
| 5 | Mini-Admin, Export und Live-Demo | Prüf- und Export-API |

**Hackathon-Schnitt:** Wenn Zeit knapp wird, funktionieren Karte → Baumdetail → Baum-Check → Einreichen → Rückmeldung → Prüf-Dashboard vor Arten-Detektiv, komplexem Profil oder aufwendigen Animationen.

## 11. Offene Frontend-Entscheidungen

- [ ] Welcher Kartenstil unterstützt Lesbarkeit und spielerische Anmutung bei Tageslicht?
- [ ] Welche Markerstatus unterscheiden wir visuell, ohne die Karte zu überladen?
- [ ] Wie groß ist das Pilotgebiet und welche Anzahl an Bäumen muss die Karte flüssig darstellen?
- [ ] Gibt es verbindliche Arteninformationen und Bildrechte für den Arten-Detektiv?
- [ ] Soll der erste Test vollständig ohne Login nutzbar sein oder mit einem pseudonymen Spielkonto?
- [ ] Welche Standortgenauigkeit ist für eine Mission ausreichend und wann gilt eine Meldung als manuell prüfpflichtig?
- [ ] Welcher XP-Wert wird vorgemerkt und wann genau bestätigt?
- [ ] Welche Teile des Admin-Dashboards muss das Frontend-Team selbst umsetzen?

## 12. Definition of Done für den Frontend-MVP

- [ ] Auf einem realen Smartphone in einem aktuellen Browser vorführbar.
- [ ] Start bis erste abgeschlossene Mission in wenigen, verständlichen Schritten möglich.
- [ ] Karte funktioniert ohne Standortfreigabe im manuellen Modus.
- [ ] Die App zeigt echte Rückmeldungen bei Ladefehlern, ungültigen Daten und schlechtem GPS.
- [ ] Ein eingereichter Beitrag erscheint korrekt als *ungeprüft*; vorgemerkte XP sind nicht als bestätigt dargestellt.
- [ ] Eine neue Baum-Meldung kann als Kandidat angelegt werden.
- [ ] Das Baumbuch aktualisiert sich nach erfolgreicher Einreichung.
- [ ] Demo-Daten und echte Beiträge sind deutlich unterscheidbar.
- [ ] Die End-to-End-Demo ist ohne App-Store und ohne simulierte amtliche Bestätigung möglich.

**Bezug zum ursprünglichen Backlog:** T03 (User Journey), T04 (Missionen), T06 (Stack), T07 (Karte), T08 (Mission), T09 (Admin), T10 (Datenschutz), T15 (UX).
