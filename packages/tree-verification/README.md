# @openquest/tree-verification

Decides whether a player's camera frame shows a tree and whether it is the quest's tree.
Framework free, no city specifics: known trees come in through `NearbyTreeProvider`s (city adapter, OSM, our DB).

```ts
import { resolveConfig, verifyTreePhoto, createOsmTreeProvider } from "@openquest/tree-verification";
import { createMuensterTreeProvider } from "@openquest/adapter-de-muenster";

const result = await verifyTreePhoto(
  {
    image: { data: jpegBytes },                                  // Uint8Array or base64, max 5 MB, resize to ~1024 px first
    expected: { position: asset.geom, genus: asset.attributes.genus },
    playerPosition: { lat, lon, accuracyM },                     // browser geolocation
    capturedAt: new Date(),
    geofenceRadiusM: quest.geofence_radius_m,
  },
  { config: resolveConfig(), providers: [createMuensterTreeProvider(), createOsmTreeProvider()] },
);

result.verdict;         // "approve" | "review" | "reject"
result.reasons;         // [{ code: "genus_mismatch", severity: "soft", detail: "expected Tilia, photo looks like Platanus (98%)" }, ...]
result.genusSuggestion; // for assets without genus ("Baum Amt62"): proposed ATTRIBUTE_CHANGE
```

Never throws for model or network failures; they degrade to `review`. Run it server side only (API key).

## Pipeline

```
frame + player position + expected tree
  1. precheck        magic bytes, size, geofence (Haversine, GPS accuracy tolerance), capture age      free
  2. geo context     providers in parallel, merged and deduped (<2 m), sorted by distance             free
  3. vision          2 models via OpenRouter, strict JSON schema, blind prompt (no expected genus)
                     escalation to a 3rd model on disagreement, uncertainty, failure, genus conflict
  4. Jev             text only decision model over the structured evidence: tree_present, genus,
                     target_match (only with expected genus), verdict; calibrated probabilities
  5. fusion          deterministic rules: hard reason = reject, soft reason = review, else approve.
                     Jev can only make the verdict stricter.
```

Hard reasons: `invalid_image`, `image_too_large`, `outside_geofence`, `no_tree`, `not_a_live_photo` (all models).
Soft reasons: `tree_uncertain`, `models_disagree`, `possibly_not_live_photo`, `poor_image_quality`, `genus_mismatch`,
`possibly_neighbor_tree`, `jev_disagrees`, `gps_inaccurate`, `stale_capture`, `vision_unavailable`, `no_known_tree_nearby`.

## Configuration

`resolveConfig(overrides, env)` reads `OPENROUTER_API_KEY`, `VISION_MODELS`, `VISION_ESCALATION_MODEL`, `JEV_MODEL`
(empty string disables a stage). Thresholds and timeouts live in `src/config.ts`.

| Default | Value |
|---|---|
| Vision | `google/gemini-3.8-flash` + `openai/gpt-6-luna`, escalation `google/gemini-3.5-flash-lite` |
| Jev | `typesafe/jev-1.13` via OpenRouter (`/api/v1/systemone`, same key) |
| Approve / reject tree probability | 0.85 / 0.20 |
| Jev vetoes | `different_tree` ≥ 0.8, `reject` ≥ 0.9 |

Do not send `temperature`: reasoning models reject it together with `provider.require_parameters`, which strict JSON output needs.

## Evaluation

See [docs/adr/0002-tree-photo-verification.md](../../docs/adr/0002-tree-photo-verification.md) for results.

```sh
pnpm eval:fetch   # download the curated Wikimedia Commons samples (eval/samples.json)
pnpm eval         # compare single model, ensemble, ensemble + Jev; vision answers cached in eval/out/
pnpm verify eval/images/tilia-1.jpg --lat 51.9622 --lon 7.6255
```
