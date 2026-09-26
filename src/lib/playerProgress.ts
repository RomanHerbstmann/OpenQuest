import { cardArtBySpecies, initiallyUnlockedCards } from '@/data/cardArt';
import { levelForXp } from '@/lib/levels';
import type { PlayerProgress, ScanEvent } from '@/types/player';

export const initialProgress: PlayerProgress = {
  xp: 0, level: 1, discoveredTrees: [], discoveredSpecies: [], scannedSpecies: [],
  completedMissions: [], unlockedCards: [...initiallyUnlockedCards], scanEvents: [],
};

const strings = (value: unknown) => Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
const hasCardArt = (name: string) => Boolean(cardArtBySpecies[name as keyof typeof cardArtBySpecies]);

export function restoreProgress(value: Partial<PlayerProgress>, legacy: boolean): PlayerProgress {
  const xp = typeof value.xp === 'number' && Number.isFinite(value.xp) ? Math.max(0, value.xp) : 0;
  const events = !legacy && Array.isArray(value.scanEvents) ? value.scanEvents.filter((event): event is ScanEvent =>
    Boolean(event && typeof event.id === 'string' && typeof event.treeId === 'string' && typeof event.species === 'string' && typeof event.scannedAt === 'string' && Number.isFinite(Date.parse(event.scannedAt)) && (event.assetId === null || typeof event.assetId === 'string')),
  ) : [];
  // v1 discoveries remain historical data. They do not reveal cards in v3.
  const unlockedCards = legacy ? [] : strings(value.unlockedCards);
  return {
    xp, level: levelForXp(xp),
    discoveredTrees: strings(value.discoveredTrees),
    discoveredSpecies: strings(value.discoveredSpecies),
    scannedSpecies: strings(value.scannedSpecies),
    completedMissions: strings(value.completedMissions),
    unlockedCards: [...new Set([...initiallyUnlockedCards, ...unlockedCards])],
    scanEvents: events,
  };
}

export function withDiscovery(current: PlayerProgress, treeId: string, species: string, xpReward: number): PlayerProgress {
  if (current.completedMissions.includes(treeId)) return current;
  const canReveal = species !== 'Festtanne' || treeId === 'presentation-festtanne';
  const xp = current.xp + (canReveal ? Math.max(0, Number.isFinite(xpReward) ? xpReward : 0) : 0);
  return {
    ...current, xp, level: levelForXp(xp),
    discoveredTrees: current.discoveredTrees.includes(treeId) ? current.discoveredTrees : [...current.discoveredTrees, treeId],
    discoveredSpecies: current.discoveredSpecies.includes(species) ? current.discoveredSpecies : [...current.discoveredSpecies, species],
    unlockedCards: canReveal && hasCardArt(species) && !current.unlockedCards.includes(species) ? [...current.unlockedCards, species] : current.unlockedCards,
    completedMissions: [...current.completedMissions, treeId],
  };
}

export function withScan(current: PlayerProgress, event: ScanEvent, xpReward = 0): PlayerProgress {
  if (current.scanEvents.some((scan) => scan.id === event.id)) return current;
  const canReveal = event.species !== 'Festtanne' || event.treeId === 'presentation-festtanne';
  const firstUnlock = !current.unlockedCards.includes(event.species);
  const xp = current.xp + (canReveal && firstUnlock ? Math.max(0, Number.isFinite(xpReward) ? xpReward : 0) : 0);
  return {
    ...current, xp, level: levelForXp(xp),
    discoveredTrees: current.discoveredTrees.includes(event.treeId) ? current.discoveredTrees : [...current.discoveredTrees, event.treeId],
    discoveredSpecies: current.discoveredSpecies.includes(event.species) ? current.discoveredSpecies : [...current.discoveredSpecies, event.species],
    scannedSpecies: current.scannedSpecies.includes(event.species) ? current.scannedSpecies : [...current.scannedSpecies, event.species],
    unlockedCards: canReveal && hasCardArt(event.species) && firstUnlock ? [...current.unlockedCards, event.species] : current.unlockedCards,
    scanEvents: [...current.scanEvents, event],
  };
}
