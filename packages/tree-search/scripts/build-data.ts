/**
 * Builds packages/tree-search/data/trees.json: all Münster inventory trees, enriched with street name,
 * district, quarter and a height from the NRW surface model (nDOM50).
 *
 *   pnpm --filter @openquest/tree-search build-data      (~20 min on first run, resumable)
 */
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  MUENSTER_ATTRIBUTION,
  MUENSTER_TREES_WFS,
  areaOf,
  fetchDistricts,
  fetchQuarters,
  fetchStreetNames,
  muensterTreeId,
  normalizeBaumgruppe,
} from "@openquest/adapter-de-muenster";
import { NDOM_ATTRIBUTION, sampleTreeHeights } from "@openquest/adapter-de-nrw";
import { Agent, setGlobalDispatcher } from "undici";

// opendata.stadt-muenster.de sometimes needs > 20 s to accept a connection; Node's default is 10 s.
setGlobalDispatcher(new Agent({ connect: { timeout: 90_000 } }));

const here = dirname(fileURLToPath(import.meta.url));
const dataDir = join(here, "..", "data");
await mkdir(join(dataDir, "cache"), { recursive: true });

/** The city servers are slow at times; retry transient failures. */
const retryFetch: typeof fetch = async (input, init) => {
  for (let attempt = 1; ; attempt++) {
    try {
      const res = await fetch(input, { ...init, signal: AbortSignal.timeout(120_000) });
      if (res.ok || attempt >= 4) return res;
    } catch (err) {
      if (attempt >= 4) throw err;
    }
    console.log(`  retry ${attempt} for ${String(input).slice(0, 80)}`);
    await new Promise((r) => setTimeout(r, 3000 * attempt));
  }
};

console.log("fetching trees, streets, districts ...");
const res = await retryFetch(`${MUENSTER_TREES_WFS}?SERVICE=WFS&VERSION=1.1.0&REQUEST=GetFeature&TYPENAME=Baeume&OUTPUTFORMAT=geojson`);
const gj = (await res.json()) as { features: { geometry: { coordinates: [number, number] }; properties: { baumgruppe?: string; str_schl?: string } }[] };
const [streets, districts, quarters] = await Promise.all([fetchStreetNames(retryFetch), fetchDistricts(retryFetch), fetchQuarters(retryFetch)]);
console.log(`${gj.features.length} trees, ${streets.size} streets, ${districts.length} districts, ${quarters.length} quarters`);

const index = <T>(list: T[]) => {
  const m = new Map<T, number>();
  return { list, id: (v: T | null) => (v === null ? -1 : (m.get(v) ?? (m.set(v, list.push(v) - 1), list.length - 1))) };
};
const genera = index<string>([]);
const streetNames = index<string>([]);
const districtNames = index<string>([]);
const quarterNames = index<string>([]);

const points = gj.features.map((f) => ({ lon: f.geometry.coordinates[0], lat: f.geometry.coordinates[1] }));

console.log("sampling heights from nDOM50 (cached in data/cache) ...");
let last = 0;
const heights = await sampleTreeHeights(points, {
  cacheFile: join(dataDir, "cache", "ndom-heights.json"),
  concurrency: 6,
  onProgress: (done, total) => {
    const pct = Math.floor((100 * done) / total);
    if (pct !== last) console.log(`  ${pct}% (${done}/${total} cells)`), (last = pct);
  },
});

const t = { id: [] as string[], lon: [] as number[], lat: [] as number[], genus: [] as number[], raw: [] as number[], height: [] as number[], street: [] as number[], district: [] as number[], quarter: [] as number[] };
const rawValues = index<string>([]);
gj.features.forEach((f, i) => {
  const p = points[i]!;
  const key = (f.properties.str_schl ?? "").trim().padStart(5, "0");
  t.id.push(muensterTreeId(p));
  t.lon.push(Math.round(p.lon * 1e6) / 1e6);
  t.lat.push(Math.round(p.lat * 1e6) / 1e6);
  t.genus.push(genera.id(normalizeBaumgruppe(f.properties.baumgruppe).genus));
  t.raw.push(rawValues.id(f.properties.baumgruppe?.trim() || ""));
  const h = heights[i]!.heightM;
  t.height.push(h === null ? -1 : Math.round(h * 10));
  t.street.push(streetNames.id(streets.get(key) ?? null));
  t.district.push(districtNames.id(areaOf(p.lon, p.lat, districts)));
  t.quarter.push(quarterNames.id(areaOf(p.lon, p.lat, quarters)));
});

const out = {
  generatedAt: new Date().toISOString(),
  attribution: [MUENSTER_ATTRIBUTION, NDOM_ATTRIBUTION],
  genera: genera.list,
  rawGenus: rawValues.list,
  streets: streetNames.list,
  districts: districtNames.list,
  quarters: quarterNames.list,
  /** Columnar: height in decimeters (-1 = none), -1 index = unknown. */
  trees: t,
};
await writeFile(join(dataDir, "trees.json"), JSON.stringify(out));
const withHeight = t.height.filter((h) => h >= 0).length;
console.log(`wrote data/trees.json: ${t.id.length} trees, ${withHeight} with height, ${genera.list.length} genera`);
