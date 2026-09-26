import { resolveConfig } from "../src/config.ts";
import type { TreeAssessmentObservation, VisionObservation } from "../src/vision/schema.ts";

export const config = resolveConfig({ openRouterApiKey: "test-key" }, {});

/** Smallest valid JPEG header, enough for magic byte detection. */
export const JPEG = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 0x10, 0x4a, 0x46, 0x49, 0x46]);

export const TREE_POS = { lat: 51.96219, lon: 7.62545 };

export function obs(overrides: Partial<VisionObservation> = {}): VisionObservation {
  return {
    model: "m1",
    tree_present: "yes",
    tree_present_confidence: 0.95,
    main_subject: "single_tree",
    tree_count: 1,
    main_tree_frame_share: "large",
    visible_parts: { trunk: true, bark_closeup: false, leaves_closeup: false, canopy: true, flowers: false, fruits_or_seeds: false, bare_branches: false },
    leafless: false,
    genus_candidates: [{ genus: "Tilia", probability: 0.8, evidence: "heart-shaped leaves" }],
    photo_authenticity: "live_camera_photo",
    image_quality: "good",
    quality_issues: ["none"],
    scene_description: "A linden tree on a sidewalk.",
    assessment: assessment(),
    ...overrides,
  };
}

export function assessment(overrides: Partial<TreeAssessmentObservation> = {}): TreeAssessmentObservation {
  return {
    site_state: "tree_present",
    vitality: "healthy",
    crown_density: "dense",
    damage: ["none"],
    fungi_on_trunk: false,
    pests: ["none"],
    drought_stress: "none",
    tree_pit: { visible: true, surface: "open_soil", issues: ["none"], watering_bag: false, stakes: false, protection_guard: false },
    phenology: "full_leaf",
    age_class: "mature",
    notes: "",
    ...overrides,
  };
}
