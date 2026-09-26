# ADR-0014: Open data is read-only for us, and every value knows its origin

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-09-26 |
| Decider | Backend developer (hackathon team) |
| Related | [ADR-0001](0001-baumkataster-datenbezug-und-rueckkanal.md) (return channel), [ADR-0004](0004-event-driven-writeback.md) (event-driven write-back, now off by default), [ADR-0006](0006-eigene-asset-id-und-raeumliches-matching.md), [ADR-0011](0011-new-tree-reports-and-automatic-review.md), [ERD](../data-model/erd.md) |

## Context

ADR-0001 and ADR-0004 planned a return channel: the moment a moderator accepts a player's change, it is published (a public feed, a GitHub file) so that the city can adopt it. The city does not want its data
updated this way. So nothing is sent to open data, and our own database has to keep what players found out. That makes it important to tell the two kinds of data apart: what the city delivered, and what came from players.

## Decision

**Publishing to open data is off by default** (`Publishing:Enabled=false`).
- The handler that pushes accepted changes to the publishers (feed, GitHub) does nothing while it is off. Changes and reported trees stay `accepted`; the events are still consumed (points, cards, badges work as before).
  Nothing is lost: once someone switches publishing on, `POST /admin/publications/retry` publishes everything that was accepted meanwhile. While it is off, that endpoint answers `409 publishing_disabled`.
- `GET /admin/publications/status` shows whether it is on, how many changes are kept (`acceptedNotPublished`) and how many were sent (`published`).
- The code (outbox, publishers, feed, GitHub) stays. It is a switch, not a removal: another city, or this one later, may want the return channel.

**The two kinds of data are stored apart and never mixed.**
| | Stored in | Written by | Origin |
|---|---|---|---|
| The city's data | `asset.attributes` | the importer only; replaced by every sync | `open_data` |
| Accepted player changes | `attribute_change` (`accepted`, `exported`) with old value, new value, submission | approval of a submission | `user` |
| Trees players reported as missing | `asset_proposal` (`accepted`, `exported`) | approval of a submission | `user` |

`asset.attributes` never contains player data, so a sync can replace it without destroying contributions, and contributions cannot pass for the city's data.

**The game shows the merged view, with the origin per value.** `AttributeProvenance` (core, pure, tested) decides which value an attribute shows: the latest accepted contribution, unless the city has the same value now
(then it is open data) or has changed the attribute after the contribution (then the city's newer value wins and the contribution is marked `outdated`). The API says it:
- `GET /assets/{id}`: the city's `attributes`, the `source` (data source, license, attribution), the `contributions` and the `effective` value per attribute with `origin` = `open_data` or `user`.
- `GET /assets/nearby` and `GET /quests/nearby`: each asset has `origin` (always `open_data`), its `dataSource` and its `contributions`, so a map can show a player's value differently.
- `GET /assets/reported`: the trees players reported and a moderator accepted, `origin: "user"`. They are not assets and get no quests (assets belong to the importer).

## Consequences

- What players contribute is only visible in our game, not in the city's data, until the city (or a person) decides to adopt it. The public feed keeps its old content but is not updated.
- Clients can (and should) mark user values, for example "confirmed by players" next to a genus, and must not present them as the city's data. The attribution of the source still applies to `attributes`.
- A contribution that the city later confirms simply becomes open data in the view; one that the city contradicts is shown as outdated. Nothing is deleted.
- Moderation is now the only quality gate for user data that the game shows, since there is no second check by the city; the automatic review (ADR-0011) approves only on a clear verdict.
