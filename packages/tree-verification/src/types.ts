export interface LatLon {
  lat: number;
  lon: number;
}

export interface PlayerPosition extends LatLon {
  /** Reported GPS accuracy in meters (browser `coords.accuracy`). */
  accuracyM?: number;
}

/** The tree the quest points at, taken from our asset table. */
export interface ExpectedTree {
  externalId?: string;
  position: LatLon;
  /** Latin genus, `null` if the source has no usable value (e.g. placeholders). */
  genus: string | null;
}

export interface VerificationImage {
  /** Raw bytes or base64 (without data URL prefix). */
  data: Uint8Array | string;
  /** Detected from magic bytes if omitted. */
  mimeType?: "image/jpeg" | "image/png" | "image/webp";
}

export interface VerificationInput {
  image: VerificationImage;
  playerPosition?: PlayerPosition;
  expected?: ExpectedTree;
  capturedAt?: Date;
  /** Overrides `config.geofenceRadiusM` (QUEST.geofence_radius_m). */
  geofenceRadiusM?: number;
}

/** A tree known from some geo source near a position. */
export interface NearbyTree {
  source: string;
  externalId?: string;
  position: LatLon;
  genus: string | null;
  species?: string | null;
  /** Distance to the query center, filled by the context builder. */
  distanceM?: number;
}

/**
 * Anything that can list known trees around a point: a city adapter, OSM, our own DB.
 * Implementations must not throw for "nothing found", only for real failures.
 */
export interface NearbyTreeProvider {
  id: string;
  findNearby(center: LatLon, radiusM: number, signal?: AbortSignal): Promise<NearbyTree[]>;
}

export type Verdict = "approve" | "review" | "reject";

export type ReasonCode =
  | "invalid_image"
  | "image_too_large"
  | "outside_geofence"
  | "gps_inaccurate"
  | "stale_capture"
  | "no_tree"
  | "tree_uncertain"
  | "models_disagree"
  | "not_a_live_photo"
  | "possibly_not_live_photo"
  | "poor_image_quality"
  | "genus_mismatch"
  | "possibly_neighbor_tree"
  | "no_known_tree_nearby"
  | "vision_unavailable"
  | "jev_unavailable"
  | "jev_disagrees"
  | "tree_confirmed"
  | "genus_confirmed";

export interface Reason {
  code: ReasonCode;
  /** `hard` reasons decide the verdict on their own. */
  severity: "info" | "soft" | "hard";
  detail?: string;
}

export interface GenusEstimate {
  genus: string;
  probability: number;
}

export interface ModelCall {
  model: string;
  stage: "vision" | "vision_escalation" | "jev";
  ok: boolean;
  latencyMs: number;
  costUsd: number;
  error?: string;
}

export interface TreeVerificationResult {
  verdict: Verdict;
  /** Final probability that the photo shows a real tree as main subject. */
  treePresent: { value: boolean; probability: number };
  /** Only set when an expected tree was given. */
  targetMatch?: { value: "expected_tree" | "different_tree" | "no_tree" | "uncertain"; probability: number };
  genus: { value: string | null; probability: number; alternatives: GenusEstimate[] };
  /** Proposed genus for assets without a usable genus (feeds ATTRIBUTE_CHANGE). */
  genusSuggestion?: GenusEstimate;
  reasons: Reason[];
  signals: {
    distanceToExpectedM?: number;
    geofenceRadiusM: number;
    nearbyTrees: NearbyTree[];
    vision: import("./vision/schema.ts").VisionObservation[];
    visionAgreement: number;
    jev?: import("./decide/jev.ts").JevAnswers;
  };
  models: ModelCall[];
  costUsd: number;
  latencyMs: number;
}
