import type { LatLon, NearbyTree, NearbyTreeProvider } from "../types.ts";
import { haversineM } from "./distance.ts";

export interface GeoContext {
  trees: NearbyTree[];
  /** Genus share among nearby trees with a known genus, sorted descending. */
  genusDistribution: { genus: string; count: number }[];
  failedProviders: string[];
}

/** Two trees closer than this are treated as the same physical tree across sources. */
const SAME_TREE_M = 2;

async function withTimeout<T>(ms: number, fn: (signal: AbortSignal) => Promise<T>): Promise<T> {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(new Error(`timeout after ${ms} ms`)), ms);
  try {
    return await fn(ctrl.signal);
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Queries all providers in parallel and merges their trees.
 * Providers are ordered by trust: when two sources report the same tree, the earlier one wins,
 * but a missing genus is filled from a later source.
 */
export async function buildGeoContext(
  providers: NearbyTreeProvider[],
  center: LatLon,
  radiusM: number,
  timeoutMs: number,
): Promise<GeoContext> {
  const settled = await Promise.allSettled(
    providers.map((p) => withTimeout(timeoutMs, (signal) => p.findNearby(center, radiusM, signal))),
  );

  const failedProviders: string[] = [];
  const merged: NearbyTree[] = [];
  settled.forEach((res, i) => {
    if (res.status === "rejected") {
      failedProviders.push(providers[i]!.id);
      return;
    }
    for (const tree of res.value) {
      const distanceM = haversineM(center, tree.position);
      if (distanceM > radiusM) continue;
      const twin = merged.find((t) => haversineM(t.position, tree.position) < SAME_TREE_M);
      if (twin) {
        twin.genus ??= tree.genus;
        twin.species ??= tree.species ?? null;
        continue;
      }
      merged.push({ ...tree, distanceM });
    }
  });

  merged.sort((a, b) => (a.distanceM ?? 0) - (b.distanceM ?? 0));

  const counts = new Map<string, number>();
  for (const t of merged) if (t.genus) counts.set(t.genus, (counts.get(t.genus) ?? 0) + 1);
  const genusDistribution = [...counts].map(([genus, count]) => ({ genus, count })).sort((a, b) => b.count - a.count);

  return { trees: merged, genusDistribution, failedProviders };
}
