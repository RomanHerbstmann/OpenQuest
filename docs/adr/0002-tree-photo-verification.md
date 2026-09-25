# ADR-0002: Tree photo verification with a vision ensemble, Jev and geo context

| Field | Value |
|---|---|
| Status | Proposed |
| Date | 2026-09-25 |
| Code | [`packages/tree-verification`](../../packages/tree-verification), [`packages/adapters/de-muenster`](../../packages/adapters/de-muenster), [`eval/`](../../eval) |

## Context

Players complete quests such as "photograph tree #4711". Every submission needs a decision: is there a tree in the frame, and is it the quest's tree? Manual review of every photo does not scale, but a wrong auto approval pollutes the data we give back to the city.

Constraints found while building it:

- **Jev** (TypeSafe AI, released 2026-09-15) is a fast, cheap "System 1" decision model with calibrated probabilities, but it accepts **text only**. It is available through OpenRouter (`/api/v1/systemone`, model `typesafe/jev-1.13`) with the same OpenRouter key.
- Vision models on OpenRouter support strict JSON schema output. They must be called without `temperature` when `provider.require_parameters` is set.
- The Münster tree inventory WFS supports BBOX queries (lat,lon axis order for `EPSG::4326`) and has the genus for ~92 % of 43,114 trees. OSM has only ~13k trees in Münster, ~3 % with genus: useful as a fallback, not as ground truth.

## Decision

A five stage pipeline (details in the package README):

1. **Deterministic prechecks** before any paid call: image validity and size, geofence with GPS accuracy tolerance, capture age.
2. **Geo context** from pluggable `NearbyTreeProvider`s (Münster WFS adapter, OSM Overpass with mirror fallback).
3. **Vision ensemble**: two models (`gemini-3.8-flash`, `gpt-6-luna`) with a strict, descriptive schema (subject type, visible parts, authenticity, quality, top 3 genera with evidence). The prompt is **blind**: it never mentions the expected genus, so a genus match is real evidence. A third model is asked only on disagreement, uncertainty, failure or genus conflict.
4. **Jev** reads the structured evidence plus geo context and answers `tree_present`, `genus`, `target_match` and `verdict` with calibrated probabilities.
5. **Rule based fusion** produces `approve` / `review` / `reject` with machine readable reasons. Models provide probabilities, rules decide. Jev can only make a verdict stricter.

City specifics stay in the adapter (CLAUDE.md rule). The result maps onto the ERD: `reasons` and signals go to `SUBMISSION.payload`, `genusSuggestion` becomes an `ATTRIBUTE_CHANGE` for trees without genus (e.g. the 2,832 "Baum Amt62").

## Evaluation

25 hand checked Wikimedia Commons photos (15 trees in 8 genera, many leafless; 10 non-trees: hedges, potted plants, facade, stumps, paintings). Each tree photo is tested against a real Münster tree of the **same** genus and one of a **different** genus (38 cases). Run 2026-09-25, `pnpm eval`:

| Metric | Single model | Ensemble | **Ensemble + Jev** |
|---|---|---|---|
| Tree detection accuracy | 92 % | 96 % | **96 %** |
| Trees auto approved | 87 % | 93 % | 80 % |
| Trees rejected (bad) | 7 % | 0 % | **0 %** |
| Non-trees approved (bad) | 0 % | 0 % | **0 %** |
| Wrong genus target not approved | 69 % | 62 % | **92 %** |
| Genus top-1 | 85 % | 69 % | **92 %** |
| Cost per photo | $0.0048 | $0.0053 | $0.0053 |
| Latency | 8 s | 8 s | 8.5 s |

Findings that shaped the rules:

- Averaging two vision models dilutes genus probabilities, so a fixed genus threshold missed wrong trees. The mismatch rule is relative: clear alternative genus (≥ 0.45) while the expected genus is almost absent (≤ 0.1).
- Jev's `target_match` separates right and wrong targets well (typically 0.85 to 0.99 for wrong ones), its overall `verdict` is overly cautious. Only `target_match` (≥ 0.8) and a strong `reject` (≥ 0.9) may veto.
- Jev adds almost no cost (~$0.00002 per call, ~0.4 s) and lifts genus accuracy by merging the models' candidates.
- A "leafless means skip genus" rule was too blunt: birch and plane stay recognizable in winter; model probabilities already reflect uncertainty.

## Consequences

- About 1 in 5 correct photos still goes to review. Acceptable for a moderated game; thresholds are config, not code.
- The sample set is small and skewed to leafless photos. Before launch: build a Münster specific set from real player photos (leaves on) and re-run `pnpm eval`.
- Not covered yet: photos of screens (no test samples), perceptual hash duplicate detection (`MEDIA.phash`), EXIF stripping (caller's job before storage).
- Depends on OpenRouter and an early access model (Jev). Every stage degrades to `review` on failure, and Jev can be disabled (`JEV_MODEL=`).
