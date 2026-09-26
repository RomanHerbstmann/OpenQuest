import type { TreeAssessment } from "../assess/aggregate.ts";
import type { InventoryStatus } from "../assess/proposals.ts";
import type { VerificationConfig } from "../config.ts";
import type { GeoContext } from "../geo/context.ts";
import type { PrecheckResult } from "../precheck.ts";
import type { ExpectedTree, GenusEstimate, Reason, TreeVerificationResult, Verdict } from "../types.ts";
import type { EnsembleResult } from "../vision/ensemble.ts";
import type { JevAnswers } from "./jev.ts";

export interface FusionInput {
  pre: PrecheckResult;
  vision: EnsembleResult | null;
  geo: GeoContext;
  jev?: JevAnswers;
  jevFailed: boolean;
  expected?: ExpectedTree;
  assessment?: TreeAssessment | null;
}

export type FusionResult = Pick<TreeVerificationResult, "verdict" | "treePresent" | "targetMatch" | "genus" | "genusSuggestion" | "reasons" | "inventory">;

const NON_LIVE = new Set(["photo_of_screen", "photo_of_print", "illustration_or_render"]);

/** Blends the ensemble genus vote with Jev's genus distribution (labels "other"/"unknown" dropped). */
export function blendGenus(vision: GenusEstimate[], jev: JevAnswers | undefined, jevWeight: number): GenusEstimate[] {
  if (!jev || jevWeight <= 0) return vision;
  const out = new Map<string, number>();
  for (const g of vision) out.set(g.genus, (1 - jevWeight) * g.probability);
  for (const [genus, p] of Object.entries(jev.genus.probabilities)) {
    if (genus === "other" || genus === "unknown") continue;
    out.set(genus, (out.get(genus) ?? 0) + jevWeight * p);
  }
  return [...out].map(([genus, probability]) => ({ genus, probability })).sort((a, b) => b.probability - a.probability);
}

/**
 * Deterministic decision. Models provide probabilities, rules decide.
 * Order of precedence: hard reasons reject, any soft reason sends to review, otherwise approve.
 * Jev can only make the verdict stricter, never more lenient.
 */
