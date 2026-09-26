# OpenQuest – Frontend Prototype v0.1

## Projekt

Wir entwickeln für den Hackathon **MS Hack Münster** eine mobile Web-App namens **OpenQuest**.

OpenQuest soll Bürger:innen spielerisch dazu bringen, städtische Open-Data-Datensätze zu überprüfen und anzureichern.

Der erste Anwendungsfall sind **Stadtbäume in Münster**.

Die Nutzer:innen sollen dabei möglichst wenig das Gefühl haben, Datenpflege zu betreiben. Das Erlebnis soll eher wie ein kleines Location-Based-Game funktionieren:

> Karte öffnen → Baum entdecken → hingehen → Mission lösen → XP erhalten → Baum sammeln.

In dieser ersten Version bauen wir ausschließlich einen **Frontend-Prototyp mit Mockdaten**.

---

## 1. Ziel des Prototyps

Der Prototyp soll einen vollständigen Userflow demonstrieren:

1. Nutzer öffnet OpenQuest.
2. Eine Karte von Münster wird angezeigt.
3. Auf der Karte befinden sich verschiedene Bäume.
4. Nutzer klickt auf einen Baum.
5. Ein Bottom Sheet mit Informationen zum Baum öffnet sich.
6. Nutzer startet eine Mission.
7. Nutzer bestätigt den Baum bzw. beantwortet eine einfache Frage.
8. Die Mission wird abgeschlossen.
9. Nutzer erhält XP.
10. Der Baum wird seinem persönlichen Baumbuch hinzugefügt.
11. Der Status des Baumes verändert sich auf der Karte.

Der Flow soll sich bereits wie ein kleines Spiel anfühlen.

---

## 2. Technischer Stack

Bitte verwende:

- Next.js
- React
- TypeScript
- Tailwind CSS
- Leaflet oder MapLibre für die Karte
- Lucide Icons
- localStorage für persistente Demo-Daten

Keine Backend-Anbindung.

Keine Datenbank.

Keine Authentifizierung.

Keine externe API außer der Kartenquelle.

Die Architektur soll allerdings so aufgebaut sein, dass Mockdaten später problemlos durch eine API ersetzt werden können.

---

## 3. Fokus

Mobile First.

Primäre Zielgröße: **390 × 844 px**.

Die App soll aber auch auf Desktop funktionieren.

Desktop ist zunächst nur eine responsive Erweiterung und nicht die primäre UX.

---

## 4. App-Struktur

Die App besitzt zunächst vier Hauptbereiche:

### Karte

Hauptansicht der App.

Route: `/`

Hier findet die eigentliche Entdeckung statt.

### Baumbuch

Route: `/collection`

Zeigt alle bereits entdeckten Baumarten.

### Missionen

Route: `/missions`

Zeigt aktive und abgeschlossene Missionen.

Für den ersten Prototyp reicht eine einfache Übersicht.

### Profil

Route: `/profile`

Zeigt:

- Level
- XP
- entdeckte Bäume
- abgeschlossene Missionen
- Badges

---

## 5. Navigation

Mobile Bottom Navigation mit vier Menüpunkten:

- Karte
- Missionen
- Baumbuch
- Profil

Die Navigation ist fixed am unteren Bildschirmrand.

Die Karte selbst soll dadurch weiterhin möglichst viel Bildschirmfläche bekommen.

---

## 6. Hauptscreen – Karte

Die Karte ist das zentrale Element.

Sie soll nahezu fullscreen dargestellt werden.

### Header

Oben liegt ein schwebendes UI-Element mit OpenQuest-Logo bzw. Schriftzug.

Daneben:

- Level-Anzeige, z. B. `Level 3`
- XP-Fortschritt, z. B. `340 / 500 XP`

Optional als kleiner Progressbar.

---

## 7. Kartenmarker

Auf der Karte befinden sich zunächst ca. 20 Mock-Bäume rund um Münster.

Jeder Baum enthält:

```ts
type Tree = {
  id: string
  lat: number
  lng: number

  species?: string
  speciesLatin?: string

  status:
    | "unverified"
    | "verified"
    | "missing"
    | "new"

  verificationCount: number

  rarity:
    | "common"
    | "uncommon"
    | "rare"

  discovered: boolean

  xpReward: number
}
```

