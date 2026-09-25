import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { createMuensterTreeProvider, muensterTreeId, normalizeBaumgruppe, parseWfsTrees } from "../src/index.ts";

const fixture = JSON.parse(readFileSync(new URL("./fixtures/wfs-bbox.geojson", import.meta.url), "utf8"));

describe("normalizeBaumgruppe", () => {
  it.each([
    ["Tilia", "Tilia", null],
    ["Baum Amt62", null, null],
    ["Standort", null, null],
    ["", null, null],
    ["Catalpha", "Catalpa", null],
    ["Malus-Hybride", "Malus", null],
    ["Metasequoia glyptostroboides", "Metasequoia", "Metasequoia glyptostroboides"],
  ])("%s", (raw, genus, species) => expect(normalizeBaumgruppe(raw)).toEqual({ genus, species }));
});

describe("parseWfsTrees", () => {
  it("parses the WFS GeoJSON (lon, lat order)", () => {
    const trees = parseWfsTrees(fixture);
    expect(trees).toHaveLength(85);
    expect(trees[0]).toMatchObject({ source: "de-muenster", genus: "Tilia" });
    expect(trees[0]!.position.lat).toBeCloseTo(51.9622, 3);
    expect(trees[0]!.position.lon).toBeCloseTo(7.6255, 3);
  });

  it("derives stable ids", () => {
    expect(muensterTreeId({ lat: 51.96219241316168, lon: 7.625453743968288 })).toBe(muensterTreeId({ lat: 51.962192413, lon: 7.6254537439 }));
  });
});

describe("createMuensterTreeProvider", () => {
  it("queries the WFS with a lat,lon BBOX", async () => {
    let url = "";
    const provider = createMuensterTreeProvider({
      fetch: (async (u: URL | string) => {
        url = String(u);
        return new Response(JSON.stringify(fixture));
      }) as typeof fetch,
    });
    const trees = await provider.findNearby({ lat: 51.9612, lon: 7.627 }, 50);
    const bbox = new URL(url).searchParams.get("BBOX")!.split(",");
    expect(Number(bbox[0])).toBeCloseTo(51.9607, 3);
    expect(Number(bbox[1])).toBeCloseTo(7.6263, 3);
    expect(bbox[4]).toBe("urn:ogc:def:crs:EPSG::4326");
    expect(trees.length).toBe(85);
  });

  it.runIf(process.env.LIVE === "1")("live: finds trees around Prinzipalmarkt", async () => {
    const trees = await createMuensterTreeProvider().findNearby({ lat: 51.9622, lon: 7.6255 }, 60);
    expect(trees.length).toBeGreaterThan(0);
  });
});
