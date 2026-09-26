import { aggregateAssessment } from "./assess/aggregate.ts";
import { buildProposals } from "./assess/proposals.ts";
import { resolveConfig, type VerificationConfig } from "./config.ts";
import { fuse } from "./decide/fusion.ts";
import { askJev, createJevClient, type JevAnswers, type JevClient } from "./decide/jev.ts";
import { buildGeoContext, type GeoContext } from "./geo/context.ts";
import { precheck } from "./precheck.ts";
import type { ModelCall, NearbyTreeProvider, TreeVerificationResult, VerificationInput } from "./types.ts";
import { runVisionEnsemble, type EnsembleResult } from "./vision/ensemble.ts";
import { createOpenRouterClient, type OpenRouterClient } from "./vision/openrouter.ts";

export interface VerifierDeps {
  config: VerificationConfig;
  /** Known trees near the player, most trusted first (city adapter before OSM). */
  providers?: NearbyTreeProvider[];
  openRouter?: OpenRouterClient;
  jev?: JevClient;
}

/**
 * Decides whether a camera frame shows a tree and whether it is the quest's tree.
 *
 * Pipeline: cheap prechecks, then geo context and vision ensemble in parallel,
 * then Jev as a calibrated second opinion over the structured evidence,
 * then deterministic fusion into approve / review / reject with reasons.
 * Never throws for model or network failures; those degrade to `review`.
 */
export async function verifyTreePhoto(input: VerificationInput, deps: VerifierDeps): Promise<TreeVerificationResult> {
  const started = Date.now();
  const { config } = deps;
  const openRouter =
    deps.openRouter ??
    createOpenRouterClient({ apiKey: config.openRouterApiKey, baseUrl: config.openRouterBaseUrl, appUrl: config.appUrl, appName: config.appName });

  const pre = precheck(input, config);
  const calls: ModelCall[] = [];
  const emptyGeo: GeoContext = { trees: [], genusDistribution: [], failedProviders: [] };

  // A hard precheck failure (bad image, outside geofence) makes model calls pointless.
  const skipModels = !pre.image || pre.reasons.some((r) => r.severity === "hard");

  const center = input.playerPosition ?? input.expected?.position;
  const geoPromise = center && deps.providers?.length
    ? buildGeoContext(deps.providers, center, config.contextRadiusM, config.timeouts.geoMs)
    : Promise.resolve(emptyGeo);

  const visionPromise: Promise<EnsembleResult | null> = skipModels
    ? Promise.resolve(null)
    : runVisionEnsemble(openRouter, pre.image!, {
        models: config.visionModels,
        escalationModel: config.escalationModel,
        timeoutMs: config.timeouts.visionMs,
        unsureBand: [config.thresholds.rejectTreeProbability, config.thresholds.approveTreeProbability],
        maxSpread: config.thresholds.maxModelSpread,
      });

  const [geo, vision] = await Promise.all([geoPromise, visionPromise]);
  if (vision) calls.push(...vision.calls);

  let jev: JevAnswers | undefined;
  let jevFailed = false;
  if (vision && vision.observations.length > 0 && config.jevModel) {
    const client =
      deps.jev ?? createJevClient({ apiKey: config.openRouterApiKey, baseUrl: config.openRouterBaseUrl, model: config.jevModel, timeoutMs: config.timeouts.jevMs });
    const res = await askJev(client, config.jevModel, {
      observations: vision.observations,
      ensembleGenus: vision.genus,
      expected: input.expected,
      distanceToExpectedM: pre.distanceToExpectedM,
      geofenceRadiusM: pre.geofenceRadiusM,
      geo,
    });
    calls.push(res.call);
    jev = res.answers;
    jevFailed = !res.answers;
  }

  const assessment = vision ? aggregateAssessment(vision.observations, jev, config.jevWeight) : null;
  const fused = fuse({ pre, vision, geo, jev, jevFailed, expected: input.expected, assessment }, config);
  const proposedChanges = buildProposals({
    verdict: fused.verdict,
    inventory: fused.inventory,
    assessment: fused.treePresent.value || fused.inventory === "tree_missing" ? assessment : null,
    genusSuggestion: fused.genusSuggestion,
    genus: fused.genus,
    playerPosition: input.playerPosition,
    capturedAt: input.capturedAt ?? new Date(),
    minConfidence: config.minProposalConfidence,
  });

  return {
    ...fused,
    assessment,
    proposedChanges,
    signals: {
      distanceToExpectedM: pre.distanceToExpectedM,
      geofenceRadiusM: pre.geofenceRadiusM,
      nearbyTrees: geo.trees,
      vision: vision?.observations ?? [],
      visionAgreement: vision ? 1 - vision.spread : 0,
      jev,
    },
    models: calls,
    costUsd: calls.reduce((s, c) => s + c.costUsd, 0),
    latencyMs: Date.now() - started,
  };
}

export { resolveConfig, DEFAULT_CONFIG, type VerificationConfig } from "./config.ts";
export { createOsmTreeProvider, DEFAULT_OVERPASS_URLS } from "./geo/osm.ts";
export { haversineM, bboxAround } from "./geo/distance.ts";
export { normalizeGenus } from "./vision/ensemble.ts";
export { createOpenRouterClient, type OpenRouterClient } from "./vision/openrouter.ts";
export type { VisionObservation } from "./vision/schema.ts";
export type { JevAnswers, JevClient } from "./decide/jev.ts";
export type { TreeAssessment, SafetyFlag, Voted, Finding } from "./assess/aggregate.ts";
export type { ProposedChange, InventoryStatus } from "./assess/proposals.ts";
export type * from "./types.ts";
