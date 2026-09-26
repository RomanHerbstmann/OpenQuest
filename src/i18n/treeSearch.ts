import type { Interpretation } from '@/types/treeSearch';

/**
 * UI texts of the map search. The app has no i18n yet and is German only; the search keeps its
 * texts in this dictionary so an English version can be added next to `de` later.
 */
export const treeSearchText = {
  de: {
    label: 'Echte Stadtbäume suchen',
    placeholder: 'z. B. die größten Birken in Hiltrup',
    submit: 'Suchen',
    clear: 'Suche zurücksetzen',
    loading: 'Jev denkt …',
    interpretation: 'So hat Jev die Suche verstanden',
    source: { jev: 'Jev', rules: 'Regeln' } satisfies Record<Interpretation['source'], string>,
    labels: { sort: 'Sortierung', genus: 'Gattung', area: 'Gebiet', street: 'Straße', height: 'Höhe', intent: 'Absicht' },
    sort: {
      none: 'keine',
      tallest: 'höchste zuerst',
      shortest: 'niedrigste zuerst',
      oldest: 'älteste zuerst',
      youngest: 'jüngste zuerst',
      rarest: 'seltenste zuerst',
      most_common: 'häufigste zuerst',
    } satisfies Record<Interpretation['sort']['value'], string>,
    count: 'Anzahl',
    unknownGenus: 'ohne Gattung',
    notes: {
      age_is_height_proxy: 'Das Alter ist im Baumkataster nicht erfasst. Sortiert wird nach der Höhe aus dem Oberflächenmodell als grobe Näherung.',
      below_2m_excluded: 'Bäume unter 2 m im Oberflächenmodell sind ausgeblendet (meist Jungpflanzung, Rückschnitt oder nicht mehr vorhanden).',
      jev_unavailable: 'Jev war nicht erreichbar. Die Suche wurde mit einfachen Regeln interpretiert.',
    } as Record<string, string>,
    showList: (n: number) => `Top ${n} anzeigen`,
    hideList: 'Liste ausblenden',
    noHeight: 'keine Höhe',
    unknownStreet: 'Straße unbekannt',
    empty: 'Keine Treffer. Versuch es mit einer Baumart, einem Stadtteil oder einer Straße.',
    error: 'Die Suche ist fehlgeschlagen. Bitte versuch es noch einmal.',
    sampled: (shown: number, total: number) => `${shown.toLocaleString('de-DE')} von ${total.toLocaleString('de-DE')} Treffern auf der Karte`,
    source_note: 'Baumkataster Münster · Höhe aus dem Oberflächenmodell NRW',
    focus: (rank: number, name: string) => `Treffer ${rank}, ${name}, auf der Karte zeigen`,
  },
};

export type TreeSearchText = (typeof treeSearchText)['de'];
