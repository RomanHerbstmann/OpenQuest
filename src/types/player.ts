export type PlayerProgress = {
  xp: number;
  level: number;
  discoveredTrees: string[];
  discoveredSpecies: string[];
  scannedSpecies: string[];
  completedMissions: string[];
  unlockedCards: string[];
  scanEvents: ScanEvent[];
};

export type ScanEvent = {
  id: string;
  /** Null for a free scan without a chosen tree point. */
  treeId: string | null;
  assetId: string | null;
  species: string;
  scannedAt: string;
  observationId: string | null;
  /** demo: local fallback without photo check; verified: approved by /api/verify; quest: submitted live quest. */
  source: 'demo' | 'verified' | 'quest';
};
