import { describe, expect, it } from "vitest";
import { blendGenus, fuse, type FusionInput } from "../src/decide/fusion.ts";
import type { JevAnswers } from "../src/decide/jev.ts";
import type { GeoContext } from "../src/geo/context.ts";
import type { EnsembleResult } from "../src/vision/ensemble.ts";
import { aggregateGenus, treeProbability } from "../src/vision/ensemble.ts";
import type { VisionObservation } from "../src/vision/schema.ts";
import { aggregateAssessment } from "../src/assess/aggregate.ts";
import { assessment, config, obs, TREE_POS } from "./helpers.ts";

const noGeo: GeoContext = { trees: [], genusDistribution: [], failedProviders: [] };

function vision(observations: VisionObservation[]): EnsembleResult {
  const probs = observations.map(treeProbability);
  return {
    observations,
    calls: [],
    treeProbability: probs.reduce((s, p) => s + p, 0) / probs.length,
    spread: Math.max(...probs) - Math.min(...probs),
    genus: aggregateGenus(observations),
    escalated: false,
  };
}

const input = (o: Partial<FusionInput>): FusionInput => ({
  pre: { image: { base64: "", mimeType: "image/jpeg", bytes: 10 }, geofenceRadiusM: 30, reasons: [] },
  vision: vision([obs(), obs()]),
  geo: noGeo,
  jevFailed: false,
  expected: { position: TREE_POS, genus: "Tilia" },
  ...o,
});

const codes = (r: ReturnType<typeof fuse>) => r.reasons.map((x) => x.code);

describe("fuse", () => {
  it("approves a clear photo of the expected genus", () => {
    const r = fuse(input({}), config);
    expect(r.verdict).toBe("approve");
    expect(r.targetMatch?.value).toBe("expected_tree");
    expect(codes(r)).toEqual(expect.arrayContaining(["genus_confirmed", "tree_confirmed"]));
  });

  it("rejects when no tree is visible", () => {
    const none = obs({ tree_present: "no", tree_present_confidence: 0.95, genus_candidates: [] });
    const r = fuse(input({ vision: vision([none, none]) }), config);
    expect(r.verdict).toBe("reject");
    expect(r.targetMatch?.value).toBe("no_tree");
  });

  it("reviews an uncertain photo", () => {
    const unsure = obs({ tree_present: "uncertain" });
    expect(fuse(input({ vision: vision([unsure, unsure]) }), config).verdict).toBe("review");
  });

  it("rejects screen photos when all models agree, reviews when only one does", () => {
    const screen = obs({ photo_authenticity: "photo_of_screen" });
    expect(fuse(input({ vision: vision([screen, screen]) }), config).verdict).toBe("reject");
    const mixed = fuse(input({ vision: vision([screen, obs()]) }), config);
    expect(mixed.verdict).toBe("review");
    expect(codes(mixed)).toContain("possibly_not_live_photo");
  });

  it("flags a different genus and points at the neighbor tree", () => {
    const oak = obs({ genus_candidates: [{ genus: "Quercus", probability: 0.85, evidence: "lobed leaves" }] });
    const geo: GeoContext = { ...noGeo, trees: [{ source: "de-muenster", position: TREE_POS, genus: "Quercus", distanceM: 8 }] };
    const r = fuse(input({ vision: vision([oak, oak]), geo }), config);
    expect(r.verdict).toBe("review");
    expect(r.targetMatch?.value).toBe("different_tree");
    expect(codes(r)).toEqual(expect.arrayContaining(["genus_mismatch", "possibly_neighbor_tree"]));
  });

  it("stays neutral when the genus vote is not decisive (typical for leafless trees)", () => {
    const winter = obs({ leafless: true, genus_candidates: [{ genus: "Fagus", probability: 0.35, evidence: "bark" }, { genus: "Tilia", probability: 0.3, evidence: "form" }] });
    const r = fuse(input({ vision: vision([winter, winter]) }), config);
    expect(r.verdict).toBe("approve");
    expect(r.targetMatch?.value).toBe("uncertain");
  });

  it("still flags a decisive mismatch on leafless trees (e.g. birch bark)", () => {
    const winterBirch = obs({ leafless: true, genus_candidates: [{ genus: "Betula", probability: 0.95, evidence: "white bark" }] });
    expect(fuse(input({ vision: vision([winterBirch, winterBirch]) }), config).verdict).toBe("review");
  });

  it("suggests a genus for trees without one", () => {
    const r = fuse(input({ expected: { position: TREE_POS, genus: null } }), config);
    expect(r.genusSuggestion).toEqual({ genus: "Tilia", probability: 0.8 });
  });

  it("hard precheck reasons win", () => {
    const r = fuse(input({ pre: { geofenceRadiusM: 30, reasons: [{ code: "outside_geofence", severity: "hard" }] }, vision: null }), config);
    expect(r.verdict).toBe("reject");
  });

  it("reviews when vision is unavailable", () => {
    const r = fuse(input({ vision: null }), config);
    expect(r.verdict).toBe("review");
    expect(codes(r)).toContain("vision_unavailable");
  });

  const jev = (verdict: JevAnswers["verdict"]["choice"], confidence: number, treePresent = 0.95): JevAnswers => ({
    treePresent,
    genus: { choice: "Tilia", confidence: 0.9, probabilities: { Tilia: 0.9, other: 0.1 } },
    verdict: { choice: verdict, confidence, probabilities: {} },
  });

  it("lets a confident Jev downgrade, but never upgrade", () => {
    expect(fuse(input({ jev: jev("reject", 0.95) }), config).verdict).toBe("review");
    expect(fuse(input({ jev: jev("reject", 0.5) }), config).verdict).toBe("approve");
    expect(fuse(input({ jev: jev("review", 0.95) }), config).verdict).toBe("approve");
    const differentTree = { ...jev("approve", 0.5), targetMatch: { choice: "different_tree" as const, confidence: 0.9, probabilities: {} } };
    expect(fuse(input({ jev: differentTree }), config).verdict).toBe("review");
    const none = obs({ tree_present: "no", genus_candidates: [] });
    expect(fuse(input({ vision: vision([none, none]), jev: jev("approve", 0.99, 0.3) }), config).verdict).toBe("reject");
  });

  it("flags unavailable Jev as info only", () => {
    const r = fuse(input({ jevFailed: true }), config);
    expect(r.verdict).toBe("approve");
    expect(codes(r)).toContain("jev_unavailable");
  });
});

