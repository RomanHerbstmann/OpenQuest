# ADR-0010: Recurring quests for stale data, and sync events

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-09-26 |
| Decider | Backend developer (hackathon team) |
| Related | [ADR-0004](0004-event-driven-writeback.md) (outbox and events), [ADR-0006](0006-eigene-asset-id-und-raeumliches-matching.md) (importer), [ADR-0007](0007-cities-and-districts-drawn-by-admins.md) (cities, time zones), [ADR-0009](0009-tree-cards-and-rarity.md) (cards), [ERD](../data-model/erd.md) |

## Context

City data gets old: a tree that nobody looked at for years may be gone or changed. We want quests for exactly these assets, and we want them to come back on a
rhythm (for example every Sunday, with a bonus) so that players have a reason to return. Independently, everything that depends on the imported assets
(statistics, later recurring quests) needs to know when a sync finished, without polling the database.

## Decision

- **What players verified when is a projection of the approved submissions** (`asset_activity`: `last_verified_at`, `verification_count`). A handler on
  `SubmissionApproved` recalculates the affected assets from the submissions instead of counting up, so a redelivered event changes nothing. The table is filled from
  the existing submissions by the migration. An asset without a row was never verified. "Stale" means *not verified by a player*; the importer's own change history
  says nothing about whether the data is true.
- **Quests can target staleness and places:** `target.notVerifiedForDays`, `target.districtId`, `target.cityId` next to the existing selectors. With
  `notVerifiedForDays` the assets are taken never-verified first, then the longest unchecked. A quest whose `endsAt` has passed no longer blocks a new quest of the
  same kind for the asset (otherwise a recurring quest could never return to it).
- **Weekly schedules are templates in the database** (`quest_schedule`: city, weekday, time of day, duration, task, reward = the bonus, target). The weekday and time
  are meant in the **city's time zone**; the week is the ISO week (Monday to Sunday). The rule is a pure function in the core (`WeeklySchedule`, unit-tested including
  daylight saving time and the year boundary). Quests are created through the same `IQuestCampaignService` as by hand, so all rules apply the same way.
- **A run happens once per schedule and period.** `quest_schedule_run` has the primary key (schedule, period); the run inserts its row first, in the same
  transaction as the quests, and only the one that got the row creates the quests. A restart, a second API instance or a repeated tick finds the row and does nothing.
  A moment that has been reached stays due for the rest of its week (catch-up after downtime) unless the quests' window is already over. A run that cannot create quests
  is recorded with its error and not retried in that period.
- **The worker only looks at the clock** (`QuestScheduleWorker`, every `Gamification:QuestScheduleIntervalSeconds`, 60): "Sunday 08:00" is a moment, not an
  event, so this is the one place where a timer is right. Everything else stays event-driven.
- **A finished sync is an event.** After committing a run, the importer sends `pg_notify('sync_finished', run_id)` in the same transaction. The API listens
  (`SyncFinishedListener`, like the outbox processor) and puts an `AssetSyncCompleted` event into the outbox, which handlers consume. Because a notification is lost while
  the API is down, the listener announces the runs of the last two days that have no event yet after connecting; a run that is announced twice is skipped. First consumer:
  the genus statistics of all districts are marked as out of date (recalculated when next needed) instead of only aging out after 24 hours.

## Consequences

- Admins define recurring quests in the panel (`/admin/quest-schedules`) instead of the code knowing about "Sunday quests"; a city can run its own rhythm and time zone.
- A schedule is validated when saved by creating its quests once and rolling back, so mistakes show up at once and not on Sunday morning.
- The selection is deterministic, so trees that nobody does stay at the front in later weeks. Rotating (for example by the last time a quest was made for the tree)
  is a possible refinement.
- `pg_notify` is at-most-once and not durable; the catch-up window covers restarts, not a database that was unreachable for longer. Consumers that must not miss a sync
  should tolerate a recalculation (all current ones do).
- Not covered: sync events for report and reading feeds carry no asset changes; the handlers ignore runs without changes.
