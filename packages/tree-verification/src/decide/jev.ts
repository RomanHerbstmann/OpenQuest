import { choice, noul, TypeSafeClient, type Questions } from "@typesafe-ai/sdk";
import type { GeoContext } from "../geo/context.ts";
import type { ExpectedTree, GenusEstimate, ModelCall } from "../types.ts";
import { PHENOLOGY, type VisionObservation } from "../vision/schema.ts";

export interface JevAnswers {
  treePresent: number;
  targetMatch?: { choice: "expected_tree" | "different_tree" | "no_tree" | "uncertain"; confidence: number; probabilities: Record<string, number> };
  genus: { choice: string; confidence: number; probabilities: Record<string, number> };
  verdict: { choice: "approve" | "review" | "reject"; confidence: number; probabilities: Record<string, number> };
  vitality?: { choice: string; confidence: number; probabilities: Record<string, number> };
  phenology?: { choice: string; confidence: number; probabilities: Record<string, number> };
  safetyConcern?: number;
}

/** The subset of the TypeSafe client we use; lets tests inject a fake. */
export interface JevClient {
  systemOne(req: { state: unknown; questions: Questions; model?: string }): PromiseLike<{
    answers: Record<string, { type: string; noul?: number; choice?: string; confidence?: number; probabilities?: Record<string, number> }>;
    usage: { input_tokens: number; output_tokens: number; cost?: number };
  }>;
}

/** Jev through OpenRouter: the OpenRouter key works, no separate TypeSafe key needed. */
export function createJevClient(opts: { apiKey: string; baseUrl: string; model: string; timeoutMs: number }): JevClient {
  return new TypeSafeClient({
    apiKey: opts.apiKey,
    baseURL: opts.baseUrl,
    defaultModel: opts.model,
    timeout: opts.timeoutMs,
    retry: { maxRetries: 1 },
    logLevel: "off",
  }) as unknown as JevClient;
}

export interface JevInput {
  observations: VisionObservation[];
  ensembleGenus: GenusEstimate[];
  expected?: ExpectedTree;
  distanceToExpectedM?: number;
  geofenceRadiusM: number;
  geo: GeoContext;
}

/** Jev reads text only, so the state is a compact, English, fully structured summary of all evidence. */
export function buildJevState(input: JevInput) {
  return {
    task: "A player of a civic game must photograph a specific municipal tree. Decide from the evidence whether the photo is valid.",
    expected_tree: input.expected
      ? { genus: input.expected.genus ?? "unknown (not recorded by the city)", player_distance_m: round(input.distanceToExpectedM), geofence_radius_m: input.geofenceRadiusM }
      : "none (free photo, no specific target)",
    known_trees_near_player: input.geo.trees.slice(0, 12).map((t) => ({ genus: t.genus ?? "unknown", distance_m: round(t.distanceM), source: t.source })),
    vision_models: input.observations.map((o) => ({
      model: o.model,
      tree_present: o.tree_present,
      confidence: o.tree_present_confidence,
      main_subject: o.main_subject,
      tree_count: o.tree_count,
      main_tree_frame_share: o.main_tree_frame_share,
      visible_parts: Object.entries(o.visible_parts).filter(([, v]) => v).map(([k]) => k),
      leafless: o.leafless,
      genus_candidates: o.genus_candidates.map((c) => `${c.genus} ${(c.probability * 100).toFixed(0)}% (${c.evidence})`),
      photo_authenticity: o.photo_authenticity,
      image_quality: o.image_quality,
      quality_issues: o.quality_issues.filter((q) => q !== "none"),
      description: o.scene_description,
      assessment: o.assessment
        ? {
            site_state: o.assessment.site_state,
            vitality: o.assessment.vitality,
            crown_density: o.assessment.crown_density,
            damage: o.assessment.damage.filter((d) => d !== "none"),
            fungi_on_trunk: o.assessment.fungi_on_trunk,
            pests: o.assessment.pests.filter((p) => p !== "none"),
            drought_stress: o.assessment.drought_stress,
            phenology: o.assessment.phenology,
            notes: o.assessment.notes,
          }
        : null,
    })),
    combined_genus_vote: input.ensembleGenus.slice(0, 5).map((g) => `${g.genus} ${(g.probability * 100).toFixed(0)}%`),
  };
}

