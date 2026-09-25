import type { LatLon } from "../types.ts";

const EARTH_RADIUS_M = 6_371_008.8;
const rad = (deg: number) => (deg * Math.PI) / 180;

/** Great circle distance in meters. Accurate to well below a meter at city scale. */
export function haversineM(a: LatLon, b: LatLon): number {
  const dLat = rad(b.lat - a.lat);
  const dLon = rad(b.lon - a.lon);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLon / 2) ** 2;
  return 2 * EARTH_RADIUS_M * Math.asin(Math.min(1, Math.sqrt(h)));
}

/** Axis aligned bounding box that fully contains the circle around `center`. */
export function bboxAround(center: LatLon, radiusM: number): { minLat: number; minLon: number; maxLat: number; maxLon: number } {
  const dLat = (radiusM / EARTH_RADIUS_M) * (180 / Math.PI);
  const dLon = dLat / Math.cos(rad(center.lat));
  return { minLat: center.lat - dLat, minLon: center.lon - dLon, maxLat: center.lat + dLat, maxLon: center.lon + dLon };
}
