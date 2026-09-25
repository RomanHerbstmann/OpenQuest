import type { GenusEstimate, ModelCall } from "../types.ts";
import type { OpenRouterClient } from "./openrouter.ts";
import { buildVisionPrompt, VISION_JSON_SCHEMA, type VisionObservation } from "./schema.ts";

const COMMON_NAMES: Record<string, string> = {
  linden: "Tilia", lime: "Tilia", linde: "Tilia", basswood: "Tilia",
  oak: "Quercus", eiche: "Quercus",
  maple: "Acer", sycamore: "Acer", ahorn: "Acer",
  plane: "Platanus", platane: "Platanus",
  birch: "Betula", birke: "Betula",
  hornbeam: "Carpinus", hainbuche: "Carpinus",
  ash: "Fraxinus", esche: "Fraxinus",
  beech: "Fagus", buche: "Fagus",
  chestnut: "Aesculus", kastanie: "Aesculus",
  cherry: "Prunus", kirsche: "Prunus",
  rowan: "Sorbus", eberesche: "Sorbus",
  willow: "Salix", weide: "Salix",
  poplar: "Populus", pappel: "Populus",
  elm: "Ulmus", ulme: "Ulmus",
  robinia: "Robinia", locust: "Robinia",
  pine: "Pinus", kiefer: "Pinus",
  spruce: "Picea", fichte: "Picea",
};

/** "tilia cordata" -> "Tilia", "Linden" -> "Tilia", "Malus-Hybride" -> "Malus". */
export function normalizeGenus(raw: string | null | undefined): string | null {
  if (!raw) return null;
  const word = raw.trim().split(/[\s\-_(,/]+/)[0]?.replace(/[^\p{L}]/gu, "");
  if (!word || word.length < 3) return null;
  const lower = word.toLowerCase();
  if (["unknown", "none", "unbekannt", "tree", "baum", "other"].includes(lower)) return null;
  return COMMON_NAMES[lower] ?? lower.charAt(0).toUpperCase() + lower.slice(1);
}

/** Probability that the frame shows a tree, as reported by one model. */
export function treeProbability(obs: VisionObservation): number {
  const c = clamp01(obs.tree_present_confidence);
  if (obs.tree_present === "yes") return c;
  if (obs.tree_present === "no") return 1 - c;
  return 0.5;
}

const clamp01 = (n: number) => (Number.isFinite(n) ? Math.min(1, Math.max(0, n)) : 0.5);

/** Validates the model JSON and fixes the harmless deviations models make (casing, prob sums). */
export function parseObservation(model: string, content: string): VisionObservation {
  const cleaned = content.trim().replace(/^```(?:json)?\s*|\s*```$/g, "");
  const o = JSON.parse(cleaned) as VisionObservation;
  if (!["yes", "no", "uncertain"].includes(o.tree_present)) throw new Error(`invalid tree_present: ${o.tree_present}`);
  const candidates = (Array.isArray(o.genus_candidates) ? o.genus_candidates : [])
    .map((c) => ({ ...c, genus: normalizeGenus(c.genus) ?? "", probability: clamp01(c.probability) }))
    .filter((c) => c.genus)
    .slice(0, 3);
  const sum = candidates.reduce((s, c) => s + c.probability, 0);
  if (sum > 1) for (const c of candidates) c.probability /= sum;
  return {
    ...o,
    model,
    tree_present_confidence: clamp01(o.tree_present_confidence),
    genus_candidates: candidates,
    quality_issues: Array.isArray(o.quality_issues) ? o.quality_issues : [],
  };
}

export interface EnsembleResult {
  observations: VisionObservation[];
  calls: ModelCall[];
  /** Mean tree probability over successful models, `null` if all failed. */
  treeProbability: number | null;
  /** Max minus min tree probability between models (0 = perfect agreement). */
  spread: number;
  genus: GenusEstimate[];
  escalated: boolean;
}

export interface EnsembleOptions {
  models: string[];
  escalationModel: string | null;
  timeoutMs: number;
  /** Escalate when the mean lands in this "unsure" band. */
  unsureBand: [number, number];
  maxSpread: number;
}

async function observe(
  client: OpenRouterClient,
  model: string,
  image: { base64: string; mimeType: string },
  timeoutMs: number,
  stage: ModelCall["stage"],
): Promise<{ obs?: VisionObservation; call: ModelCall }> {
  const started = Date.now();
  let lastError: unknown;
  // One retry: providers occasionally return an empty or truncated completion.
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      return await observeOnce(client, model, image, timeoutMs, stage);
    } catch (err) {
      lastError = err;
    }
  }
  return {
    call: { model, stage, ok: false, latencyMs: Date.now() - started, costUsd: 0, error: lastError instanceof Error ? lastError.message : String(lastError) },
  };
}

