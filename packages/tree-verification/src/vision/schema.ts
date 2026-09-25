/**
 * What every vision model must return for a frame. Kept deliberately descriptive
 * (visible parts, subject type, authenticity) so the decision stage can reason about
 * *why* a model thinks there is a tree, not just trust a boolean.
 */
export interface VisionObservation {
  model: string;
  tree_present: "yes" | "no" | "uncertain";
  tree_present_confidence: number;
  main_subject:
    | "single_tree"
    | "multiple_trees"
    | "tree_row_or_avenue"
    | "woodland"
    | "shrub_or_hedge"
    | "small_or_potted_plant"
    | "tree_stump"
    | "other_vegetation"
    | "no_vegetation";
  tree_count: number;
  main_tree_frame_share: "none" | "small" | "medium" | "large";
  visible_parts: {
    trunk: boolean;
    bark_closeup: boolean;
    leaves_closeup: boolean;
    canopy: boolean;
    flowers: boolean;
    fruits_or_seeds: boolean;
    bare_branches: boolean;
  };
  leafless: boolean;
  genus_candidates: { genus: string; probability: number; evidence: string }[];
  photo_authenticity: "live_camera_photo" | "photo_of_screen" | "photo_of_print" | "illustration_or_render" | "uncertain";
  image_quality: "good" | "acceptable" | "poor";
  quality_issues: ("blurry" | "too_dark" | "overexposed" | "obstructed" | "too_far" | "too_close" | "none")[];
  scene_description: string;
  /** Optional so a model that omits it degrades to "no assessment" instead of failing the frame. */
  assessment?: TreeAssessmentObservation;
}

export const VITALITY = ["healthy", "slightly_damaged", "clearly_damaged", "severely_damaged_or_dead", "not_assessable"] as const;
export const SITE_STATE = ["tree_present", "stump", "empty_tree_pit", "unclear"] as const;
export const CROWN = ["dense", "normal", "sparse", "not_visible"] as const;
export const DAMAGE = ["bark_wound", "cavity", "crack", "leaning", "broken_branch", "dead_branches", "root_damage", "none"] as const;
export const PESTS = ["oak_processionary_nests", "leaf_miner_damage", "mistletoe", "other_pest", "none"] as const;
export const DROUGHT = ["none", "mild", "severe", "not_assessable"] as const;
export const PIT_SURFACE = ["open_soil", "planted", "mulched", "sealed", "grate", "not_visible"] as const;
export const PIT_ISSUES = ["litter", "parked_vehicle", "compacted_soil", "none"] as const;
export const PHENOLOGY = ["bare", "budding", "leaf_out", "full_leaf", "flowering", "fruiting", "autumn_coloring", "leaf_fall", "not_assessable"] as const;
export const AGE_CLASS = ["young", "semi_mature", "mature", "veteran", "not_assessable"] as const;

/** Condition, tree pit, phenology and site state as seen by one vision model. */
export interface TreeAssessmentObservation {
  site_state: (typeof SITE_STATE)[number];
  vitality: (typeof VITALITY)[number];
  crown_density: (typeof CROWN)[number];
  damage: (typeof DAMAGE)[number][];
  fungi_on_trunk: boolean;
  pests: (typeof PESTS)[number][];
  drought_stress: (typeof DROUGHT)[number];
  tree_pit: {
    visible: boolean;
    surface: (typeof PIT_SURFACE)[number];
    issues: (typeof PIT_ISSUES)[number][];
    watering_bag: boolean;
    stakes: boolean;
    protection_guard: boolean;
  };
  phenology: (typeof PHENOLOGY)[number];
  age_class: (typeof AGE_CLASS)[number];
  notes: string;
}

const bool = { type: "boolean" } as const;
const prob = { type: "number", minimum: 0, maximum: 1 } as const;
const enumOf = (...values: string[]) => ({ type: "string", enum: values });

