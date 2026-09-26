export type TreeStatus = 'unverified' | 'verified' | 'missing' | 'new';
export type TreeRarity = 'common' | 'uncommon' | 'rare';

export type Tree = {
  id: string;
  lat: number;
  lng: number;
  species: string;
  speciesLatin: string;
  status: TreeStatus;
  verificationCount: number;
  rarity: TreeRarity;
  discovered: boolean;
  xpReward: number;
  lastChecked?: string;
  area: string;
  inventory?: { genus: string; streetKey: string };
  presentation?: boolean;
  /** Card and lexicon species when `species` is only a genus level name (live quests). */
  lexiconSpecies?: string;
  /** Set for trees that come from a real quest of the OpenQuest API (live mode). */
  quest?: QuestInfo;
};

export type QuestKind = 'photo' | 'verify';

export type QuestInfo = {
  id: string;
  kind: QuestKind;
  title: string;
  description: string | null;
  rewardPoints: number;
  freeSlots: number;
  geofenceRadiusM: number;
  /** Asset attribute a verify quest asks for, e.g. `genus`. */
  attribute: string | null;
  assetId: string;
  /** Genus in the city inventory, `null` if it is a placeholder. */
  genus: string | null;
  genusRaw: string | null;
  /** The player's active claim on this quest, if any. */
  claim: { id: string; expiresAt: string } | null;
};
