export type LatLng = [lat: number, lng: number];

export type District = {
  id: string;
  name: string;
  shortName: string;
  center: LatLng;
  polygon: LatLng[];
};

export type TerritoryContribution = {
  userId: string;
  treeId: string;
  observedAt: string;
  reviewedAt: string;
  reviewStatus: 'verified' | 'pending' | 'rejected' | 'needs_review';
  source: 'demo' | 'local';
};
