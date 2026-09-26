import type { TerritoryContribution } from '@/types/district';

type DemoRecord = [userId: string, treeId: string, daysAgo: number, reviewStatus?: TerritoryContribution['reviewStatus']];

const records: DemoRecord[] = [
  ['mara', 'ms-001', 2], ['mara', 'ms-002', 6], ['jonas', 'ms-003', 4],
  ['jonas', 'ms-004', 2], ['jonas', 'ms-018', 8], ['mara', 'ms-019', 3],
  ['aylin', 'ms-005', 1], ['aylin', 'ms-011', 7], ['jonas', 'ms-023', 3],
  ['mara', 'ms-006', 3], ['mara', 'ms-007', 9], ['mara', 'ms-008', 11],
  ['aylin', 'ms-009', 5], ['aylin', 'ms-017', 12], ['jonas', 'ms-024', 2],
  ['jonas', 'ms-010', 2], ['aylin', 'ms-015', 10],
  ['aylin', 'ms-014', 5], ['aylin', 'ms-022', 8],
  // Prüf- und Frischefälle: beide zählen bewusst nicht zur Rangliste.
  ['mara', 'ms-001', 1, 'pending'], ['aylin', 'ms-020', 45],
];

export function getDemoTerritoryContributions(now = new Date()): TerritoryContribution[] {
  return records.map(([userId, treeId, daysAgo, reviewStatus = 'verified']) => {
    const observedAt = new Date(now.getTime() - daysAgo * 24 * 60 * 60 * 1000).toISOString();
    return { userId, treeId, reviewStatus, source: 'demo', observedAt, reviewedAt: observedAt };
  });
}
