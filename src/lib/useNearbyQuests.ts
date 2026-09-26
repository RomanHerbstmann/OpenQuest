'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { liveText } from '@/i18n/liveGame';
import { api, ApiError, type QuestDto } from '@/lib/api';
import { distanceMeters } from '@/lib/distance';

type Point = { lat: number; lng: number };

export const NEARBY_RADIUS_M = 600;
/** Panning less than this keeps the loaded quests: the loaded circle still covers the view. */
const RELOAD_DISTANCE_M = 200;
const DEBOUNCE_MS = 600;
const CACHE_TTL_MS = 60_000;

// Module level so the cache survives navigating between the map and other pages.
const cache = new Map<string, { at: number; quests: QuestDto[] }>();
const cacheKey = ({ lat, lng }: Point) => `${lat.toFixed(3)},${lng.toFixed(3)}`;

export function invalidateNearbyQuests() { cache.clear(); }

/** Merges two nearby answers by quest id and keeps them sorted nearest first. */
export function mergeNearby(...lists: QuestDto[][]): QuestDto[] {
  const byId = new Map<string, QuestDto>();
  for (const list of lists) for (const quest of list) byId.set(quest.id, quest);
  return [...byId.values()].sort((a, b) => (a.distanceMeters ?? Infinity) - (b.distanceMeters ?? Infinity));
}

/**
 * Loads open quests around `center` (player position, else map center) while `enabled`.
 * The API returns at most 200 quests and the dense photo campaign would crowd out the few "which tree is this"
 * quests, so both are requested and merged.
 */
export function useNearbyQuests(enabled: boolean, center: Point | null) {
  const [quests, setQuests] = useState<QuestDto[]>([]);
  const [loading, setLoading] = useState(false);
  /** At least one answer arrived for the current session; before that an empty list means "not loaded yet". */
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState('');
  const [tick, setTick] = useState(0);
  const loadedAt = useRef<Point | null>(null);

  const reload = useCallback(() => { invalidateNearbyQuests(); loadedAt.current = null; setTick((value) => value + 1); }, []);

  useEffect(() => {
    if (!enabled) { setQuests([]); setError(''); setLoaded(false); loadedAt.current = null; return; }
    if (!center) return;
    if (loadedAt.current && distanceMeters(loadedAt.current, center) < RELOAD_DISTANCE_M) return;

    const key = cacheKey(center);
    const cached = cache.get(key);
    if (cached && Date.now() - cached.at < CACHE_TTL_MS) {
      loadedAt.current = center;
      setQuests(cached.quests);
      setLoaded(true);
      setError('');
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      setLoading(true);
      try {
        const [all, verify] = await Promise.all([
          api.nearbyQuests(center.lat, center.lng, NEARBY_RADIUS_M, undefined, controller.signal),
          api.nearbyQuests(center.lat, center.lng, NEARBY_RADIUS_M, 'verify_attribute', controller.signal),
        ]);
        const merged = mergeNearby(all, verify);
        cache.set(key, { at: Date.now(), quests: merged });
        loadedAt.current = center;
        setQuests(merged);
        setLoaded(true);
        setError('');
      } catch (cause) {
        if (controller.signal.aborted) return;
        setError(cause instanceof ApiError ? cause.message : liveText.de.map.error);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    }, DEBOUNCE_MS);
    return () => { window.clearTimeout(timer); controller.abort(); setLoading(false); };
    // `center` is compared by value above; its identity changes on every map move.
  }, [enabled, center?.lat, center?.lng, tick]); // eslint-disable-line react-hooks/exhaustive-deps

  return { quests, loading: loading || (enabled && !loaded && !error), error, reload };
}
