import { describe, expect, it } from "vitest";
import { haversineM } from "../src/geo/distance.ts";
import { detectMime, precheck } from "../src/precheck.ts";
import { config, JPEG, TREE_POS } from "./helpers.ts";

describe("haversineM", () => {
  it("measures ~111 m per 0.001 deg latitude", () => {
    expect(haversineM({ lat: 51.96, lon: 7.62 }, { lat: 51.961, lon: 7.62 })).toBeCloseTo(111.2, 0);
  });
});

describe("detectMime", () => {
  it("detects jpeg, png and rejects text", () => {
    expect(detectMime(JPEG)).toBe("image/jpeg");
    expect(detectMime(new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0, 0, 0, 0]))).toBe("image/png");
    expect(detectMime(new TextEncoder().encode("hello world!"))).toBeNull();
  });
});

describe("precheck", () => {
  it("accepts a valid image inside the geofence", () => {
    const r = precheck({ image: { data: JPEG }, expected: { position: TREE_POS, genus: "Tilia" }, playerPosition: { lat: 51.96225, lon: 7.62545 } }, config);
    expect(r.image?.mimeType).toBe("image/jpeg");
    expect(r.distanceToExpectedM).toBeCloseTo(6.7, 0);
    expect(r.reasons).toEqual([]);
  });

  it("accepts base64 input", () => {
    expect(precheck({ image: { data: Buffer.from(JPEG).toString("base64") } }, config).image).toBeDefined();
  });

  it("rejects non images", () => {
    const r = precheck({ image: { data: new TextEncoder().encode("not an image") } }, config);
    expect(r.reasons[0]).toMatchObject({ code: "invalid_image", severity: "hard" });
  });

  it("rejects players outside the geofence and flags bad GPS", () => {
    const far = precheck({ image: { data: JPEG }, expected: { position: TREE_POS, genus: null }, playerPosition: { lat: 51.9632, lon: 7.62545 } }, config);
    expect(far.reasons.map((r) => r.code)).toContain("outside_geofence");

    const fuzzy = precheck(
      { image: { data: JPEG }, expected: { position: TREE_POS, genus: null }, playerPosition: { lat: 51.96255, lon: 7.62545, accuracyM: 45 } },
      config,
    );
    // 40 m away, radius 30 m, accuracy 45 m: tolerated (capped at radius) but flagged.
    expect(fuzzy.reasons.map((r) => r.code)).toEqual(["gps_inaccurate"]);
  });

  it("flags stale captures", () => {
    const now = new Date("2026-09-25T12:00:00Z");
    const r = precheck({ image: { data: JPEG }, capturedAt: new Date("2026-09-25T11:00:00Z") }, config, now);
    expect(r.reasons[0]?.code).toBe("stale_capture");
  });
});
