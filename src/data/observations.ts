import type { Observation, ObservationAction, ReviewStatus } from '@/types/observation';
import { trees } from './trees';

type Seed = [string, string | null, ObservationAction, string, number | null, string | null, ReviewStatus, string];

const seeds: Seed[] = [
  ['OBS-1008', 'ms-001', 'exists', '2026-09-25T08:42:00+02:00', 8, null, 'pending', ''],
  ['OBS-1007', 'ms-008', 'species_suggestion', '2026-09-25T08:14:00+02:00', 14, 'Ginkgo', 'needs_review', 'Artvorschlag mit Referenzdaten abgleichen.'],
  ['OBS-1006', 'ms-014', 'missing', '2026-09-24T17:21:00+02:00', 26, null, 'needs_review', 'Weitere unabhängige Beobachtung empfohlen.'],
  ['OBS-1005', 'ms-011', 'exists', '2026-09-24T16:06:00+02:00', 6, null, 'pending', ''],
  ['OBS-1004', 'ms-004', 'exists', '2026-09-24T13:32:00+02:00', 11, null, 'verified', 'Im Demo-Prüfschritt bestätigt.'],
  ['OBS-1003', 'ms-019', 'species_suggestion', '2026-09-23T15:47:00+02:00', 31, 'Rotbuche', 'pending', ''],
  ['OBS-1002', 'ms-007', 'exists', '2026-09-23T11:26:00+02:00', 7, null, 'rejected', 'Doppelmeldung im Demo-Datensatz.'],
  ['OBS-1001', null, 'new_tree', '2026-09-22T10:18:00+02:00', 18, 'Winterlinde', 'pending', ''],
];

export const demoObservations: Observation[] = seeds.map(([id, treeId, action, observedAt, accuracyMeters, suggestedSpecies, reviewStatus, reviewNote]) => {
  const tree = trees.find((item) => item.id === treeId);
  return {
    id, treeId, action, observedAt, accuracyMeters, suggestedSpecies, reviewStatus, reviewNote,
    lat: tree?.lat ?? 51.9602,
    lng: tree?.lng ?? 7.6318,
    source: 'demo',
    reviewedAt: reviewStatus === 'verified' ? observedAt : undefined,
  };
});
