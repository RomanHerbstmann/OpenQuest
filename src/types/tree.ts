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
};
