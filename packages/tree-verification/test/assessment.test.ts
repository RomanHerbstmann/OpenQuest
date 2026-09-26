import { describe, expect, it } from "vitest";
import { aggregateAssessment } from "../src/assess/aggregate.ts";
import { buildProposals, type ProposalInput } from "../src/assess/proposals.ts";
import type { JevAnswers } from "../src/decide/jev.ts";
import { assessment, obs } from "./helpers.ts";

const withA = (...a: Parameters<typeof assessment>[0][]) => a.map((x) => obs({ assessment: assessment(x) }));

describe("aggregateAssessment", () => {
  it("votes categorical fields and ignores not_assessable", () => {
    const r = aggregateAssessment(withA({ phenology: "flowering" }, { phenology: "flowering" }, { phenology: "not_assessable" }))!;
    expect(r.phenology).toEqual({ value: "flowering", confidence: 2 / 3 });
  });

  it("breaks vitality ties conservatively, or by Jev", () => {
    const tie = withA({ vitality: "healthy" }, { vitality: "clearly_damaged" });
    expect(aggregateAssessment(tie)!.vitality.value).toBe("clearly_damaged");
    const jev = { vitality: { choice: "healthy", confidence: 0.9, probabilities: { healthy: 0.9 } } } as unknown as JevAnswers;
    expect(aggregateAssessment(tie, jev)!.vitality.value).toBe("healthy");
  });

  it("raises safety flags when a single model sees a hazard", () => {
    const r = aggregateAssessment(withA({ fungi_on_trunk: true, damage: ["cavity"] }, {}))!;
    expect(r.safetyFlags.map((f) => f.value).sort()).toEqual(["cavity", "fungi_on_trunk"]);
    expect(r.safetyFlags[0]!.confidence).toBe(0.5);
  });

  it("flags oak processionary nests and dead trees", () => {
    const r = aggregateAssessment(withA({ pests: ["oak_processionary_nests"], vitality: "severely_damaged_or_dead" }, { vitality: "severely_damaged_or_dead" }))!;
    expect(r.safetyFlags.map((f) => f.value)).toEqual(["dead_or_dying", "oak_processionary_nests"]);
  });

  it("collects tree pit features", () => {
    const r = aggregateAssessment(
      withA(
        { tree_pit: { visible: true, surface: "sealed", issues: ["litter"], watering_bag: true, stakes: true, protection_guard: false } },
        { tree_pit: { visible: true, surface: "sealed", issues: ["none"], watering_bag: true, stakes: false, protection_guard: false } },
      ),
    )!;
    expect(r.treePit.surface.value).toBe("sealed");
    expect(r.treePit.wateringBag?.confidence).toBe(1);
    expect(r.treePit.stakes?.confidence).toBe(0.5);
    expect(r.treePit.issues).toEqual([{ value: "litter", confidence: 0.5 }]);
  });

  it("returns null without assessments", () => {
    expect(aggregateAssessment([obs({ assessment: undefined })])).toBeNull();
  });
});

describe("buildProposals", () => {
  const base = (a: ReturnType<typeof aggregateAssessment>, o: Partial<ProposalInput> = {}): ProposalInput => ({
    verdict: "approve",
    inventory: "confirmed",
    assessment: a,
    genus: { value: "Tilia", probability: 0.9 },
    capturedAt: new Date("2026-09-25T10:00:00Z"),
    minConfidence: 0.5,
    ...o,
  });

  it("proposes condition, tree pit, phenology and age class", () => {
    const a = aggregateAssessment(withA({ vitality: "slightly_damaged", phenology: "autumn_coloring" }, { vitality: "slightly_damaged", phenology: "autumn_coloring" }));
    const p = buildProposals(base(a));
    const byKey = Object.fromEntries(p.map((c) => [c.key, c]));
    expect(byKey.condition).toMatchObject({ kind: "attribute", value: "damaged", requiresReview: false });
    expect(byKey.vitality?.value).toBe("slightly_damaged");
    expect(byKey.phenology).toMatchObject({ kind: "observation", value: { stage: "autumn_coloring", observedAt: "2026-09-25T10:00:00.000Z" } });
    expect(byKey.tree_pit?.value).toEqual({ surface: "open_soil", watering_bag: false, stakes: false, protection_guard: false });
    expect(byKey.age_class?.value).toBe("mature");
  });

  it("always proposes hazards for review, even at low confidence", () => {
    const a = aggregateAssessment(withA({ fungi_on_trunk: true }, {}, {}));
    const hazard = buildProposals(base(a)).find((c) => c.key === "safety_concern");
    expect(hazard).toMatchObject({ requiresReview: true, value: { flag: "fungi_on_trunk" } });
    expect(hazard!.confidence).toBeCloseTo(1 / 3);
  });

  it("proposes gone for missing trees and nothing else", () => {
    const a = aggregateAssessment(withA({ site_state: "empty_tree_pit" }, { site_state: "empty_tree_pit" }));
    expect(buildProposals(base(a, { inventory: "tree_missing", verdict: "review" }))).toEqual([
      expect.objectContaining({ key: "condition", value: "gone", requiresReview: true }),
    ]);
  });

  it("proposes a new asset for trees missing in the inventory", () => {
    const a = aggregateAssessment(withA({}, {}));
    const p = buildProposals(base(a, { inventory: "new_tree_candidate", verdict: "review", playerPosition: { lat: 51.96, lon: 7.62, accuracyM: 6 } }));
    expect(p[0]).toMatchObject({ kind: "new_asset", value: { genus: "Tilia", positionAccuracyM: 16 }, requiresReview: true });
    expect(p.every((c) => c.requiresReview)).toBe(true);
  });

  it("drops findings below the confidence threshold", () => {
    const a = aggregateAssessment(withA({ phenology: "flowering" }, { phenology: "fruiting" }, { phenology: "leaf_fall" }));
    expect(buildProposals(base(a)).find((c) => c.key === "phenology")).toBeUndefined();
  });
});
