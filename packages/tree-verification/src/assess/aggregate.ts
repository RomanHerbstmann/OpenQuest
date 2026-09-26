import type { JevAnswers } from "../decide/jev.ts";
import type { TreeAssessmentObservation, VisionObservation } from "../vision/schema.ts";

/** A categorical finding with how strongly the models (and Jev) back it. */
export interface Voted<T extends string> {
  value: T;
  /** 0..1: share of models agreeing, blended with Jev where Jev was asked. */
  confidence: number;
}

/** A finding in a list (damage, pest, ...): `confidence` = share of models reporting it. */
export interface Finding<T extends string> {
  value: T;
  confidence: number;
}

export type SafetyFlag =
  | "fungi_on_trunk"
  | "oak_processionary_nests"
  | "cavity"
  | "crack"
  | "leaning"
  | "broken_branch"
  | "root_damage"
  | "dead_or_dying"
  /** Jev sees a hazard in the combined evidence that no single model flagged. */
  | "possible_hazard";

export interface TreeAssessment {
  siteState: Voted<TreeAssessmentObservation["site_state"]>;
  vitality: Voted<TreeAssessmentObservation["vitality"]>;
  crownDensity: Voted<TreeAssessmentObservation["crown_density"]>;
  damage: Finding<Exclude<TreeAssessmentObservation["damage"][number], "none">>[];
  fungiOnTrunk: Finding<"fungi_on_trunk"> | null;
  pests: Finding<Exclude<TreeAssessmentObservation["pests"][number], "none">>[];
  droughtStress: Voted<TreeAssessmentObservation["drought_stress"]>;
  treePit: {
    visible: boolean;
    surface: Voted<TreeAssessmentObservation["tree_pit"]["surface"]>;
    issues: Finding<Exclude<TreeAssessmentObservation["tree_pit"]["issues"][number], "none">>[];
    wateringBag: Finding<"watering_bag"> | null;
    stakes: Finding<"stakes"> | null;
    protectionGuard: Finding<"protection_guard"> | null;
  };
  phenology: Voted<TreeAssessmentObservation["phenology"]>;
  ageClass: Voted<TreeAssessmentObservation["age_class"]>;
  /**
   * Possible hazards. Raised as soon as ONE model (or Jev) reports them: a missed hazard costs
   * more than an extra review. Never auto approved, never auto exported as fact.
   */
  safetyFlags: Finding<SafetyFlag>[];
  notes: string[];
}

const UNKNOWN = new Set(["not_assessable", "not_visible", "unclear"]);

/** Order used to break ties conservatively (worse condition wins). */
const VITALITY_RANK = ["healthy", "slightly_damaged", "clearly_damaged", "severely_damaged_or_dead"];

function vote<T extends string>(values: T[], opts: { jev?: { choice: string; probabilities: Record<string, number> }; jevWeight?: number; tieBreak?: (a: T, b: T) => T } = {}): Voted<T> {
  const known = values.filter((v) => !UNKNOWN.has(v));
  const pool = known.length ? known : values;
  const counts = new Map<T, number>();
  for (const v of pool) counts.set(v, (counts.get(v) ?? 0) + 1);
  const ranked = [...counts].sort((a, b) => b[1] - a[1]);
  let [value, n] = ranked[0] ?? [values[0]!, 0];
  const tied = ranked.filter(([, c]) => c === n).map(([v]) => v);

  const jevP = (v: T) => opts.jev?.probabilities[v] ?? 0;
  if (tied.length > 1) {
    // Jev breaks ties first, then the field specific rule, then the primary model's answer.
    const byJev = opts.jev ? tied.find((v) => v === opts.jev!.choice) : undefined;
    value = byJev ?? (opts.tieBreak ? tied.reduce(opts.tieBreak) : tied[0]!);
  }
  const share = values.length ? n / values.length : 0;
  const w = opts.jev ? (opts.jevWeight ?? 0.35) : 0;
  return { value, confidence: (1 - w) * share + w * jevP(value) };
}