const round = (n: number | undefined) => (n === undefined ? null : Math.round(n * 10) / 10);

export function buildJevQuestions(input: JevInput): Questions {
  const labels = new Set<string>();
  for (const g of input.ensembleGenus) labels.add(g.genus);
  if (input.expected?.genus) labels.add(input.expected.genus);
  for (const g of input.geo.genusDistribution) labels.add(g.genus);
  const genusCriteria: Record<string, string | null> = {};
  for (const l of [...labels].slice(0, 250)) genusCriteria[l] = null;
  genusCriteria.other = "a genus not listed here";
  genusCriteria.unknown = "no tree, or the genus cannot be determined from the evidence";

  const questions: Questions = {
    tree_present: noul(
      "Does the photo show a real, living, rooted tree as a main subject (not a shrub, hedge, potted plant, stump, or a picture/screen of a tree)?",
    ),
    genus: choice("Which genus is the main tree in the photo, weighing the vision models' visible evidence above the local tree list?", genusCriteria),
    verdict: choice("Should the submission be accepted automatically?", {
      approve: "clearly a real live photo of a tree that plausibly is the target tree; no quality or cheating concerns",
      review: "plausible but something is uncertain or inconsistent; a human should look",
      reject: "no tree, a picture of a screen or print, or clearly not the target",
    }),
  };
  if (input.observations.some((o) => o.assessment && o.assessment.site_state === "tree_present")) {
    questions.vitality = choice("How vital is the main tree according to the models' observations (Roloff scale)?", {
      healthy: "full, even crown",
      slightly_damaged: "somewhat thin crown or some dead twigs",
      clearly_damaged: "sparse crown, dead branches, larger wounds",
      severely_damaged_or_dead: "mostly dead or dying",
      not_assessable: "leafless, close-up, or evidence insufficient",
    });
    questions.phenology = choice("Which seasonal stage does the main tree show?", Object.fromEntries(PHENOLOGY.map((p) => [p, null])));
    questions.safety_concern = noul(
      "Do the observations indicate a possible hazard a city tree inspector should check (fungal fruiting bodies, large cavity or crack, dangerous lean, broken hanging branch, oak processionary moth nests, dead tree)?",
    );
  }

  // Without an expected genus there is nothing to compare; Jev would guess "different_tree".
  if (input.expected?.genus) {
    questions.target_match = choice("Is the photographed tree the expected target tree?", {
      expected_tree: "a tree consistent with the expected genus and position",
      different_tree: "a tree, but evidence (e.g. genus) points to a different nearby tree",
      no_tree: "no tree is shown",
      uncertain: "cannot be decided from the evidence",
    });
  }
  return questions;
}

export async function askJev(client: JevClient, model: string, input: JevInput): Promise<{ answers?: JevAnswers; call: ModelCall }> {
  const started = Date.now();
  try {
    const res = await client.systemOne({ model, state: buildJevState(input), questions: buildJevQuestions(input) });
    const a = res.answers;
    const pick = <T extends string>(k: string) =>
      a[k] ? { choice: a[k]!.choice as T, confidence: a[k]!.confidence ?? 0, probabilities: a[k]!.probabilities ?? {} } : undefined;
    const answers: JevAnswers = {
      treePresent: a.tree_present?.noul ?? 0.5,
      genus: pick<string>("genus")!,
      verdict: pick<"approve" | "review" | "reject">("verdict")!,
      targetMatch: pick("target_match"),
      vitality: pick<string>("vitality"),
      phenology: pick<string>("phenology"),
      safetyConcern: a.safety_concern?.noul,
    };
    if (!answers.genus || !answers.verdict) throw new Error("incomplete Jev answer");
    return { answers, call: { model, stage: "jev", ok: true, latencyMs: Date.now() - started, costUsd: res.usage.cost ?? 0 } };
  } catch (err) {
    return { call: { model, stage: "jev", ok: false, latencyMs: Date.now() - started, costUsd: 0, error: err instanceof Error ? err.message : String(err) } };
  }
}
