import { districts, territoryPlayers } from '@/data/districts';
import { trees } from '@/data/trees';
import type { District, LatLng, TerritoryContribution } from '@/types/district';
import type { Observation } from '@/types/observation';
import type { Tree } from '@/types/tree';

export const TERRITORY_WINDOW_DAYS = 30;

export function isPointInDistrict(point: LatLng, district: District): boolean {
  const [lat, lng] = point;
  let inside = false;
  for (let i = 0, j = district.polygon.length - 1; i < district.polygon.length; j = i, i += 1) {
    const [latI, lngI] = district.polygon[i];
    const [latJ, lngJ] = district.polygon[j];
    const crosses = (latI > lat) !== (latJ > lat) && lng < ((lngJ - lngI) * (lat - latI)) / (latJ - latI) + lngI;
    if (crosses) inside = !inside;
  }
  return inside;
}

export function districtForTree(tree: Tree): District | undefined {
  if (tree.presentation) return undefined;
  return districts.find((district) => isPointInDistrict([tree.lat, tree.lng], district));
}

export function treesInDistrict(district: District): Tree[] {
  return trees.filter((tree) => !tree.presentation && isPointInDistrict([tree.lat, tree.lng], district));
}

export function contributionsFromObservations(observations: Observation[]): TerritoryContribution[] {
  return observations.flatMap((observation) => {
    if (observation.source !== 'local' || !observation.userId || !observation.treeId || !observation.reviewedAt || !['exists', 'species_suggestion'].includes(observation.action)) return [];
    return [{ userId: observation.userId, treeId: observation.treeId, observedAt: observation.observedAt, reviewedAt: observation.reviewedAt, reviewStatus: observation.reviewStatus, source: 'local' as const }];
  });
}

export function districtStandings(district: District, contributions: TerritoryContribution[], now = new Date()) {
  const cutoff = now.getTime() - TERRITORY_WINDOW_DAYS * 24 * 60 * 60 * 1000;
  const treeIds = new Set(treesInDistrict(district).map((tree) => tree.id));
  const uniqueTreesByPlayer = new Map<string, Set<string>>();

  for (const contribution of contributions) {
    const observedAt = Date.parse(contribution.observedAt);
    if (contribution.reviewStatus !== 'verified' || !treeIds.has(contribution.treeId) || !Number.isFinite(observedAt) || observedAt < cutoff || observedAt > now.getTime()) continue;
    const ids = uniqueTreesByPlayer.get(contribution.userId) ?? new Set<string>();
    ids.add(contribution.treeId);
    uniqueTreesByPlayer.set(contribution.userId, ids);
  }

  const ranking = territoryPlayers.map((player) => ({ ...player, count: uniqueTreesByPlayer.get(player.id)?.size ?? 0 }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name, 'de'));
  const topCount = ranking[0]?.count ?? 0;
  const topPlayers = ranking.filter((entry) => entry.count === topCount);
  const owner = topCount > 0 && topPlayers.length === 1 ? ranking[0] : null;
  const playerCount = ranking.find((entry) => entry.id === 'explorer')?.count ?? 0;
  return { ranking, owner, tied: topCount > 0 && topPlayers.length > 1, topCount, playerCount, treesToLead: Math.max(0, topCount + 1 - playerCount) };
}
