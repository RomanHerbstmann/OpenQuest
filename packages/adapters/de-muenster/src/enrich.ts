/** Münster reference data used to enrich trees: street names and districts. */

export const MUENSTER_STREETS_WFS = "https://www.stadt-muenster.de/ows/mapserv706/odstrasseserv";
export const MUENSTER_DISTRICTS_GEOJSON = "https://opendata.stadt-muenster.de/sites/default/files/stadtbezirke-muenster.geojson";
export const MUENSTER_QUARTERS_GEOJSON = "https://opendata.stadt-muenster.de/sites/default/files/stadtteile-statistische-bezirke-muenster.geojson";

/** STR_SCHL (5 digits) -> street name, from the city's street directory WFS. */
export async function fetchStreetNames(doFetch: typeof fetch = fetch): Promise<Map<string, string>> {
  const url = `${MUENSTER_STREETS_WFS}?SERVICE=WFS&VERSION=1.1.0&REQUEST=GetFeature&TYPENAME=ms:Strassen&OUTPUTFORMAT=csv`;
  const res = await doFetch(url);
  if (!res.ok) throw new Error(`street WFS HTTP ${res.status}`);
  return parseStreetCsv(await res.text());
}

export function parseStreetCsv(csv: string): Map<string, string> {
  const lines = csv.replace(/^﻿/, "").split(/\r?\n/).filter(Boolean);
  const header = lines.shift()!.split(",");
  const iKey = header.indexOf("STR_SCHL");
  const iName = header.indexOf("NAME");
  const map = new Map<string, string>();
  for (const line of lines) {
    const cols = line.match(/("([^"]|"")*"|[^,]*)(,|$)/g)!.map((c) => c.replace(/,$/, "").replace(/^"|"$/g, "").replace(/""/g, '"'));
    const key = cols[iKey]?.padStart(5, "0");
    if (key && cols[iName]) map.set(key, cols[iName]!);
  }
  return map;
}

export interface Area {
  name: string;
  /** Outer rings as [lon, lat][] (holes ignored, not present in these data sets). */
  rings: [number, number][][];
}

type Geometry = { type: "Polygon"; coordinates: [number, number][][] } | { type: "MultiPolygon"; coordinates: [number, number][][][] };

export async function fetchAreas(url: string, nameOf: (props: Record<string, unknown>) => string, doFetch: typeof fetch = fetch): Promise<Area[]> {
  const res = await doFetch(url);
  if (!res.ok) throw new Error(`${url} HTTP ${res.status}`);
  const gj = (await res.json()) as { features: { properties: Record<string, unknown>; geometry: Geometry }[] };
  return gj.features.map((f) => ({
    name: nameOf(f.properties),
    rings: f.geometry.type === "Polygon" ? [f.geometry.coordinates[0]!] : f.geometry.coordinates.map((p) => p[0]!),
  }));
}

/** 6 Stadtbezirke, e.g. "Münster-Südost". */
export const fetchDistricts = (doFetch?: typeof fetch) => fetchAreas(MUENSTER_DISTRICTS_GEOJSON, (p) => String(p.NAME_STADT ?? p.STADTBEZIR), doFetch);

/** 45 Stadtteile (statistical districts), e.g. "Kreuzviertel". */
export const fetchQuarters = (doFetch?: typeof fetch) => fetchAreas(MUENSTER_QUARTERS_GEOJSON, (p) => String(p.NAME_STATI ?? p.STATIST_BE), doFetch);

/** Ray casting point in polygon. */
export function inRing(lon: number, lat: number, ring: [number, number][]): boolean {
  let inside = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const [xi, yi] = ring[i]!;
    const [xj, yj] = ring[j]!;
    if (yi > lat !== yj > lat && lon < ((xj - xi) * (lat - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

export function areaOf(lon: number, lat: number, areas: Area[]): string | null {
  return areas.find((a) => a.rings.some((r) => inRing(lon, lat, r)))?.name ?? null;
}
