// All texts of the panel (German is the first locale).
export const t = {
  title: 'OpenQuest Admin',
  login: { heading: 'Anmelden', username: 'Benutzername', password: 'Passwort', submit: 'Anmelden', failed: 'Anmeldung fehlgeschlagen.', needAdmin: 'Dieses Konto ist weder Administrator noch Moderator.' },
  nav: { districts: 'Städte & Stadtviertel', moderation: 'Moderation', logout: 'Abmelden', role: { admin: 'Administrator', moderator: 'Moderator' } },
  city: {
    label: 'Stadt', none: 'Noch keine Stadt angelegt.', add: 'Neue Stadt', edit: 'Stadt bearbeiten', create: 'Stadt anlegen',
    name: 'Name', key: 'Kürzel (URL)', country: 'Land (2 Buchstaben)', centerLat: 'Kartenmitte Breite', centerLon: 'Kartenmitte Länge', zoom: 'Zoom', timezone: 'Zeitzone',
    active: 'Aktiv (für Spieler sichtbar)', useCenter: 'Aktuelle Kartenansicht übernehmen', delete: 'Stadt löschen', confirmDelete: 'Diese Stadt wirklich löschen?', saved: 'Stadt gespeichert.', deleted: 'Stadt gelöscht.',
  },
  districts: {
    heading: 'Stadtviertel', empty: 'Noch keine Viertel. Zeichne das erste oder importiere GeoJSON.', draw: 'Viertel zeichnen', import: 'GeoJSON importieren',
    inactive: 'inaktiv', points: 'Punkte', edit: 'Bearbeiten', activate: 'Aktivieren', deactivate: 'Deaktivieren', delete: 'Löschen', confirmDelete: 'Dieses Viertel wirklich löschen?',
    deleted: 'Viertel gelöscht.', saved: 'Viertel gespeichert.',
  },
  editor: {
    newHeading: 'Neues Viertel', editHeading: 'Viertel bearbeiten', name: 'Name', description: 'Beschreibung', color: 'Farbe', active: 'Aktiv',
    hint: 'Klicke auf die Karte, um die Eckpunkte der Reihe nach zu setzen. Ziehen verschiebt einen Punkt, Rechtsklick löscht ihn. Die Nummern zeigen die Reihenfolge; vom letzten Punkt schließt sich die Fläche zum ersten.',
    pointsHeading: 'Eckpunkte (in Reihenfolge)', noPoints: 'Noch keine Punkte.', removeLast: 'Letzten Punkt entfernen', restart: 'Neu beginnen', save: 'Speichern', cancel: 'Abbrechen',
    checking: 'Prüfe …', valid: 'Die Form ist gültig.', needPoints: 'Mindestens 3 Punkte setzen.', needName: 'Name eingeben.', remove: 'Punkt entfernen',
  },
  problems: {
    self_intersection: (p) => `Die Kanten ${p.edgeA + 1} und ${p.edgeB + 1} kreuzen oder berühren sich.`,
    overlaps_district: (p) => `Überschneidet das Viertel „${p.districtName}“ (${Math.round((p.overlapRatio ?? 0) * 1000) / 10} % der kleineren Fläche).`,
    too_few_points: () => 'Mindestens 3 verschiedene Punkte nötig.',
    too_many_points: () => 'Zu viele Punkte (höchstens 5000).',
    duplicate_point: (p) => `Punkt ${p.edgeA + 1} wiederholt sich direkt.`,
    zero_area: () => 'Die Punkte liegen auf einer Linie, die Fläche ist leer.',
    invalid_coordinate: (p) => `Punkt ${p.edgeA + 1} ist keine gültige Position.`,
    holes_not_supported: () => 'Flächen mit Löchern werden nicht unterstützt.',
    unsupported_geometry: () => 'Nur einfache Polygone werden unterstützt (kein MultiPolygon).',
    missing_name: (p) => p.message,
    duplicate_name: (p) => p.message,
    duplicate_key: (p) => p.message,
    invalid_field: (p) => p.message,
  },
  import: {
    heading: 'GeoJSON importieren', file: 'GeoJSON-Datei (FeatureCollection mit Polygonen)', name: 'Eigenschaft mit dem Namen', description: 'Eigenschaft mit der Beschreibung (optional)',
    none: '— keine —', submit: 'Importieren', cancel: 'Abbrechen', done: (n) => `${n} Viertel importiert.`, invalidFile: 'Die Datei ist kein gültiges GeoJSON.',
    failedHeading: 'Nichts wurde importiert. Bitte diese Einträge prüfen:', feature: (i, name) => `Eintrag ${i + 1}${name ? ` („${name}“)` : ''}`,
  },
  leaderboard: { heading: 'Rangliste der Viertel', refresh: 'Aktualisieren', all: 'Gesamt', week: 'Diese Woche', empty: 'Noch keine Viertel.', points: 'Punkte', people: 'Spieler' },
  moderation: {
    heading: 'Abgaben prüfen', pending: 'Offen', approved: 'Freigegeben', rejected: 'Abgelehnt', empty: 'Keine Abgaben.', approve: 'Freigeben', reject: 'Ablehnen',
    reason: 'Grund der Ablehnung', confirmReject: 'Ablehnen bestätigen', approvedDone: 'Freigegeben. Die Punkte werden gutgeschrieben.', rejectedDone: 'Abgelehnt.',
    by: 'von', distance: 'Abstand zum Baum', noPhoto: 'Kein Foto', task: 'Aufgabe', value: 'Angabe', reloadHint: 'Aktualisieren',
  },
  errors: { generic: 'Das hat nicht geklappt.', network: 'Keine Verbindung zum Server.' },
};
