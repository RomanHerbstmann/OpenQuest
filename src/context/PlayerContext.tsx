'use client';

import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { levelForXp } from '@/lib/levels';
import type { PlayerProgress } from '@/types/player';

const STORAGE_KEY = 'openquest-player-v1';
const initialProgress: PlayerProgress = { xp: 0, level: 1, discoveredTrees: [], discoveredSpecies: [], completedMissions: [] };

type PlayerContextValue = {
  progress: PlayerProgress;
  ready: boolean;
  recordDiscovery: (treeId: string, species: string, xp: number) => void;
};

const PlayerContext = createContext<PlayerContextValue | null>(null);

export function PlayerProvider({ children }: { children: ReactNode }) {
  const [progress, setProgress] = useState<PlayerProgress>(initialProgress);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    try {
      const stored = window.localStorage.getItem(STORAGE_KEY);
      if (stored) {
        const value = JSON.parse(stored) as Partial<PlayerProgress>;
        const xp = typeof value.xp === 'number' && Number.isFinite(value.xp) ? Math.max(0, value.xp) : 0;
        setProgress({
          xp,
          level: levelForXp(xp),
          discoveredTrees: Array.isArray(value.discoveredTrees) ? value.discoveredTrees.filter((item): item is string => typeof item === 'string') : [],
          discoveredSpecies: Array.isArray(value.discoveredSpecies) ? value.discoveredSpecies.filter((item): item is string => typeof item === 'string') : [],
          completedMissions: Array.isArray(value.completedMissions) ? value.completedMissions.filter((item): item is string => typeof item === 'string') : [],
        });
      }
    } catch {
      // Ein beschädigter lokaler Spielstand verhindert den App-Start nicht.
    }
    setReady(true);
  }, []);

  useEffect(() => {
    if (ready) window.localStorage.setItem(STORAGE_KEY, JSON.stringify(progress));
  }, [progress, ready]);

  const recordDiscovery = (treeId: string, species: string, xp: number) => {
    setProgress((current) => {
      if (current.completedMissions.includes(treeId)) return current;
      const totalXp = current.xp + xp;
      return {
        xp: totalXp,
        level: levelForXp(totalXp),
        discoveredTrees: [...current.discoveredTrees, treeId],
        discoveredSpecies: current.discoveredSpecies.includes(species) ? current.discoveredSpecies : [...current.discoveredSpecies, species],
        completedMissions: [...current.completedMissions, treeId],
      };
    });
  };

  return <PlayerContext.Provider value={{ progress, ready, recordDiscovery }}>{children}</PlayerContext.Provider>;
}

export function usePlayer() {
  const context = useContext(PlayerContext);
  if (!context) throw new Error('usePlayer benötigt PlayerProvider');
  return context;
}
