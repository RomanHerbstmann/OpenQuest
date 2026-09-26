import { demoObservations } from '@/data/observations';
import type { Observation, ReviewStatus } from '@/types/observation';

export const OBSERVATIONS_STORAGE_KEY = 'openquest-observations-v1';
const reviewStatuses: ReviewStatus[] = ['pending', 'needs_review', 'verified', 'rejected'];

export function loadObservations(): Observation[] {
  try {
    const raw = window.localStorage.getItem(OBSERVATIONS_STORAGE_KEY);
    if (!raw) return demoObservations;
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return demoObservations;
    const saved = parsed.filter((item): item is Observation => {
      if (!item || typeof item !== 'object') return false;
      const value = item as Record<string, unknown>;
      return typeof value.id === 'string' && typeof value.lat === 'number' && typeof value.lng === 'number' && reviewStatuses.includes(value.reviewStatus as ReviewStatus);
    });
    const byId = new Map(saved.map((item) => [item.id, item]));
    return [...demoObservations.map((item) => byId.get(item.id) ?? item), ...saved.filter((item) => !demoObservations.some((demo) => demo.id === item.id))];
  } catch {
    return demoObservations;
  }
}

export function saveObservations(items: Observation[]) {
  window.localStorage.setItem(OBSERVATIONS_STORAGE_KEY, JSON.stringify(items));
}

function csvCell(value: string | number | null) {
  const text = value == null ? '' : String(value);
  // Prevent spreadsheet apps from evaluating a text field as a formula.
  const safe = /^[=+@\-\t\r]/.test(text) ? `'${text}` : text;
  return `"${safe.replaceAll('"', '""')}"`;
}

export function observationsToCsv(items: Observation[]) {
  const columns = ['observation_id', 'tree_id', 'action', 'observed_at', 'latitude', 'longitude', 'accuracy_m', 'suggested_species', 'review_status', 'reviewed_at', 'review_note', 'source', 'user_id'];
  const rows = items.map((item) => [item.id, item.treeId, item.action, item.observedAt, item.lat, item.lng, item.accuracyMeters, item.suggestedSpecies, item.reviewStatus, item.reviewedAt ?? null, item.reviewNote, item.source, item.userId ?? null].map(csvCell).join(','));
  return '\uFEFF' + [columns.join(','), ...rows].join('\r\n');
}

export function observationsToGeoJson(items: Observation[]) {
  return JSON.stringify({
    type: 'FeatureCollection',
    name: 'OpenQuest Demo-Beobachtungen',
    metadata: { demoData: true, note: 'Projekt-Prüfstatus; keine amtliche Freigabe oder Änderung des städtischen Datensatzes.' },
    features: items.map((item) => ({
      type: 'Feature',
      id: item.id,
      geometry: { type: 'Point', coordinates: [item.lng, item.lat] },
      properties: { observation_id: item.id, tree_id: item.treeId, action: item.action, observed_at: item.observedAt, accuracy_m: item.accuracyMeters, suggested_species: item.suggestedSpecies, review_status: item.reviewStatus, reviewed_at: item.reviewedAt ?? null, review_note: item.reviewNote, source: item.source, user_id: item.userId ?? null },
    })),
  }, null, 2);
}

/** Stores a photo scan of a quest tree so it shows up in the review queue (admin) with the verifier's outcome. */
export function recordScanObservation(entry: {
  treeId: string | null;
  lat: number;
  lng: number;
  accuracyMeters: number | null;
  suggestedSpecies: string | null;
  reviewStatus: Extract<ReviewStatus, 'verified' | 'needs_review'>;
  reviewNote: string;
}) {
  const now = new Date().toISOString();
  const observation: Observation = {
    id: `scan-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 7)}`,
    action: 'exists',
    observedAt: now,
    source: 'local',
    ...entry,
    ...(entry.reviewStatus === 'verified' ? { reviewedAt: now } : {}),
  };
  try {
    saveObservations([...loadObservations(), observation]);
  } catch {
    // A full or blocked localStorage must not break the scan result.
  }
  return observation;
}
