# ADR-0003: Tree assessment from player photos (condition, tree pit, phenology, inventory)

| Field | Value |
|---|---|
| Status | Proposed |
| Date | 2026-09-25 |
| Builds on | [ADR-0002](0002-tree-photo-verification.md) |
| Code | [`packages/tree-verification/src/assess`](../../packages/tree-verification/src/assess), [`eval/`](../../eval) |

## Context

The verification pipeline (ADR-0002) already sends every photo to vision models. The same call can describe much more than "tree yes/no": the city data only has position and genus, so condition, tree pit and seasonal state are new, valuable data for Münster. At the same time, hazard assessment is legally the city's job (tree inspections following the FLL guideline), so our output must stay a hint, never a verdict.

## Decision

Extend the existing vision call (no extra model call) with an `assessment` object and derive proposals from it:

| Area | Fields | Output |
|---|---|---|
| 1. Condition and vitality | `vitality` (Roloff-like, 4 levels), `crown_density`, `damage[]`, `fungi_on_trunk`, `pests[]` (oak processionary nests, leaf miner, mistletoe) | attributes `vitality`, `condition`, `damage`, `pests`; `safety_concern` observations |
| 2. Tree pit and watering | `tree_pit.surface`, `issues[]` (litter, parked vehicle, compacted soil), `watering_bag`, `stakes`, `protection_guard`, `drought_stress` | attribute `tree_pit`; observations `tree_pit_issue`, `drought_stress` |
| 3. Phenology | `phenology` (bare, budding, leaf_out, full_leaf, flowering, fruiting, autumn_coloring, leaf_fall) with capture time | observation `phenology` (time series) |
| 5. Inventory check | `site_state` (tree_present, stump, empty_tree_pit), plus geo context | `condition = gone` for missing trees; `new_asset` for trees missing in the inventory |

Plus `age_class` as a cheap by-product.

Rules:

- **Aggregation**: categorical fields are voted across models (ignoring `not_assessable`); ties are broken by Jev, then conservatively (worse vitality wins). List findings carry the share of models reporting them.
- **Hazards** (fungi, oak processionary nests, cavity, crack, leaning, broken branch, root damage, dead or dying, or Jev's `safety_concern` ≥ 0.8) are raised if **one** model reports them. They add the soft reason `safety_concern`: the photo is never auto approved and the hazard is proposed with `requiresReview`. A missed hazard costs more than one extra review.
- **Missing tree**: a stump or empty tree pit at the target, in the geofence and a live photo, turns the hard `no_tree` reject into a soft `tree_missing` report (review, proposal `condition = gone`). Without a target it stays a reject.
- **New tree**: in free photo mode, a tree with no inventory tree within `newTreeRadiusM` (12 m) of the player, and only if every provider answered, becomes a `new_asset` candidate (review).
- Jev gets three more questions (`vitality`, `phenology`, `safety_concern`), used for tie breaks and confidence.
- Nothing is applied automatically: proposals map to `ATTRIBUTE_CHANGE` (see ERD), below `minProposalConfidence` (0.5) they are dropped, hazards are always kept.

**Model change:** the eval showed `gpt-6-luna` called 8 photos "illustration or render", 7 of them real photos, and misses thin young trees. Default vision pair is now `gemini-3.8-flash` + `gemini-3.5-flash-lite`, Luna is the escalation model.

## Evaluation

42 hand checked Wikimedia Commons photos (the 25 from ADR-0002 plus 17 labeled for assessment: bracket fungi, oak processionary nests, horse chestnut leaf miner, mistletoe, watering bags, autumn colors, flowering, a dead tree, a cavity), 61 cases. Run 2026-09-25, `pnpm eval`:

| Metric | Single model | Ensemble | **Full (default)** | Full, Luna pair | Full, Sonnet pair |
|---|---|---|---|---|---|
| Tree detection accuracy | 98 % | 98 % | **98 %** | 95 % | 98 % |
| Clean trees auto approved | 84 % | 84 % | **84 %** | 68 % | 80 % |
| Trees rejected (bad) | 0 % | 0 % | **0 %** | 0 % | 0 % |
| Non-trees approved (bad) | 0 % | 0 % | **0 %** | 0 % | 0 % |
| Wrong genus target not approved | 89 % | 84 % | **100 %** | 95 % | 95 % |
| Genus top-1 | 89 % | 89 % | **89 %** | 84 % | 89 % |
| Hazards flagged and not approved | 100 % (7/7) | 100 % | **100 %** | 100 % | 100 % |
| False hazard alarms | 5 % (1/20) | 5 % | **5 %** | 5 % | 5 % |
| Pest detected | 100 % (6/6) | 100 % | **100 %** | 100 % | 100 % |
| Phenology correct | 100 % (4/4) | 100 % | **100 %** | 100 % | 100 % |
| Watering bag / age class | 100 % (2/2) | 100 % | **100 %** | 100 % | 100 % |
| Missing tree reported | 100 % (2/2) | 50 % | **50 %** | 50 % | 100 % |
| Cost per photo | $0.0069 | $0.0093 | **$0.0094** | $0.0085 | $0.0238 |
| Latency | 8 s | 8 s | **8 s** | 7 s | 7 s |

Notes: the assessment roughly doubled output tokens (cost per photo from ~$0.005 to ~$0.009). The one false hazard alarm is a veteran ash with visible cracks, arguably worth a look anyway. The missed missing tree is a stump with regrowth that one model reads as a small tree. New tree detection is covered by unit tests only (it depends on inventory gaps, not on the photo).

## Consequences

- Every submission now produces structured condition, tree pit and phenology data; phenology creates a time series the city does not have.
- Hazards become a review queue for moderators and, after review, a candidate channel to the city's Mängelmelder. This must be communicated as hints, not as tree inspections.
- Labeled sets per field are tiny (1 to 7 photos). The numbers show the approach works, not its exact accuracy. Next step: collect labeled player photos from Münster and re-run `pnpm eval`.
- Not covered: empty tree pits (no suitable test photo found), litter and parked vehicles in tree pits, drought stress (not labeled yet), species level identification, measurements (planned as a separate `measure` quest type).