export const VISION_JSON_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: [
    "tree_present",
    "tree_present_confidence",
    "main_subject",
    "tree_count",
    "main_tree_frame_share",
    "visible_parts",
    "leafless",
    "genus_candidates",
    "photo_authenticity",
    "image_quality",
    "quality_issues",
    "scene_description",
    "assessment",
  ],
  properties: {
    tree_present: enumOf("yes", "no", "uncertain"),
    tree_present_confidence: prob,
    main_subject: enumOf(
      "single_tree",
      "multiple_trees",
      "tree_row_or_avenue",
      "woodland",
      "shrub_or_hedge",
      "small_or_potted_plant",
      "tree_stump",
      "other_vegetation",
      "no_vegetation",
    ),
    tree_count: { type: "integer", minimum: 0 },
    main_tree_frame_share: enumOf("none", "small", "medium", "large"),
    visible_parts: {
      type: "object",
      additionalProperties: false,
      required: ["trunk", "bark_closeup", "leaves_closeup", "canopy", "flowers", "fruits_or_seeds", "bare_branches"],
      properties: {
        trunk: bool,
        bark_closeup: bool,
        leaves_closeup: bool,
        canopy: bool,
        flowers: bool,
        fruits_or_seeds: bool,
        bare_branches: bool,
      },
    },
    leafless: bool,
    genus_candidates: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        required: ["genus", "probability", "evidence"],
        properties: { genus: { type: "string" }, probability: prob, evidence: { type: "string" } },
      },
    },
    photo_authenticity: enumOf("live_camera_photo", "photo_of_screen", "photo_of_print", "illustration_or_render", "uncertain"),
    image_quality: enumOf("good", "acceptable", "poor"),
    quality_issues: {
      type: "array",
      items: enumOf("blurry", "too_dark", "overexposed", "obstructed", "too_far", "too_close", "none"),
    },
    scene_description: { type: "string" },
    assessment: {
      type: "object",
      additionalProperties: false,
      required: ["site_state", "vitality", "crown_density", "damage", "fungi_on_trunk", "pests", "drought_stress", "tree_pit", "phenology", "age_class", "notes"],
      properties: {
        site_state: enumOf(...SITE_STATE),
        vitality: enumOf(...VITALITY),
        crown_density: enumOf(...CROWN),
        damage: { type: "array", items: enumOf(...DAMAGE) },
        fungi_on_trunk: bool,
        pests: { type: "array", items: enumOf(...PESTS) },
        drought_stress: enumOf(...DROUGHT),
        tree_pit: {
          type: "object",
          additionalProperties: false,
          required: ["visible", "surface", "issues", "watering_bag", "stakes", "protection_guard"],
          properties: {
            visible: bool,
            surface: enumOf(...PIT_SURFACE),
            issues: { type: "array", items: enumOf(...PIT_ISSUES) },
            watering_bag: bool,
            stakes: bool,
            protection_guard: bool,
          },
        },
        phenology: enumOf(...PHENOLOGY),
        age_class: enumOf(...AGE_CLASS),
        notes: { type: "string" },
      },
    },
  },
} as const;

/** Common Central European urban tree genera. Only a hint for the model, not a whitelist. */
export const COMMON_GENERA = [
  "Acer", "Aesculus", "Alnus", "Betula", "Carpinus", "Castanea", "Catalpa", "Corylus", "Crataegus", "Fagus",
  "Fraxinus", "Ginkgo", "Gleditsia", "Juglans", "Larix", "Liquidambar", "Liriodendron", "Magnolia", "Malus",
  "Metasequoia", "Paulownia", "Picea", "Pinus", "Platanus", "Populus", "Prunus", "Pyrus", "Quercus", "Robinia",
  "Salix", "Sophora", "Sorbus", "Taxus", "Tilia", "Ulmus", "Zelkova",
];

/**
 * The prompt is deliberately blind: it never mentions the genus the city expects.
 * Otherwise models tend to echo the hint and the genus check would prove nothing.
 * Local priors are applied later in the decision stage.
 */
export function buildVisionPrompt(): string {
  return `You verify photos for a civic game in which players photograph municipal street and park trees.

Analyse the image and fill the JSON schema. Rules:
- "tree_present" = yes only if a real, rooted, woody tree (trunk plus crown, or a clear close-up of a tree's trunk/bark/leaves) is a main subject. Shrubs, hedges, potted or small ornamental plants, a lone stump, or trees that are only a tiny background element do not count: answer no.
- Use "uncertain" rather than guessing. "tree_present_confidence" is your probability (0..1) that your tree_present answer is correct.
- "genus_candidates": up to 3 Latin genera (e.g. Tilia, Quercus, Acer), most likely first, probabilities summing to at most 1. Base them only on visible features (leaf shape and margin, bark texture, flowers, fruits, growth form) and name the feature in "evidence". Empty array if no tree.
- "photo_authenticity": detect photos of a monitor or phone screen (moire, pixel grid, bezels, glare), prints or posters, and illustrations or renders. Players may try to cheat this way.
- "leafless" is true for winter trees without foliage; genus is harder then, lower the probabilities.
- "scene_description": one or two plain English sentences describing the scene and the main tree.

"assessment" describes the main tree (or the spot where it should stand). Report only what is visible; prefer "not_assessable", "not_visible" or empty lists over guessing:
- "site_state": "stump" for a cut stump, "empty_tree_pit" for a street tree pit or planting spot without a tree, "tree_present" otherwise, "unclear" if you cannot tell.
- "vitality" (after Roloff): healthy = full, even crown; slightly_damaged = somewhat thin crown or some dead twigs; clearly_damaged = sparse crown, dead branches, larger wounds; severely_damaged_or_dead = mostly dead or dying. Use "not_assessable" for leafless trees in winter unless dead wood is obvious, and for bark or leaf close-ups.
- "damage", "fungi_on_trunk" (bracket fungi or other fruiting bodies on trunk or root collar), "pests" (oak processionary nests are white silky webs on oak trunks and branches; leaf_miner_damage = brown blotched leaves, typical on horse chestnut). Use ["none"] when you looked and saw nothing.
- "drought_stress": wilted, curled, yellowed or prematurely browning foliage in summer.
- "tree_pit": the ground area around the trunk. "watering_bag" = green or other watering sack around the trunk, "stakes" = support stakes, "protection_guard" = metal guard or bollards.
- "phenology": the current seasonal stage of the foliage, flowers or fruit.
- "age_class": young = recently planted, thin trunk, often staked; veteran = very old, massive trunk, hollows.
- "notes": one short sentence with the most relevant finding for a city tree inspector, or "".
Common urban genera for orientation: ${COMMON_GENERA.join(", ")}.`;
}
