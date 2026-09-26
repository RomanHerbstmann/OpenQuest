import type { GenusEstimate, LatLon, Verdict } from "../types.ts";
import type { TreeAssessment } from "./aggregate.ts";

/**
 * What a submission proposes to change or record. Maps onto the ERD:
 * - `attribute`   -> ATTRIBUTE_CHANGE on the asset (state of the tree, replaces the old value)
 * - `observation` -> time stamped fact (phenology, drought, litter); kept as history, never overwrites
 * - `new_asset`   -> candidate for a tree missing in the city inventory
 * Nothing is applied automatically: `requiresReview` marks proposals a moderator must confirm
 * before they may be accepted or exported to the city.
 */
export interface ProposedChange {
  kind: "attribute" | "observation" | "new_asset";
  key: string;
  value: unknown;
  confidence: number;
  requiresReview: boolean;
  evidence?: string;
}

export type InventoryStatus = "confirmed" | "tree_missing" | "new_tree_candidate" | "unknown";

export interface ProposalInput {
  verdict: Verdict;
  inventory: InventoryStatus;
  assessment: TreeAssessment | null;
  genusSuggestion?: GenusEstimate;
  genus: { value: string | null; probability: number };
  playerPosition?: LatLon & { accuracyM?: number };
  capturedAt: Date;
  /** Findings below this confidence are not proposed. */
  minConfidence: number;
}

const CONDITION: Record<string, "good" | "damaged" | "dead"> = {
  healthy: "good",
  slightly_damaged: "damaged",
  clearly_damaged: "damaged",
  severely_damaged_or_dead: "dead",
};

export function buildProposals(input: ProposalInput): ProposedChange[] {
  const out: ProposedChange[] = [];
  const reviewAll = input.verdict !== "approve";
  const add = (c: Omit<ProposedChange, "requiresReview"> & { requiresReview?: boolean }) => {
    if (c.confidence < input.minConfidence) return;
    out.push({ ...c, requiresReview: reviewAll || (c.requiresReview ?? false) });
  };
  const observedAt = input.capturedAt.toISOString();

  if (input.inventory === "tree_missing") {
    const site = input.assessment?.siteState;
    add({ kind: "attribute", key: "condition", value: "gone", confidence: site?.confidence ?? 0.5, requiresReview: true, evidence: `photo shows ${site?.value ?? "no tree"} at the recorded position` });
    return out;
  }

  if (input.inventory === "new_tree_candidate" && input.playerPosition) {
    add({
      kind: "new_asset",
      key: "tree",
      value: { genus: input.genus.value, approximatePosition: input.playerPosition, positionAccuracyM: (input.playerPosition.accuracyM ?? 10) + 10 },
      confidence: 0.6,
      requiresReview: true,
      evidence: "tree in photo, no inventory tree near the player",
    });
  }

  if (input.genusSuggestion) {
    add({ kind: "attribute", key: "genus", value: input.genusSuggestion.genus, confidence: input.genusSuggestion.probability, requiresReview: true, evidence: "genus missing in city data" });
  }

  const a = input.assessment;
  if (!a || a.siteState.value !== "tree_present") return out;
  const hasSafety = a.safetyFlags.length > 0;

  if (a.vitality.value !== "not_assessable") {
    add({ kind: "attribute", key: "vitality", value: a.vitality.value, confidence: a.vitality.confidence, requiresReview: a.vitality.value === "severely_damaged_or_dead" });
    add({ kind: "attribute", key: "condition", value: CONDITION[a.vitality.value], confidence: a.vitality.confidence, requiresReview: a.vitality.value === "severely_damaged_or_dead" });
  }

  const damage = a.damage.filter((d) => d.confidence >= input.minConfidence).map((d) => d.value);
  if (damage.length) add({ kind: "attribute", key: "damage", value: damage, confidence: Math.min(...a.damage.filter((d) => damage.includes(d.value)).map((d) => d.confidence)), requiresReview: hasSafety });

  const pests = a.pests.filter((p) => p.confidence >= input.minConfidence).map((p) => p.value);
  if (pests.length) add({ kind: "attribute", key: "pests", value: pests, confidence: Math.min(...a.pests.filter((p) => pests.includes(p.value)).map((p) => p.confidence)), requiresReview: true });

  // Hazards are always proposed (even at low confidence) and always need a human.
  for (const f of a.safetyFlags) {
    out.push({ kind: "observation", key: "safety_concern", value: { flag: f.value, observedAt }, confidence: f.confidence, requiresReview: true, evidence: a.notes[0] });
  }

  if (a.treePit.visible && a.treePit.surface.value !== "not_visible") {
    add({
      kind: "attribute",
      key: "tree_pit",
      value: {
        surface: a.treePit.surface.value,
        watering_bag: (a.treePit.wateringBag?.confidence ?? 0) >= 0.5,
        stakes: (a.treePit.stakes?.confidence ?? 0) >= 0.5,
        protection_guard: (a.treePit.protectionGuard?.confidence ?? 0) >= 0.5,
      },
      confidence: a.treePit.surface.confidence,
    });
    for (const i of a.treePit.issues) add({ kind: "observation", key: "tree_pit_issue", value: { issue: i.value, observedAt }, confidence: i.confidence });
  }

  if (a.droughtStress.value === "mild" || a.droughtStress.value === "severe") {
    add({ kind: "observation", key: "drought_stress", value: { level: a.droughtStress.value, observedAt }, confidence: a.droughtStress.confidence });
  }
  if (a.phenology.value !== "not_assessable") {
    add({ kind: "observation", key: "phenology", value: { stage: a.phenology.value, observedAt }, confidence: a.phenology.confidence });
  }
  if (a.ageClass.value !== "not_assessable") {
    add({ kind: "attribute", key: "age_class", value: a.ageClass.value, confidence: a.ageClass.confidence });
  }
  return out;
}
