import { createHash } from "node:crypto";
import { bboxAround, type LatLon, type NearbyTree, type NearbyTreeProvider } from "@openquest/tree-verification";

/** MapServer WFS behind the "Digitales Baumkataster Münster" dataset (dl-de/by-2.0, Stadt Münster). */
export const MUENSTER_TREES_WFS = "https://geo.stadt-muenster.de/mapserv/odgruen_serv";

export const MUENSTER_ATTRIBUTION =
  "Datenquelle: Stadt Münster, Digitales Baumkataster, dl-de/by-2-0 (https://www.govdata.de/dl-de/by-2-0)";

/** Values in `baumgruppe` that are not a genus. */
const PLACEHOLDERS = new Set(["baum amt62", "baumgruppe", "standort", "leerer", "leerer standort", "unbekannt", ""]);

/** Known typos in the source data. */
const TYPOS: Record<string, string> = { Catalpha: "Catalpa", Cladrastris: "Cladrastis" };

/** "Tilia" -> "Tilia", "Malus-Hybride" -> "Malus", "Metasequoia glyptostroboides" -> "Metasequoia", "Baum Amt62" -> null. */
export function normalizeBaumgruppe(raw: string | null | undefined): { genus: string | null; species: string | null } {
  const value = (raw ?? "").trim();
  if (PLACEHOLDERS.has(value.toLowerCase())) return { genus: null, species: null };
  const [first, ...rest] = value.split(/[\s-]+/);
  const genus = TYPOS[first!] ?? first!;
  const species = rest.length && /^[a-z]/.test(rest[0]!) ? `${genus} ${rest.join(" ")}` : null;
  return { genus, species };
}

/**
 * Stable id for a tree without source id: position rounded to 7 decimals (~1 cm).
 * Must match the importer's `external_id` derivation (see docs/data-model/erd.md).
 */
export function muensterTreeId(pos: LatLon): string {
  return createHash("sha1").update(`${pos.lon.toFixed(7)},${pos.lat.toFixed(7)}`).digest("hex").slice(0, 16);
}

interface WfsFeature {
  geometry?: { type: string; coordinates?: [number, number] };
  properties?: { str_schl?: string; baumgruppe?: string };
}

export function parseWfsTrees(geojson: { features?: WfsFeature[] }): NearbyTree[] {
  return (geojson.features ?? []).flatMap((f): NearbyTree[] => {
    const c = f.geometry?.type === "Point" ? f.geometry.coordinates : undefined;
    if (!c) return [];
    const position = { lon: c[0], lat: c[1] }; // GeoJSON output is CRS84: lon, lat
    const { genus, species } = normalizeBaumgruppe(f.properties?.baumgruppe);
    return [{ source: "de-muenster", externalId: muensterTreeId(position), position, genus, species }];
  });
}

export function createMuensterTreeProvider(opts: { fetch?: typeof fetch; baseUrl?: string } = {}): NearbyTreeProvider {
  const doFetch = opts.fetch ?? fetch;
  const baseUrl = opts.baseUrl ?? MUENSTER_TREES_WFS;
  return {
    id: "de-muenster",
    async findNearby(center, radiusM, signal) {
      const b = bboxAround(center, radiusM);
      const url = new URL(baseUrl);
      url.search = new URLSearchParams({
        SERVICE: "WFS",
        VERSION: "2.0.0",
        REQUEST: "GetFeature",
        TYPENAMES: "ms:Baeume",
        OUTPUTFORMAT: "geojson",
        // EPSG:4326 URN means lat,lon axis order in WFS 2.0.
        BBOX: `${b.minLat},${b.minLon},${b.maxLat},${b.maxLon},urn:ogc:def:crs:EPSG::4326`,
      }).toString();
      const res = await doFetch(url, { signal });
      if (!res.ok) throw new Error(`Münster WFS HTTP ${res.status}`);
      return parseWfsTrees((await res.json()) as { features?: WfsFeature[] });
    },
  };
}
