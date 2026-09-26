import type { Reason, TreeVerificationResult, Verdict } from '@openquest/tree-verification';
import { species } from '@/data/species';

export type SpeciesName = (typeof species)[number]['name'];

export const verdictText: Record<Verdict, { label: string; summary: string }> = {
  approve: { label: 'Bestätigt', summary: 'Dein Foto zeigt den gesuchten Baum.' },
  review: { label: 'Wird geprüft', summary: 'Wir sind uns nicht ganz sicher. Dein Fund wird vorgemerkt und später geprüft.' },
  reject: { label: 'Abgelehnt', summary: 'Dieses Foto können wir nicht werten.' },
};

// Player facing texts per reason code. Unknown codes (e.g. from newer package versions) fall back to a generic text.
const reasonText: Record<string, string> = {
  invalid_image: 'Das Bild konnte nicht gelesen werden.',
  image_too_large: 'Das Bild ist zu groß.',
  outside_geofence: 'Du bist zu weit vom Baum entfernt. Geh näher heran und versuche es noch einmal.',
  gps_inaccurate: 'Dein Standort ist gerade sehr ungenau.',
  stale_capture: 'Das Foto wurde nicht gerade eben aufgenommen.',
  no_tree: 'Auf dem Foto ist kein Baum zu erkennen.',
  tree_uncertain: 'Es ist nicht sicher, ob das Foto einen Baum zeigt.',
  models_disagree: 'Die Bilderkennung ist sich uneinig.',
  not_a_live_photo: 'Das sieht nach einem Bildschirm, Bild oder Ausdruck aus, nicht nach einem echten Baum vor Ort.',
  possibly_not_live_photo: 'Möglicherweise ist das kein Foto, das vor Ort aufgenommen wurde.',
  poor_image_quality: 'Das Foto ist unscharf, zu dunkel oder der Baum ist zu klein im Bild.',
  genus_mismatch: 'Die erkannte Gattung passt nicht zum Baum im Kataster.',
  possibly_neighbor_tree: 'Vielleicht hast du einen Nachbarbaum fotografiert.',
  no_known_tree_nearby: 'An dieser Stelle ist kein Baum im Kataster bekannt.',
  vision_unavailable: 'Die Bilderkennung war gerade nicht erreichbar.',
  jev_unavailable: 'Die zweite Prüfung war gerade nicht erreichbar.',
  jev_disagrees: 'Eine zweite, unabhängige Prüfung hat Bedenken.',
  tree_confirmed: 'Baum eindeutig erkannt.',
  genus_confirmed: 'Die Gattung passt zum Baum im Kataster.',
};

export function reasonMessage(reason: Reason, result?: TreeVerificationResult): string {
  if (reason.code === 'outside_geofence' && result?.signals.distanceToExpectedM !== undefined) {
    return `Du bist etwa ${Math.round(result.signals.distanceToExpectedM)} m vom Baum entfernt, erlaubt sind ${result.signals.geofenceRadiusM} m. Geh näher heran.`;
  }
  return reasonText[reason.code] ?? 'Weiterer Prüfhinweis.';
}

// German names on genus level, for genera without a card in species.ts too.
const genusNames: Record<string, string> = {
  Abies: 'Tanne', Acer: 'Ahorn', Aesculus: 'Rosskastanie', Alnus: 'Erle', Betula: 'Birke', Carpinus: 'Hainbuche',
  Castanea: 'Esskastanie', Catalpa: 'Trompetenbaum', Corylus: 'Hasel', Crataegus: 'Weißdorn', Fagus: 'Buche',
  Fraxinus: 'Esche', Ginkgo: 'Ginkgo', Gleditsia: 'Gleditschie', Juglans: 'Walnuss', Larix: 'Lärche', Liquidambar: 'Amberbaum',
  Liriodendron: 'Tulpenbaum', Magnolia: 'Magnolie', Malus: 'Apfel', Metasequoia: 'Urweltmammutbaum', Picea: 'Fichte',
  Pinus: 'Kiefer', Platanus: 'Platane', Populus: 'Pappel', Prunus: 'Kirsche', Pyrus: 'Birne', Quercus: 'Eiche',
  Robinia: 'Robinie', Salix: 'Weide', Sophora: 'Schnurbaum', Sorbus: 'Eberesche', Taxus: 'Eibe', Tilia: 'Linde',
  Ulmus: 'Ulme', Zelkova: 'Zelkove',
};

export function genusLabel(genus: string): string {
  const german = genusNames[genus];
  return german ? `${german} (${genus})` : genus;
}

export const genusOf = (latin: string) => latin.split(/\s+/)[0] ?? latin;

/** Card species for a detected genus: the expected species if it has that genus, else the first card of the genus. */
export function speciesForGenus(genus: string | null, expectedSpecies?: string): SpeciesName | null {
  if (!genus) return null;
  const expected = species.find((item) => item.name === expectedSpecies);
  if (expected && genusOf(expected.latin) === genus) return expected.name;
  return species.find((item) => genusOf(item.latin) === genus)?.name ?? null;
}

export const formatPercent = (probability: number) => `${Math.round(probability * 100)} %`;
