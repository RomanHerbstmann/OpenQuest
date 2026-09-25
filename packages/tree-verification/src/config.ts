export interface VerificationConfig {
  openRouterApiKey: string;
  openRouterBaseUrl: string;
  /** Models asked in parallel for every frame. */
  visionModels: string[];
  /** Asked additionally when the primary models disagree or are unsure. `null` disables escalation. */
  escalationModel: string | null;
  /** `null` disables the Jev decision stage (rule based fusion only). */
  jevModel: string | null;
  /** Sent as `HTTP-Referer` / `X-Title` for OpenRouter app attribution. */
  appUrl: string;
  appName: string;

  geofenceRadiusM: number;
  /** Radius around the player used to look up known trees. */
  contextRadiusM: number;
  /** Frames older than this go to review. */
  maxCaptureAgeMinutes: number;
  maxImageBytes: number;

  thresholds: {
    /** Final tree probability at or above which a frame may be auto approved. */
    approveTreeProbability: number;
    /** Final tree probability below which a frame is rejected. */
    rejectTreeProbability: number;
    /** Primary models whose tree probabilities differ by more than this "disagree". */
    maxModelSpread: number;
    /** Genus probability needed to confirm a genus or to suggest one for assets without genus. */
    genusDecisive: number;
    /** Jev confidence in "different_tree" needed to send a submission to review. */
    jevVeto: number;
    /** Jev confidence in "reject" needed to send a submission to review. Jev's verdict is conservative, so this is high. */
    jevRejectVeto: number;
  };
  /** No inventory tree within this distance of the player makes a photographed tree a "new tree" candidate. */
  newTreeRadiusM: number;
  /** Assessment findings below this confidence are not proposed as changes. */
  minProposalConfidence: number;
  /** Weight of Jev's tree probability vs. the vision ensemble (0 = ignore Jev). */
  jevWeight: number;

  timeouts: { visionMs: number; jevMs: number; geoMs: number };
}

export const DEFAULT_CONFIG: Omit<VerificationConfig, "openRouterApiKey"> = {
  openRouterBaseUrl: "https://openrouter.ai/api",
  // ADR-0003: gpt-6-luna as second model often labels real photos as illustrations and misses
  // thin young trees; the Gemini pair was better on every metric. Luna stays as cheap tie breaker.
  visionModels: ["google/gemini-3.8-flash", "google/gemini-3.5-flash-lite"],
  escalationModel: "openai/gpt-6-luna",
  jevModel: "typesafe/jev-1.13",
  appUrl: "https://openquest.fun",
  appName: "OpenQuest",

  geofenceRadiusM: 30,
  contextRadiusM: 40,
  maxCaptureAgeMinutes: 10,
  maxImageBytes: 5 * 1024 * 1024,

  thresholds: {
    approveTreeProbability: 0.85,
    rejectTreeProbability: 0.2,
    maxModelSpread: 0.4,
    genusDecisive: 0.6,
    jevVeto: 0.8,
    jevRejectVeto: 0.9,
  },
  jevWeight: 0.35,
  newTreeRadiusM: 12,
  minProposalConfidence: 0.5,

  timeouts: { visionMs: 45_000, jevMs: 15_000, geoMs: 8_000 },
};

type DeepPartial<T> = { [K in keyof T]?: T[K] extends object ? (T[K] extends unknown[] ? T[K] : DeepPartial<T[K]>) : T[K] };

export function resolveConfig(
  overrides: DeepPartial<VerificationConfig> & { openRouterApiKey?: string } = {},
  env: Record<string, string | undefined> = process.env,
): VerificationConfig {
  const list = (v: string | undefined) =>
    v
      ?.split(",")
      .map((s) => s.trim())
      .filter(Boolean);

  const fromEnv: DeepPartial<VerificationConfig> = {};
  const visionModels = list(env.VISION_MODELS);
  if (visionModels?.length) fromEnv.visionModels = visionModels;
  if (env.VISION_ESCALATION_MODEL !== undefined) fromEnv.escalationModel = env.VISION_ESCALATION_MODEL || null;
  if (env.JEV_MODEL !== undefined) fromEnv.jevModel = env.JEV_MODEL || null;

  const apiKey = overrides.openRouterApiKey ?? env.OPENROUTER_API_KEY;
  if (!apiKey) throw new Error("OPENROUTER_API_KEY is not set");

  const merged = { ...DEFAULT_CONFIG, ...fromEnv, ...overrides } as VerificationConfig;
  merged.thresholds = { ...DEFAULT_CONFIG.thresholds, ...overrides.thresholds };
  merged.timeouts = { ...DEFAULT_CONFIG.timeouts, ...overrides.timeouts };
  merged.openRouterApiKey = apiKey;
  return merged;
}
