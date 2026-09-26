# ADR-0012: Badges

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-09-26 |
| Decider | Backend developer (hackathon team) |
| Related | [ADR-0004](0004-event-driven-writeback.md) (events), [ADR-0009](0009-tree-cards-and-rarity.md) (cards), [ADR-0011](0011-new-tree-reports-and-automatic-review.md) (new trees), [ERD](../data-model/erd.md) |

## Context

Points, levels, cards and the leaderboard reward what players earn on the way. Badges are the milestones on top of that ("first quest", "five condition reports", "a rare card"). The ERD already had
`BADGE` and `USER_BADGE`. A city or a deployment should be able to add its own without a code change, and awarding must survive redelivered events and concurrent evaluation.

## Decision

- **A badge is data with criteria.** `badge.criteria` is JSON: `{"type": ..., "count": n}` plus `rarity` or `taskType` where the type needs one. Types: `approved_submissions`, `task_type`, `points`, `cards`,
  `distinct_genera`, `rarity_cards` (at least the given rarity: a legendary card counts for "rare"), `new_trees`. The meaning of a criteria and its validation are **pure rules in the core**
  (`BadgeRules`, unit-tested); the API only calculates what a player has done (`UserStats`) and feeds it to them. A criteria that is invalid is never met.
- **Ten default badges** are in the core (`BadgeCatalog`) and inserted at start-up when missing (by key). What an admin changed or deactivated is not touched. `name` and `description` are translation keys
  (`badge.first_steps`), like task and asset types; badges an admin makes up carry plain text. The defaults pay **no bonus points**, so leaderboards and levels are not skewed by them.
- **Evaluation is event-driven.** A handler on `SubmissionApproved` (registered after the handlers that pay points and hand out cards, which the badges count) evaluates the player. When an admin adds a badge, changes
  its criteria or switches it on, everyone who has reached it gets it at once (a loop over all players; fine at this size, the natural next step is a query per criteria type).
- **One award per player and badge:** the primary key of `user_badge` and `INSERT ... ON CONFLICT DO NOTHING`. The bonus points (if the badge has any) and the `BadgeAwarded` event belong to the award that got in and
  are written in the same transaction, so two evaluations at the same time, or a redelivered event, cannot pay twice. A bonus can lift the player over another badge's threshold, so the evaluation repeats
  (at most four rounds) until nothing changes.
- **Bonus points are ledger lines** (`reason = badge_reward`, no district): they count for the player's total and level, not for a district's leaderboard.
- **Nothing is taken away.** A later change (a rejected submission does not exist any more, an admin raises a criteria, a badge is deactivated) does not revoke a badge; an inactive badge is hidden from players.
- **Players see progress:** `GET /me/badges` lists every active badge with `earned`, `awardedAt` and `current / required / percent`, earned first, then the closest. `BadgeAwarded` is the hook for a notification
  (there is no push channel yet).

## Consequences

- A city can have its own badges ("walked the Aasee", once there are criteria for it) without code; a new criteria *type* needs a case in `BadgeRules` and a statistic in `BadgeService.StatsAsync`.
- Every approval costs a handful of small count queries for the player. Acceptable now; if it shows up, cache the stats per player or evaluate only badges whose type the approval can change.
- Badges with bonus points reward retroactively when created (everyone who qualifies gets the bonus). Admins should know that before adding one.
- Badges are not per city yet; district- or city-specific criteria (for example points in one district) are a later extension of the criteria types.
