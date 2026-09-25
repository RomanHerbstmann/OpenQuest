# ADR-0003: Backend in .NET 10 (ASP.NET Core)

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-09-25 |
| Decider | Backend developer (hackathon team) |
| Related | [CLAUDE.md](../../CLAUDE.md), [ERD](../data-model/erd.md), [ADR-0001](0001-baumkataster-datenbezug-und-rueckkanal.md) |

## Context

`CLAUDE.md` proposed TypeScript end-to-end (Node.js backend) but marked the stack as "not final". The person building the
backend at the hackathon chose .NET 10. The rules of the architecture do not depend on the language: adapter boundary,
framework-free domain logic, PostGIS, S3-compatible storage, race-safe claims.

## Decision

- **Backend:** ASP.NET Core 10 Minimal API, EF Core 10 + Npgsql + NetTopologySuite on **PostgreSQL/PostGIS**.
- **Layout** (the `packages/` and `apps/` names of the proposal are kept):
  - `packages/core/OpenQuest.Core`: domain model, adapter interface, quest/claim/geofence rules, exporters. No framework references.
  - `packages/adapters/de-muenster/OpenQuest.Adapters.Muenster`: the only place that knows Münster (WFS URL, CRS, field names, cleaning rules).
  - `apps/api/OpenQuest.Api`: HTTP API, persistence, auth, background jobs.
  - `tests/*`: unit tests for core and adapter (offline, with fixtures), integration tests for the API against a real PostGIS.
- **Data model:** exactly the [ERD](../data-model/erd.md) (generic assets with JSONB attributes, JSON Schema per asset type and task type).
- **Auth:** username + password only, argon2id, JWT bearer tokens, one-time recovery codes instead of e-mail reset. Roles `player`, `moderator`, `admin`.
- **Photos:** decoded and re-encoded with SkiaSharp (drops EXIF/GPS, applies orientation, max 2048 px), perceptual hash for duplicates, stored in S3/MinIO.
  SkiaSharp (MIT) was chosen over ImageSharp because ImageSharp 3+/4 is under a commercial split license, which is a risk for an open-source project whose license is not chosen yet.
- **Events and write-back:** see [ADR-0004](0004-event-driven-writeback.md).
- **Claims:** `quest.slots_taken` is changed only while holding `SELECT ... FOR UPDATE` on the quest row; expiry is done lazily on claim and by a background job.
- **Sync:** the source has no ids, so `external_id` is a hash of the coordinate only (EPSG:25832, 0.1 m). Trees that moved slightly are re-matched by proximity (1 m).
  Deviation from ADR-0001, which also hashed street key and genus: a genus corrected by the city must not turn a tree into a "new" tree and orphan its quests.

## Consequences

- Positive: typed, fast, one deployable; integration tests run against real PostGIS, so the locking and geo queries are verified, not mocked.
- Negative: `CLAUDE.md` is no longer accurate for the backend (updated); frontend developers work against the OpenAPI spec (`/openapi/v1.json`) instead of shared TypeScript types.
