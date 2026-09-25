export type ObservationAction = 'exists' | 'missing' | 'species_suggestion' | 'new_tree';
export type ReviewStatus = 'pending' | 'needs_review' | 'verified' | 'rejected';

export type Observation = {
  id: string;
  treeId: string | null;
  action: ObservationAction;
  observedAt: string;
  lat: number;
  lng: number;
  accuracyMeters: number | null;
  suggestedSpecies: string | null;
  reviewStatus: ReviewStatus;
  reviewNote: string;
  source: 'demo' | 'local';
};