---

## 8. Markerstatus

Marker sollen unterschiedliche Zustände visuell anzeigen.

### Noch nicht überprüft

- Auffällig
- Mission verfügbar

### Bereits überprüft

- Dezenter
- Grüner Check oder ähnliches Symbol

### Vom Nutzer entdeckt

- Zusätzliche persönliche Markierung

### Neuer / gemeldeter Baum

- Eigener visueller Zustand

---

## 9. Baum öffnen

Beim Tippen auf einen Baum öffnet sich ein **Bottom Sheet**.

Die Karte bleibt im Hintergrund sichtbar.

Das Bottom Sheet soll ungefähr **55–70 % des Screens** einnehmen.

---

## 10. Inhalt Baum Bottom Sheet

Beispiel:

### Stieleiche

*Quercus robur*

**Status:** Noch nicht bestätigt

**Informationen:**

- Entfernung: 120 m
- Zuletzt geprüft: vor 3 Jahren
- 2 bisherige Bestätigungen
- Seltenheit: häufig

### Mission verfügbar

**Ist dieser Baum noch vorhanden?**

Belohnung: `+25 XP`

Button: **Mission starten**

---

## 11. Mission Flow

Nach Klick auf „Mission starten“ öffnet sich eine Mission.

Sie kann entweder als neues Bottom Sheet oder als Fullscreen Overlay umgesetzt werden.

---

## 12. Mission 1 – Baum vorhanden?

Frage:

### Steht dieser Baum noch hier?

Antwortmöglichkeiten:

- **Ja, Baum gefunden**
- **Baum nicht vorhanden**
- **Unsicher**

Primärer Use Case ist: `Ja, Baum gefunden`.

---

## 13. Zweiter Missionsschritt

Danach:

### Welche Baumart siehst du?

Für den Prototyp gibt es drei Optionen plus eine Unsicherheitsoption.

Beispiel:

- Stieleiche
- Rotbuche
- Ahorn
- Weiß ich nicht

Die richtige Antwort muss im Prototype nicht validiert werden.

Wir simulieren lediglich die Datenerfassung.

---

## 14. Optionaler Foto-Step

Zusätzlich kann ein UI-Step existieren:

### Foto hinzufügen

Button: `Foto aufnehmen`

Im Prototype muss kein echter Upload implementiert werden.

Ein Platzhalter bzw. File Input reicht.

Dieser Schritt darf übersprungen werden.

---

## 15. Mission abgeschlossen

Nach Abschluss erscheint eine Reward-Animation.

Beispiel:

# +25 XP

**Baum entdeckt!**

Stieleiche

`Neu im Baumbuch`

Button: **Weiter erkunden**

Optional kleine Animation:

- Scale
- Partikel
- Confetti

Nicht übertreiben.

---

## 16. Fortschritt speichern

Nach Abschluss soll folgendes in localStorage gespeichert werden:

```ts
type PlayerProgress = {
  xp: number
  level: number

  discoveredTrees: string[]

  discoveredSpecies: string[]

  completedMissions: string[]
}
```

Dadurch bleibt der Spielfortschritt beim Neuladen erhalten.

---

## 17. Levelsystem

Einfaches System für den Prototype.

| Level | XP-Bereich |
|---|---:|
| 1 | 0–99 XP |
| 2 | 100–249 XP |
| 3 | 250–499 XP |
| 4 | 500–799 XP |
| 5 | 800+ XP |

Bitte die Levelberechnung zentral als Utility implementieren.

---

## 18. Baumbuch

Route: `/collection`

Das Baumbuch soll wie eine Sammlung funktionieren, nicht wie eine Datentabelle.

Cards mit Baumarten.

Beispiel:

### Stieleiche

*Quercus robur*

Gefunden: `3x`

Status: `Entdeckt`

Noch nicht entdeckte Baumarten erscheinen ausgegraut.

Beispiel:

### ???

Noch nicht entdeckt

Dadurch soll ein Sammeltrieb entstehen.

---

## 19. Beispiel-Baumarten

Für den Prototype:

1. Stieleiche
2. Rotbuche
3. Bergahorn
4. Spitzahorn
5. Winterlinde
6. Rosskastanie
7. Platane
8. Birke
9. Eberesche
10. Ginkgo