describe("fuse: assessment and inventory", () => {
  it("turns a stump at the target into a tree_missing report instead of a reject", () => {
    const stump = obs({ tree_present: "no", tree_present_confidence: 0.95, main_subject: "tree_stump", genus_candidates: [], assessment: assessment({ site_state: "stump" }) });
    const v = vision([stump, stump]);
    const r = fuse(input({ vision: v, assessment: aggregateAssessment(v.observations) }), config);
    expect(r.verdict).toBe("review");
    expect(r.inventory).toBe("tree_missing");
    expect(r.targetMatch?.value).toBe("tree_missing");
    expect(codes(r)).toContain("tree_missing");
  });

  it("still rejects a stump photo without a target", () => {
    const stump = obs({ tree_present: "no", tree_present_confidence: 0.95, genus_candidates: [], assessment: assessment({ site_state: "stump" }) });
    const v = vision([stump, stump]);
    expect(fuse(input({ vision: v, assessment: aggregateAssessment(v.observations), expected: undefined }), config).verdict).toBe("reject");
  });

  it("sends hazards to review", () => {
    const fungi = obs({ assessment: assessment({ fungi_on_trunk: true }) });
    const v = vision([fungi, obs()]);
    const r = fuse(input({ vision: v, assessment: aggregateAssessment(v.observations) }), config);
    expect(r.verdict).toBe("review");
    expect(codes(r)).toContain("safety_concern");
  });

  it("detects new tree candidates in free photo mode", () => {
    const far = { ...noGeo, trees: [{ source: "city", position: TREE_POS, genus: "Acer", distanceM: 30 }] };
    const r = fuse(input({ expected: undefined, geo: far }), config);
    expect(r.inventory).toBe("new_tree_candidate");
    expect(r.verdict).toBe("review");
    const close = { ...noGeo, trees: [{ source: "city", position: TREE_POS, genus: "Tilia", distanceM: 5 }] };
    expect(fuse(input({ expected: undefined, geo: close }), config)).toMatchObject({ inventory: "confirmed", verdict: "approve" });
    const broken = { ...noGeo, failedProviders: ["city"] };
    expect(fuse(input({ expected: undefined, geo: broken }), config).inventory).toBe("unknown");
  });
});

describe("blendGenus", () => {
  it("mixes Jev probabilities in and drops other/unknown", () => {
    const g = blendGenus([{ genus: "Tilia", probability: 0.6 }], { treePresent: 1, genus: { choice: "Tilia", confidence: 1, probabilities: { Tilia: 0.5, Acer: 0.3, other: 0.2 } }, verdict: { choice: "approve", confidence: 1, probabilities: {} } }, 0.5);
    expect(g).toEqual([{ genus: "Tilia", probability: 0.55 }, { genus: "Acer", probability: 0.15 }]);
  });
});