async function observeOnce(
  client: OpenRouterClient,
  model: string,
  image: { base64: string; mimeType: string },
  timeoutMs: number,
  stage: ModelCall["stage"],
): Promise<{ obs: VisionObservation; call: ModelCall }> {
  const res = await client.chat(
    {
      model,
      max_tokens: 4000,
      messages: [
        {
          role: "user",
          content: [
            { type: "text", text: buildVisionPrompt() },
            { type: "image_url", image_url: { url: `data:${image.mimeType};base64,${image.base64}` } },
          ],
        },
      ],
      response_format: { type: "json_schema", json_schema: { name: "tree_observation", strict: true, schema: VISION_JSON_SCHEMA } },
    },
    AbortSignal.timeout(timeoutMs),
  );
  const obs = parseObservation(model, res.content);
  return { obs, call: { model, stage, ok: true, latencyMs: res.latencyMs, costUsd: res.costUsd } };
}

/** Weighted genus vote: each model's candidate probabilities, averaged over models that saw a tree. */
export function aggregateGenus(observations: VisionObservation[]): GenusEstimate[] {
  const voters = observations.filter((o) => o.genus_candidates.length > 0);
  if (!voters.length) return [];
  const score = new Map<string, number>();
  for (const o of voters) for (const c of o.genus_candidates) score.set(c.genus, (score.get(c.genus) ?? 0) + c.probability / voters.length);
  return [...score].map(([genus, probability]) => ({ genus, probability })).sort((a, b) => b.probability - a.probability);
}

function summarize(observations: VisionObservation[]) {
  const probs = observations.map(treeProbability);
  if (!probs.length) return { treeProbability: null, spread: 0 };
  return {
    treeProbability: probs.reduce((s, p) => s + p, 0) / probs.length,
    spread: Math.max(...probs) - Math.min(...probs),
  };
}

function topGenusConflict(observations: VisionObservation[]): boolean {
  const tops = observations
    .map((o) => o.genus_candidates[0])
    .filter((c): c is NonNullable<typeof c> => !!c && c.probability >= 0.4)
    .map((c) => c.genus);
  return new Set(tops).size > 1;
}

export async function runVisionEnsemble(
  client: OpenRouterClient,
  image: { base64: string; mimeType: string },
  opts: EnsembleOptions,
): Promise<EnsembleResult> {
  const first = await Promise.all(opts.models.map((m) => observe(client, m, image, opts.timeoutMs, "vision")));
  const observations = first.flatMap((r) => (r.obs ? [r.obs] : []));
  const calls = first.map((r) => r.call);

  let { treeProbability: p, spread } = summarize(observations);
  const unsure = p !== null && p >= opts.unsureBand[0] && p < opts.unsureBand[1];
  const needsEscalation =
    opts.escalationModel !== null &&
    !opts.models.includes(opts.escalationModel) &&
    (observations.length < opts.models.length || spread > opts.maxSpread || unsure || topGenusConflict(observations));

  let escalated = false;
  if (needsEscalation && opts.escalationModel) {
    const extra = await observe(client, opts.escalationModel, image, opts.timeoutMs, "vision_escalation");
    calls.push(extra.call);
    if (extra.obs) {
      observations.push(extra.obs);
      escalated = true;
      ({ treeProbability: p, spread } = summarize(observations));
    }
  }

  return { observations, calls, treeProbability: p, spread, genus: aggregateGenus(observations), escalated };
}