function findings<T extends string>(lists: T[][], total: number): Finding<Exclude<T, "none">>[] {
  const counts = new Map<T, number>();
  for (const list of lists) for (const v of new Set(list)) if (v !== "none") counts.set(v, (counts.get(v) ?? 0) + 1);
  return [...counts]
    .map(([value, c]) => ({ value: value as Exclude<T, "none">, confidence: c / total }))
    .sort((a, b) => b.confidence - a.confidence);
}

function flag<T extends string>(name: T, values: boolean[]): Finding<T> | null {
  const c = values.filter(Boolean).length;
  return c ? { value: name, confidence: c / values.length } : null;
}

/** Merges the assessments of all vision models (plus Jev's tie breaks) into one. */
export function aggregateAssessment(observations: VisionObservation[], jev?: JevAnswers, jevWeight = 0.35): TreeAssessment | null {
  const a = observations.map((o) => o.assessment).filter((x): x is TreeAssessmentObservation => !!x);
  if (!a.length) return null;
  const n = a.length;

  const vitality = vote(a.map((x) => x.vitality), {
    jev: jev?.vitality,
    jevWeight,
    tieBreak: (x, y) => (VITALITY_RANK.indexOf(x) >= VITALITY_RANK.indexOf(y) ? x : y),
  });
  const damage = findings(a.map((x) => x.damage), n);
  const pests = findings(a.map((x) => x.pests), n);
  const fungi = flag("fungi_on_trunk" as const, a.map((x) => x.fungi_on_trunk));
  const pitVisible = a.filter((x) => x.tree_pit.visible);

  const safety = new Map<SafetyFlag, number>();
  const raise = (f: SafetyFlag, c: number) => safety.set(f, Math.max(safety.get(f) ?? 0, c));
  if (fungi) raise("fungi_on_trunk", fungi.confidence);
  for (const p of pests) if (p.value === "oak_processionary_nests") raise("oak_processionary_nests", p.confidence);
  for (const d of damage) if (["cavity", "crack", "leaning", "broken_branch", "root_damage"].includes(d.value)) raise(d.value as SafetyFlag, d.confidence);
  const dead = a.filter((x) => x.vitality === "severely_damaged_or_dead").length;
  if (dead) raise("dead_or_dying", dead / n);
  if (jev?.safetyConcern !== undefined && jev.safetyConcern >= 0.8 && safety.size === 0) raise("possible_hazard", jev.safetyConcern);

  return {
    siteState: vote(a.map((x) => x.site_state)),
    vitality,
    crownDensity: vote(a.map((x) => x.crown_density)),
    damage,
    fungiOnTrunk: fungi,
    pests,
    droughtStress: vote(a.map((x) => x.drought_stress)),
    treePit: {
      visible: pitVisible.length * 2 >= n && pitVisible.length > 0,
      surface: vote((pitVisible.length ? pitVisible : a).map((x) => x.tree_pit.surface)),
      issues: findings(pitVisible.map((x) => x.tree_pit.issues), Math.max(1, pitVisible.length)),
      wateringBag: flag("watering_bag" as const, a.map((x) => x.tree_pit.watering_bag)),
      stakes: flag("stakes" as const, a.map((x) => x.tree_pit.stakes)),
      protectionGuard: flag("protection_guard" as const, a.map((x) => x.tree_pit.protection_guard)),
    },
    phenology: vote(a.map((x) => x.phenology), { jev: jev?.phenology, jevWeight }),
    ageClass: vote(a.map((x) => x.age_class)),
    safetyFlags: [...safety].map(([value, confidence]) => ({ value, confidence })).sort((x, y) => y.confidence - x.confidence),
    notes: [...new Set(a.map((x) => x.notes.trim()).filter(Boolean))],
  };
}