Nicht alle müssen bereits auf der Karte vorkommen.

---

## 20. Missionsübersicht

Route: `/missions`

### In deiner Nähe

Beispiel:

Baum überprüfen

120 m

`+25 XP`

### Abgeschlossen

Beispiel:

Stieleiche bestätigt

`+25 XP`

Missionen sollen aus den vorhandenen Mock-Bäumen generiert werden.

---

## 21. Profil

Route: `/profile`

Zeige:

- Avatar Placeholder
- Spielername: `Explorer`
- Level
- XP

Statistiken:

- Bäume entdeckt
- Baumarten entdeckt
- Missionen abgeschlossen
- XP gesammelt

---

## 22. Badges

Drei Demo-Badges:

### Erster Fund

Ersten Baum bestätigt.

### Baumfreund

5 Bäume bestätigt.

### Entdecker

3 unterschiedliche Arten gefunden.

Badges automatisch anhand des localStorage-Fortschritts berechnen.

---

## 23. Designrichtung

Die App soll modern wirken, nicht wie eine kommunale Verwaltungssoftware.

Stilrichtung:

- Outdoor
- Exploration
- Natur
- Spielerisch
- Clean
- Modern

Keine Pokémon-Kopie.

Keine Fantasy-Optik.

Keine übertriebenen Gaming-UIs.

---

## 24. Designsystem

Grundfarben:

- Naturgrün als Primary
- Heller warmer Hintergrund
- Dunkles Grün / Anthrazit für Text
- Akzentfarbe für XP / Rewards

Bitte CSS-Variablen bzw. Tailwind Theme Tokens verwenden.

Beispiel:

```css
--background
--surface
--primary
--primary-dark
--accent
--text
--text-muted
--border
```

---

## 25. UI-Stil

### Cards

- Stark abgerundete Ecken
- Leichte Schatten
- Klare Hierarchie

### Buttons

- Groß
- Touch-friendly
- Mindestens 48 px Höhe

### Bottom Sheets

- Große Border Radius oben
- Drag Handle
- Leichtes Overlay

---

## 26. Animationen

Subtil einsetzen.

Zum Beispiel:

- Marker selected: Scale
- Bottom Sheet: Slide-up
- XP Reward: Scale + Fade
- Collection Unlock: kurze Animation

Keine komplexen Animation Libraries notwendig.

CSS / Tailwind reicht.

---

## 27. Mockdaten

Erstelle eine Datei:

`/data/trees.ts`

mit mindestens 20 Beispielbäumen.

Koordinaten sollen ungefähr im Gebiet Münster liegen.

Die Daten dürfen für den Prototype erfunden sein.

---

## 28. Architektur

Vorschlag:

```text
src/

app/
  page.tsx

  collection/
    page.tsx

  missions/
    page.tsx

  profile/
    page.tsx

components/

  map/
    ExplorerMap.tsx
    TreeMarker.tsx

  tree/
    TreeBottomSheet.tsx
    TreeStatus.tsx

  mission/
    MissionFlow.tsx
    MissionStep.tsx
    MissionReward.tsx

  collection/
    SpeciesCard.tsx

  navigation/
    BottomNavigation.tsx

  ui/
    Button.tsx
    Card.tsx
    ProgressBar.tsx
    BottomSheet.tsx

data/
  trees.ts
  species.ts

lib/
  progress.ts
  levels.ts

types/
  tree.ts
  player.ts
```

Die genaue Struktur darf sinnvoll angepasst werden.

---

## 29. State Management

Kein Redux.

React State + Context reicht.

Erstelle beispielsweise `PlayerContext`.

Dieser verwaltet:

- XP
- Level
- Gefundene Bäume
- Baumarten
- Abgeschlossene Missionen

Synchronisation mit localStorage.

---

## 30. Geolocation

Browser Geolocation verwenden.

Wenn erlaubt: eigene Position auf Karte anzeigen.

Falls nicht erlaubt: Default Position Münster Zentrum.

Beispiel ungefähr:

- Breitengrad: `51.9607`
- Längengrad: `7.6261`

Die App muss auch ohne Standortfreigabe funktionieren.

---

## 31. Entfernung

Berechne optional die Entfernung zwischen User und Baum.

Eine einfache Haversine-Funktion reicht.

Darstellung:

- `120 m entfernt`
- `1,2 km entfernt`

---

## 32. Fake Proximity

Für den Hackathon soll man Missionen testen können, ohne wirklich durch Münster zu laufen.

Deshalb dürfen Missionen grundsätzlich gestartet werden.

Optional zusätzlich ein Entwickler-Schalter: `Demo Mode`.

Im Demo Mode gelten alle Bäume als erreichbar.

---

## 33. Neuer Baum

Auf der Karte soll ein Floating Action Button existieren: `+`.

Nach Klick:

### Baum entdeckt?

User kann einen neuen Baum melden.

Prototype Flow:

1. Position verwenden
2. Baumart auswählen
3. Optional Foto
4. Absenden

Danach erscheint der Baum als `new` auf der Karte.

Neue Bäume ebenfalls in localStorage speichern.

---

## 34. Wichtig für Hackathon-Demo

Der Prototype muss vor allem folgende Story überzeugend demonstrieren:

### Problem

Open-Data-Baumdaten können veraltet sein.

### Lösung

OpenQuest verwandelt Datenvalidierung in ein Spiel.

### Nutzeraktion

Spieler bestätigt einen Baum.

### Ergebnis

Der zugrunde liegende Datensatz erhält eine neue Beobachtung.

Im Frontend sollte deshalb nach Missionsabschluss optional kurz sichtbar werden:

`Datensatz aktualisiert`

Aber sekundär.

Die Hauptbotschaft bleibt:

**+25 XP – Baum entdeckt!**

Der User soll also zuerst die Belohnung sehen und nicht den Verwaltungsprozess.

---

## 35. Datenmodell für spätere Backend-Anbindung

Missionsergebnis intern ungefähr so vorbereiten:

```ts
type TreeObservation = {
  treeId: string

  userId?: string

  timestamp: string

  coordinates: {
    lat: number
    lng: number
  }

  exists:
    | true
    | false
    | null

  suggestedSpecies?: string

  photo?: string

  confidence?: number
}
```

Im Prototype wird dieses Objekt nur erstellt und lokal gespeichert bzw. in der Console ausgegeben.

Später kann exakt dieses Objekt an eine API gesendet werden.

---

## 36. Entwickleranforderungen

Bitte:

- Saubere TypeScript Types
- Wiederverwendbare Components
- Keine riesigen Components
- Verständliche Dateistruktur
- Responsive Umsetzung
- Mobile Touch UX berücksichtigen
- Accessibility Basics
- Sinnvolle Loading- und Empty-States
- Keine unnötigen Dependencies

---

## 37. Umsetzung in Phasen

Bitte nicht alles gleichzeitig bauen.

### Phase 1

Projektstruktur erstellen.

Danach:

- Globale Styles
- Navigation
- Mockdaten
- Player Context

### Phase 2

Karte bauen.

- Münster anzeigen
- Marker rendern
- Marker auswählbar
- Tree Bottom Sheet

### Phase 3

Mission Flow.

- Mission starten
- Fragen beantworten
- XP vergeben
- localStorage aktualisieren
- Reward Screen

### Phase 4

Baumbuch.

- Species Cards
- Locked / Unlocked State
- Anzahl Funde

### Phase 5

Profil und Missionen.

### Phase 6

Neuen Baum melden.

### Phase 7

Polish.

- Animationen
- States
- Responsive Desktop
- Kleine UX-Verbesserungen

---

## 38. Erste Aufgabe für Codex

**Beginne jetzt ausschließlich mit Phase 1 und Phase 2.**

Erstelle einen funktionierenden ersten Stand mit:

- Next.js App
- TypeScript
- Tailwind
- Karte von Münster
- Mindestens 20 Mock-Bäumen
- Bottom Navigation
- Auswählbaren Markern
- Tree Bottom Sheet
- Grundlegender Player Context Struktur

**Noch keinen vollständigen Mission Flow implementieren.**

Achte aber darauf, dass die Architektur bereits darauf vorbereitet ist.

Nach Abschluss:

1. Prüfe TypeScript-Fehler.
2. Prüfe Build.
3. Räume offensichtliche Fehler auf.
4. Gib eine kurze Übersicht über die erstellten Dateien.
5. Beschreibe anschließend, was als Nächstes für Phase 3 umgesetzt werden sollte.
