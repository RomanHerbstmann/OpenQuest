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
  treeId: string;
  assetId: string | null;
  species: string;
  scannedAt: string;
  observationId: string | null;
  source: 'demo';
};
