export type TreeStatus = 'unverified' | 'verified' | 'missing' | 'new';
export type TreeRarity = 'common' | 'uncommon' | 'rare';

export type Tree = {
  id: string;
  assetId?: string;
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
  inventory?: {
    genus: string;
    genusRaw?: string;
    species?: string | null;
    streetKey: string;
    externalId?: string;
    assetStatus?: string;
    heightM?: number | null;
    district?: string;
    quarter?: string;
    dataSource?: string;
    firstSeenAt?: string;
    lastSeenAt?: string;
    qualityFlags?: string[];
  };
  sampleAsset?: boolean;
  presentation?: boolean;
};
