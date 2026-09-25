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
Common urban genera for orientation: ${COMMON_GENERA.join(", ")}.`;
}
