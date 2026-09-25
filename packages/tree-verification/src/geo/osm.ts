import type { LatLon, NearbyTree, NearbyTreeProvider } from "../types.ts";

export const DEFAULT_OVERPASS_URLS = [
  "https://overpass-api.de/api/interpreter",
  "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
  "https://overpass.kumi.systems/api/interpreter",
];

interface OverpassElement {
  type: "node" | "way";
  id: number;
  lat?: number;
  lon?: number;
  center?: { lat: number; lon: number };
  tags?: Record<string, string>;
}

export interface OsmProviderOptions {
  urls?: string[];
  userAgent?: string;
  fetch?: typeof fetch;
  /** Cache entries (rounded center + radius). OSM data changes slowly. */
  cacheSize?: number;
}

/** Genus from `genus=*`, or the first word of `species=*`. */
export function osmGenus(tags: Record<string, string> = {}): string | null {
  const raw = tags.genus ?? tags.species?.split(/\s+/)[0];
  if (!raw) return null;
  return raw.charAt(0).toUpperCase() + raw.slice(1).toLowerCase();
}

/** OpenStreetMap trees (`natural=tree`) via Overpass, with mirror fallback. ODbL: attribute OSM contributors. */
export function createOsmTreeProvider(options: OsmProviderOptions = {}): NearbyTreeProvider {
  const urls = options.urls?.length ? options.urls : DEFAULT_OVERPASS_URLS;
  const doFetch = options.fetch ?? fetch;
  const userAgent = options.userAgent ?? "OpenQuest/0.1 (+https://openquest.fun)";
  const cacheSize = options.cacheSize ?? 200;
  const cache = new Map<string, NearbyTree[]>();

  return {
    id: "osm",
    async findNearby(center: LatLon, radiusM: number, signal?: AbortSignal) {
      const key = `${center.lat.toFixed(4)},${center.lon.toFixed(4)},${Math.ceil(radiusM)}`;
      const hit = cache.get(key);
      if (hit) return hit;

      // Query a bit wider than asked so the cache key rounding (~11 m) never hides a tree.
      const r = Math.ceil(radiusM + 15);
      const query = `[out:json][timeout:10];node(around:${r},${center.lat},${center.lon})[natural=tree];out tags;`;

      let lastError: unknown;
      for (const url of urls) {
        try {
          const res = await doFetch(url, {
            method: "POST",
            body: new URLSearchParams({ data: query }),
            headers: { "User-Agent": userAgent },
            signal,
          });
          if (!res.ok) throw new Error(`${url} HTTP ${res.status}`);
          const json = (await res.json()) as { elements?: OverpassElement[] };
          const trees = (json.elements ?? []).flatMap((el): NearbyTree[] => {
            const lat = el.lat ?? el.center?.lat;
            const lon = el.lon ?? el.center?.lon;
            if (lat === undefined || lon === undefined) return [];
            return [
              {
                source: "osm",
                externalId: `${el.type}/${el.id}`,
                position: { lat, lon },
                genus: osmGenus(el.tags),
                species: el.tags?.species ?? null,
              },
            ];
          });
          cache.set(key, trees);
          if (cache.size > cacheSize) cache.delete(cache.keys().next().value!);
          return trees;
        } catch (err) {
          if (signal?.aborted) throw err;
          lastError = err;
        }
      }
      throw lastError instanceof Error ? lastError : new Error("all Overpass endpoints failed");
    },
  };
}
