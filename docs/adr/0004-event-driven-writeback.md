# ADR-0004: Event-driven write-back to open data (transactional outbox)

| Field | Value |
|---|---|
| Status | Accepted; **switched off by default since [ADR-0014](0014-open-data-is-read-only-and-data-origin.md)** (`Publishing:Enabled`) |
| Date | 2026-09-25 |
| Decider | Backend developer (hackathon team) |
| Related | [ADR-0001](0001-baumkataster-datenbezug-und-rueckkanal.md) (return channel), [ADR-0003](0003-backend-dotnet.md), [ERD](../data-model/erd.md) |

## Context

The goal of OpenQuest is to keep the city's open data current. We know the moment data changes: when a moderator accepts a
player's contribution. An earlier version of the backend had an admin endpoint "export all accepted changes", i.e. a manual (or, if
automated, time-based) batch. That delays fresh data, needs somebody to remember, and makes the moment of change invisible to
other parts of the system.

The city offers no write API (ADR-0001), so "sending data to open data" means updating a public, stable resource the city links
(a file URL, a GitHub repository).

## Decision

1. **Domain events.** Approving or rejecting a submission publishes events (`AttributeChangeAccepted`, `SubmissionApproved`, `SubmissionRejected`; defined in `OpenQuest.Core`).
2. **Transactional outbox.** Events are inserted into `outbox_message` in the same transaction as the state change, so a change is never accepted without its event, and never announced without being accepted.
3. **No polling.** A database trigger sends `NOTIFY outbox` on insert; PostgreSQL delivers it only when the transaction commits. The `OutboxProcessor` `LISTEN`s and delivers immediately (measured: feed updated ~10 ms after approval). Pending messages are drained on start-up. Time only enters for retries: exponential backoff up to `Outbox:MaxAttempts`, then the message is parked as `dead` (visible at `GET /admin/outbox`, recoverable with `POST /admin/publications/retry`).
4. **Handlers react to events** (`IEventHandler<T>`, batched, idempotent, at-least-once). `PublishAcceptedChangesHandler` pushes accepted changes to every registered `IContributionPublisher`:
   - `StorageContributionPublisher` (always on): writes `changes.geojson` / `changes.csv` (with attribution) to a stable key, served publicly at `/open-data/{dataSource}/changes.geojson|csv`. This is the URL the city links as a resource.
   - `GitHubContributionPublisher` (optional, `Publishing:GitHub:Token`): commits the file to a public repository.
   Afterwards the changes are `exported` and an `export_run` (system-triggered, `created_by` null) is recorded.
5. **Extension without modification.** Reacting to an event means adding one handler registration (for example points and badges on `SubmissionApproved`); a new channel to the city means adding one `IContributionPublisher`.

Not event-driven, on purpose: (a) importing the city's data (done by the separate importer, on a schedule), because the city sends no change notifications, so pulling is the only option; (b) expiring claims by the clock (`ClaimExpiryWorker`), which is time by definition.

## Structure (SOLID)

- Small interfaces per capability (interface segregation): `IContributionPublisher` (write-back); `IBlobWriter` / `IBlobReader` / `IBlobDeleter`; `IQuestClaimService`, `IClaimExpiryService`, `ISubmissionService`, `ISubmissionReviewService`, `IQuestCampaignService`; read models `INearbyQuests`, `IModerationQueue`, ...
- One reason to change per class (single responsibility): claim accounting is `QuestSlotLedger`, photo cleaning `SkiaPhotoProcessor` and de-duplication `DbPhotoDuplicateFinder`.
- Dependencies point to abstractions (dependency inversion): endpoints only know interfaces; `Composition/ServiceRegistration.cs` is the only place that names implementations. Start-up work is a list of `IStartupTask`s.

## Consequences

- Positive: accepted data reaches the open channels within seconds, without operator action; failures are retried and visible; new reactions and channels need no change to existing code.
- Negative: delivery is at-least-once, so handlers and publishers must be idempotent; one more table and a trigger; the GitHub channel needs a token and a repository (off by default).
- The city still has to consume the feed (link it under "Anwendungen", see ADR-0001); pushing into the portal itself remains impossible.