export function fuse(input: FusionInput, config: VerificationConfig): FusionResult {
  const { thresholds: t } = config;
  const reasons: Reason[] = [...input.pre.reasons];
  const add = (code: Reason["code"], severity: Reason["severity"], detail?: string) => reasons.push({ code, severity, detail });

  if (input.jevFailed) add("jev_unavailable", "info");

  const vision = input.vision;
  if (!vision || vision.treeProbability === null) {
    if (input.pre.image) add("vision_unavailable", "soft", "all vision models failed");
    return finish(reasons, { value: false, probability: 0 }, undefined, { value: null, probability: 0, alternatives: [] }, "unknown");
  }

  const w = input.jev ? config.jevWeight : 0;
  const treeP = (1 - w) * vision.treeProbability + w * (input.jev?.treePresent ?? 0);
  const obs = vision.observations;

  const nonLive = obs.filter((o) => NON_LIVE.has(o.photo_authenticity)).length;
  if (nonLive > 0 && nonLive === obs.length) add("not_a_live_photo", "hard", obs.map((o) => o.photo_authenticity).join(", "));
  else if (nonLive > 0) add("possibly_not_live_photo", "soft", `${nonLive}/${obs.length} models`);

  // A stump or empty tree pit at the target is not a failed quest but a valuable report:
  // the tree is gone. Only for live photos inside the geofence (hard prechecks already reject the rest).
  const site = input.assessment?.siteState;
  const treeMissing =
    !!input.expected && treeP < 0.5 && nonLive === 0 && !!site && (site.value === "stump" || site.value === "empty_tree_pit") && site.confidence >= 0.5;

  if (treeMissing) add("tree_missing", "soft", `${site!.value} at the recorded position`);
  else if (treeP < t.rejectTreeProbability) add("no_tree", "hard", `p=${treeP.toFixed(2)}`);
  else if (treeP < t.approveTreeProbability) add("tree_uncertain", "soft", `p=${treeP.toFixed(2)}`);

  const safety = treeP >= 0.5 ? (input.assessment?.safetyFlags ?? []) : [];
  if (safety.length) add("safety_concern", "soft", safety.map((f) => `${f.value} ${(f.confidence * 100).toFixed(0)}%`).join(", "));

  if (vision.spread > t.maxModelSpread) add("models_disagree", "soft", `spread=${vision.spread.toFixed(2)}`);

  const poor = obs.filter((o) => o.image_quality === "poor").length;
  if (poor * 2 > obs.length) add("poor_image_quality", "soft", [...new Set(obs.flatMap((o) => o.quality_issues))].join(", "));

  const genusDist = blendGenus(vision.genus, input.jev, config.jevWeight);
  const top = genusDist[0];
  const genus = { value: top && top.probability >= 0.3 ? top.genus : null, probability: top?.probability ?? 0, alternatives: genusDist.slice(0, 5) };

  let targetMatch: FusionResult["targetMatch"];
  let genusSuggestion: GenusEstimate | undefined;
  if (input.expected) {
    if (treeMissing) {
      targetMatch = { value: "tree_missing", probability: site!.confidence };
    } else if (treeP < 0.5) {
      targetMatch = { value: "no_tree", probability: 1 - treeP };
    } else if (input.expected.genus && top) {
      // No special case for leafless trees: the models lower their genus probabilities themselves,
      // and some genera (Betula, Platanus) stay recognizable in winter.
      const expected = input.expected.genus;
      const pExpected = genusDist.find((g) => g.genus === expected)?.probability ?? 0;
      if (top.genus === expected && top.probability >= t.genusDecisive) {
        targetMatch = { value: "expected_tree", probability: top.probability };
        add("genus_confirmed", "info", expected);
      } else if (top.genus !== expected && top.probability >= 0.45 && pExpected <= 0.1) {
        // Relative rule: a clear alternative genus while the expected one is (almost) absent.
        targetMatch = { value: "different_tree", probability: top.probability };
        add("genus_mismatch", "soft", `expected ${expected}, photo looks like ${top.genus} (${(top.probability * 100).toFixed(0)}%)`);
        const neighbor = input.geo.trees.find((tr) => tr.genus === top.genus);
        if (neighbor) add("possibly_neighbor_tree", "soft", `${top.genus} recorded ${neighbor.distanceM?.toFixed(0)} m from the player`);
      } else {
        targetMatch = { value: "uncertain", probability: Math.max(pExpected, 1 - top.probability) };
      }
    } else {
      targetMatch = { value: "uncertain", probability: 0.5 };
    }

    if (!input.expected.genus && top && top.probability >= t.genusDecisive && treeP >= t.approveTreeProbability) {
      genusSuggestion = { genus: top.genus, probability: top.probability };
    }
  }

  if (input.jev) {
    // Eval finding: Jev's target_match separates right and wrong trees well, its overall verdict is
    // too cautious to veto with. So target_match vetoes at jevVeto, the verdict only on a strong reject.
    const v = input.jev.verdict;
    if (v.choice === "reject" && v.confidence >= t.jevRejectVeto) add("jev_disagrees", "soft", `Jev: reject (${(v.confidence * 100).toFixed(0)}%)`);
    const tm = input.jev.targetMatch;
    if (tm?.choice === "different_tree" && tm.confidence >= t.jevVeto && !reasons.some((r) => r.code === "genus_mismatch")) {
      add("jev_disagrees", "soft", `Jev: different tree (${(tm.confidence * 100).toFixed(0)}%)`);
    }
    if (tm && targetMatch && targetMatch.value === "uncertain" && tm.choice !== "uncertain") {
      targetMatch = { value: tm.choice, probability: tm.confidence };
    }
  }

  // Free photo (no target): a tree with no inventory tree close to the player is a candidate
  // for a tree the city has not recorded yet. Only trust this if every provider answered.
  const nearest = input.geo.trees[0]?.distanceM ?? Infinity;
  const newTree = !input.expected && treeP >= 0.5 && input.geo.failedProviders.length === 0 && nearest > config.newTreeRadiusM;
  if (newTree) add("new_tree_candidate", "soft", nearest === Infinity ? "no inventory tree nearby" : `nearest inventory tree ${nearest.toFixed(0)} m`);

  const inventory: InventoryStatus = treeMissing
    ? "tree_missing"
    : newTree
      ? "new_tree_candidate"
      : treeP >= 0.5 && (input.expected || nearest <= config.newTreeRadiusM)
        ? "confirmed"
        : "unknown";

  if (treeP >= t.approveTreeProbability) add("tree_confirmed", "info", `p=${treeP.toFixed(2)}`);

  return finish(reasons, { value: treeP >= 0.5, probability: treeP }, targetMatch, genus, inventory, genusSuggestion);
}

function finish(
  reasons: Reason[],
  treePresent: FusionResult["treePresent"],
  targetMatch: FusionResult["targetMatch"],
  genus: FusionResult["genus"],
  inventory: InventoryStatus,
  genusSuggestion?: GenusEstimate,
): FusionResult {
  const verdict: Verdict = reasons.some((r) => r.severity === "hard")
    ? "reject"
    : reasons.some((r) => r.severity === "soft")
      ? "review"
      : "approve";
  return { verdict, treePresent, targetMatch, genus, genusSuggestion, reasons, inventory };
}
