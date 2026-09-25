import { existsSync } from "node:fs";
import { readFile, writeFile } from "node:fs/promises";
import { fromArrayBuffer } from "geotiff";
import { toUtm32 } from "./utm.ts";

/**
 * Tree heights from the normalized digital surface model of NRW (nDOM50):
 * object height above ground, 0.5 m grid, summer image flights plus laser DGM.
 * License: Datenlizenz Deutschland Zero 2.0 (no conditions). Source: Geobasis NRW.
 */
export const NDOM_WCS = "https://www.wcs.nrw.de/geobasis/wcs_nw_ndom";
export const NDOM_ATTRIBUTION = "Höhen: Geobasis NRW, nDOM50, dl-de/zero-2-0";

export interface HeightSample {
  /** 95th percentile of nDOM heights within `radiusM` around the tree point (crown top, robust to noise). */
  heightM: number | null;
}

export interface SampleOptions {
  /** Window around the tree point. Crowns overhang the trunk point, 2.5 m catches the top without neighbors. */
  radiusM?: number;
  /** Trees are grouped into square cells of this size, one WCS request per cell. */
  cellM?: number;
  concurrency?: number;
  /** JSON file with finished cells, so an interrupted run resumes. */
  cacheFile?: string;
  fetch?: typeof fetch;
  onProgress?: (done: number, total: number) => void;
}

type CellCache = Record<string, (number | null)[]>;

/** Percentile of finite values; `null` for an empty window. */
export function percentile(values: number[], p: number): number | null {
  const v = values.filter((x) => Number.isFinite(x) && x > -5 && x < 100).sort((a, b) => a - b);
  if (!v.length) return null;
  return v[Math.min(v.length - 1, Math.floor(p * v.length))]!;
}

async function fetchCell(x0: number, y0: number, x1: number, y1: number, doFetch: typeof fetch) {
  const url =
    `${NDOM_WCS}?SERVICE=WCS&VERSION=2.0.1&REQUEST=GetCoverage&COVERAGEID=nw_ndom&FORMAT=image/tiff` +
    `&SUBSET=x(${x0},${x1})&SUBSET=y(${y0},${y1})`;
  for (let attempt = 0; ; attempt++) {
    try {
      const res = await doFetch(url);
      if (!res.ok) throw new Error(`nDOM WCS HTTP ${res.status}`);
      const tiff = await fromArrayBuffer(await res.arrayBuffer());
      const image = await tiff.getImage();
      const [originX, originY] = image.getOrigin() as [number, number];
      const [resX, resY] = image.getResolution() as [number, number];
      const raster = (await image.readRasters({ interleave: true })) as unknown as Float32Array;
      return { originX, originY, resX, resY, width: image.getWidth(), height: image.getHeight(), raster };
    } catch (err) {
      if (attempt >= 3) throw err;
      await new Promise((r) => setTimeout(r, 1000 * 2 ** attempt));
    }
  }
}

/**
 * Samples heights for many points with one WCS request per occupied cell.
 * Returns heights in input order.
 */
export async function sampleTreeHeights(points: { lat: number; lon: number }[], opts: SampleOptions = {}): Promise<HeightSample[]> {
  const radius = opts.radiusM ?? 2.5;
  const cell = opts.cellM ?? 50;
  const concurrency = opts.concurrency ?? 6;
  const doFetch = opts.fetch ?? fetch;

  const utm = points.map((p) => toUtm32(p.lat, p.lon));
  const cells = new Map<string, number[]>();
  utm.forEach((u, i) => {
    const key = `${Math.floor(u.x / cell)}_${Math.floor(u.y / cell)}`;
    (cells.get(key) ?? cells.set(key, []).get(key)!).push(i);
  });

  const cache: CellCache = opts.cacheFile && existsSync(opts.cacheFile) ? JSON.parse(await readFile(opts.cacheFile, "utf8")) : {};
  const out: HeightSample[] = points.map(() => ({ heightM: null }));
  const keys = [...cells.keys()];
  let done = 0;
  let sinceSave = 0;
  const save = async () => opts.cacheFile && (await writeFile(opts.cacheFile, JSON.stringify(cache)));

  const work = async (key: string) => {
    const idx = cells.get(key)!;
    let heights = cache[key];
    if (!heights || heights.length !== idx.length) {
      const [cx, cy] = key.split("_").map(Number) as [number, number];
      const m = Math.ceil(radius) + 1;
      const g = await fetchCell(cx * cell - m, cy * cell - m, (cx + 1) * cell + m, (cy + 1) * cell + m, doFetch);
      heights = idx.map((i) => {
        const { x, y } = utm[i]!;
        const window: number[] = [];
        const r2 = radius * radius;
        const colMin = Math.max(0, Math.floor((x - radius - g.originX) / g.resX));
        const colMax = Math.min(g.width - 1, Math.ceil((x + radius - g.originX) / g.resX));
        // resY is negative for north-up rasters.
        const rowMin = Math.max(0, Math.floor((y + radius - g.originY) / g.resY));
        const rowMax = Math.min(g.height - 1, Math.ceil((y - radius - g.originY) / g.resY));
        for (let row = rowMin; row <= rowMax; row++) {
          for (let col = colMin; col <= colMax; col++) {
            const px = g.originX + (col + 0.5) * g.resX;
            const py = g.originY + (row + 0.5) * g.resY;
            if ((px - x) ** 2 + (py - y) ** 2 <= r2) window.push(g.raster[row * g.width + col]!);
          }
        }
        const h = percentile(window, 0.95);
        return h === null ? null : Math.round(h * 10) / 10;
      });
      cache[key] = heights;
      if (++sinceSave >= 100) {
        sinceSave = 0;
        await save();
      }
    }
    idx.forEach((i, k) => (out[i] = { heightM: heights![k] ?? null }));
    opts.onProgress?.(++done, keys.length);
  };

  let next = 0;
  await Promise.all(
    Array.from({ length: concurrency }, async () => {
      while (next < keys.length) await work(keys[next++]!);
    }),
  );
  await save();
  return out;
}
