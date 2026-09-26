'use client';

import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { initialProgress, restoreProgress, withDiscovery, withScan } from '@/lib/playerProgress';
import type { PlayerProgress, ScanEvent } from '@/types/player';

const STORAGE_KEY = 'openquest-player-v3';
const LEGACY_STORAGE_KEY = 'openquest-player-v1';

type PlayerContextValue = {
  progress: PlayerProgress;
  ready: boolean;
  recordDiscovery: (treeId: string, species: string, xp: number) => void;
  recordScan: (event: ScanEvent, xpReward?: number) => void;
};

const PlayerContext = createContext<PlayerContextValue | null>(null);
export function PlayerProvider({ children }: { children: ReactNode }) {
  const [progress, setProgress] = useState<PlayerProgress>(initialProgress);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    try {
      const stored = window.localStorage.getItem(STORAGE_KEY);
      const legacy = stored ? null : window.localStorage.getItem(LEGACY_STORAGE_KEY);
      if (stored || legacy) setProgress(restoreProgress(JSON.parse(stored ?? legacy!) as Partial<PlayerProgress>, !stored));
    } catch {
      // A damaged local demo save does not prevent the app from starting.
    }
    setReady(true);
  }, []);

  useEffect(() => {
    if (!ready) return;
    try { window.localStorage.setItem(STORAGE_KEY, JSON.stringify(progress)); }
    catch { /* Browser storage can be unavailable; the current session still works. */ }
  }, [progress, ready]);

  const recordDiscovery = (treeId: string, species: string, xpReward: number) => {
    setProgress((current) => withDiscovery(current, treeId, species, xpReward));
  };

  const recordScan = (event: ScanEvent, xpReward = 0) => {
    setProgress((current) => withScan(current, event, xpReward));
  };

  return <PlayerContext.Provider value={{ progress, ready, recordDiscovery, recordScan }}>{children}</PlayerContext.Provider>;
}

export function usePlayer() {
  const context = useContext(PlayerContext);
  if (!context) throw new Error('usePlayer benötigt PlayerProvider');
  return context;
}
