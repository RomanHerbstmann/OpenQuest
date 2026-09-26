import { describe, expect, it } from "vitest";
import { buildJevQuestions } from "../src/decide/jev.ts";
import type { JevClient } from "../src/decide/jev.ts";
import { buildGeoContext } from "../src/geo/context.ts";
import { osmGenus } from "../src/geo/osm.ts";
import { verifyTreePhoto } from "../src/index.ts";
import type { NearbyTree, NearbyTreeProvider } from "../src/types.ts";
import type { OpenRouterClient } from "../src/vision/openrouter.ts";
import { config, JPEG, obs, TREE_POS } from "./helpers.ts";

const provider = (id: string, trees: NearbyTree[]): NearbyTreeProvider => ({
  id,
  findNearby: async () => trees,
});

describe("buildGeoContext", () => {
  it("merges sources, dedupes twins and fills missing genus", async () => {
    const city = provider("city", [
      { source: "city", position: TREE_POS, genus: null },
      { source: "city", position: { lat: 51.9623, lon: 7.62545 }, genus: "Acer" },
    ]);
    const osm = provider("osm", [{ source: "osm", position: { lat: 51.962195, lon: 7.62545 }, genus: "Tilia" }]);
    const failing: NearbyTreeProvider = { id: "broken", findNearby: async () => Promise.reject(new Error("down")) };
    const ctx = await buildGeoContext([city, osm, failing], TREE_POS, 40, 1000);
    expect(ctx.trees).toHaveLength(2);
    expect(ctx.trees[0]).toMatchObject({ source: "city", genus: "Tilia" });
    expect(ctx.failedProviders).toEqual(["broken"]);
    expect(ctx.genusDistribution).toEqual([{ genus: "Tilia", count: 1 }, { genus: "Acer", count: 1 }]);
  });
});

describe("osmGenus", () => {
  it("reads genus or species", () => {
    expect(osmGenus({ genus: "Tilia" })).toBe("Tilia");
    expect(osmGenus({ species: "Acer platanoides" })).toBe("Acer");
    expect(osmGenus({ leaf_type: "broadleaved" })).toBeNull();
  });
});

describe("buildJevQuestions", () => {
  it("builds genus labels from all evidence and asks target_match only with a target", () => {
    const base = {
      observations: [obs()],
      ensembleGenus: [{ genus: "Tilia", probability: 0.8 }],
      geofenceRadiusM: 30,
      geo: { trees: [], genusDistribution: [{ genus: "Acer", count: 2 }], failedProviders: [] },
    };
    const q = buildJevQuestions({ ...base, expected: { position: TREE_POS, genus: "Quercus" } });
    expect(Object.keys(q)).toEqual(["tree_present", "genus", "verdict", "vitality", "phenology", "safety_concern", "target_match"]);
    expect(Object.keys((q.genus as { criteria: object }).criteria)).toEqual(["Tilia", "Quercus", "Acer", "other", "unknown"]);
    expect(Object.keys(buildJevQuestions(base))).not.toContain("target_match");
    const noTree = { ...base, observations: [obs({ assessment: undefined })] };
    expect(Object.keys(buildJevQuestions(noTree))).toEqual(["tree_present", "genus", "verdict"]);
  });
});

describe("verifyTreePhoto", () => {
  const openRouter: OpenRouterClient = { chat: async (req) => ({ content: JSON.stringify(obs({ model: req.model })), costUsd: 0.002, latencyMs: 3 }) };
  const jev: JevClient = {
    systemOne: async () => ({
      answers: {
        tree_present: { type: "noul", noul: 0.97 },
        genus: { type: "choice", choice: "Tilia", confidence: 0.9, probabilities: { Tilia: 0.9, other: 0.1 } },
        verdict: { type: "choice", choice: "approve", confidence: 0.9, probabilities: { approve: 0.9 } },
        target_match: { type: "choice", choice: "expected_tree", confidence: 0.9, probabilities: { expected_tree: 0.9 } },
      },
      usage: { input_tokens: 500, output_tokens: 20, cost: 0.00002 },
    }),
  };

  it("runs the full pipeline and sums costs", async () => {
    const r = await verifyTreePhoto(
      { image: { data: JPEG }, expected: { position: TREE_POS, genus: "Tilia" }, playerPosition: { lat: 51.96222, lon: 7.62545 } },
      { config, openRouter, jev, providers: [provider("city", [{ source: "city", position: TREE_POS, genus: "Tilia" }])] },
    );
    expect(r.verdict).toBe("approve");
    expect(r.genus.value).toBe("Tilia");
    expect(r.models.map((m) => m.stage)).toEqual(["vision", "vision", "jev"]);
    expect(r.costUsd).toBeCloseTo(0.00402);
    expect(r.signals.nearbyTrees).toHaveLength(1);
    expect(r.inventory).toBe("confirmed");
    expect(r.assessment?.vitality.value).toBe("healthy");
    expect(r.proposedChanges.map((c) => c.key)).toEqual(["vitality", "condition", "tree_pit", "phenology", "age_class"]);
  });

  it("skips paid calls when the player is outside the geofence", async () => {
    let called = false;
    const spy: OpenRouterClient = { chat: async () => ((called = true), { content: "{}", costUsd: 0, latencyMs: 0 }) };
    const r = await verifyTreePhoto(
      { image: { data: JPEG }, expected: { position: TREE_POS, genus: "Tilia" }, playerPosition: { lat: 51.97, lon: 7.62545 } },
      { config, openRouter: spy, jev },
    );
    expect(r.verdict).toBe("reject");
    expect(called).toBe(false);
  });
});
