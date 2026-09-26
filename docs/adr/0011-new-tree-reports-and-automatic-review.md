# ADR-0011: Reports of missing trees, reported problems, and the automatic review of photos

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-09-26 |
| Decider | Backend developer (hackathon team) |
| Related | [ADR-0002](0002-tree-photo-verification.md) (photo verification), [ADR-0004](0004-event-driven-writeback.md) (events, write-back), [ADR-0006](0006-eigene-asset-id-und-raeumliches-matching.md) (assets belong to the importer), [ADR-0007](0007-cities-and-districts-drawn-by-admins.md), [ADR-0010](0010-recurring-quests-and-sync-events.md), [ERD](../data-model/erd.md) |

## Context

The city's tree data is incomplete (in Münster about half of the trees are missing) and its condition data is thin. Players should be able to tell us about trees that are not in
the data, and to say what is wrong with a tree, not only whether it is "damaged". And moderators cannot look at every photo: an automatic check that is sure enough
should take the obvious cases off their desk.

## Decision

**Problems on a tree.** A `condition_report` may carry `issues`, a list of known codes (`root_lift`, `trunk_damage`, `dead_branches`, `crown_damage`, `fungus`, `cavity`,
`leaning`, `pests`, `vandalism`; `TaskTypes.IssueCodes`). It proposes a second attribute change, `issues`, next to `condition`, and both are published like any accepted change.
A reported issue counts as a fact worth a boost for the card, like a bad condition.

**Missing trees are proposals, not assets.** Assets belong to the importer (ADR-0006); the API never creates one.
- A new task type `report_new_tree`. A quest of this type has **no asset but a district** (`quest.district_id`, one of the two is required): the player has to stand inside the
  district. Admins create it with `target.districtId` or `target.cityId` (one quest per active district); `taskConfig.dataSource` names the data set the tree is meant for;
  `maxCompletions` is how many trees can be reported there. Because of the schedules of ADR-0010 this can be a weekly quest, too.
- Submitting needs a photo and a position inside the district. The report is stored as `asset_proposal` (position of the player, genus, species, note, photo). It is refused when
  the data, or another open report, already has a tree within `Game:NewTreeMinDistanceMeters` (5 m): `tree_already_known`.
- Approving accepts the proposal and publishes it **with the other accepted changes** through the existing publishers: a feature at the reported position with
  `attribute = "new_tree"`, an empty `external_id`, no old value and `{genus, species, note, photo_url}` as new value. Approved proposals are marked `exported`. The city (or the importer, once
  the city adopts the data) decides what becomes an asset; the player does not see the tree in the game before that.
- The points of such a quest go to the quest's district. There is no card: with no tree in the data there is nothing to compare the genus statistics with.
- Contract change for clients: `QuestDto.asset` and `AdminSubmissionDto.asset` can be null for these quests and carry `area` (the district) instead. `GET /quests/nearby` only returns
  quests with an asset; `GET /quests/areas?lat&lon` returns the district quests of the districts the position is in.

**The automatic review.** A submission with a photo publishes a `SubmissionSubmitted` event. A handler asks an `ISubmissionAutoReviewer` (a port in the core); the implementation
sends the photo, the player's position and the expected tree to the web app's photo verification (`POST /api/verify`, ADR-0002) and takes over its verdict.
- Only a clear **approve** acts: the handler approves the submission through the same review service a moderator uses, so points, cards and publication follow as always;
  `reviewed_by` stays empty. `review` and `reject` leave the submission pending. **Nothing is ever rejected automatically**, a wrong "no" costs a player more than a wrong "yes" costs a moderator.
- The verdict, the reasons and the checker's full answer are stored with the submission (`auto_review`) and shown to the moderator next to the photo.
- Off by default (`AutoReview:Enabled`, `AutoReview:VerifyUrl`). It only looks at task types listed in `AutoReview:TaskTypes` (default `photo`): a photo of the tree is what the check can vouch
  for. A reported new tree changes the city's data, so it stays with the moderators unless someone adds it to the list.
- Failure handling: when the service cannot check (not configured, request refused) the answer is "review". Transient errors (network, 429, 5xx) are retried by the outbox with backoff;
  when they persist the message dies and the submission simply stays for a moderator.
- Idempotent: a submission that was checked is not checked again; if a moderator decided meanwhile, nothing happens (the review service reads the submission afresh).

## Consequences

- The game can grow the city's data without touching what belongs to the importer; the feed of accepted changes gets a second kind of entry that consumers have to know (`new_tree`).
- Clients must handle quests without asset. The frontend has to call `/quests/areas` to show them.
- The web app's `/api/verify` is rate limited per IP (10 per minute) and every call costs vision-model credits. With many photos the API sees 429 and retries; a shared secret or a higher limit
  for the API's address would be the next step, and a trust rule (auto-approve only for players with approved history) is worth considering before switching it on in production.
- The check is as good as the verification pipeline. Photographing the same tree twice is caught by the perceptual hash of the photos; a photo of another tree taken earlier is only caught
  by the position and capture-time checks of the pipeline, and the capture time is usually unknown (the app strips EXIF), so the position of the player is what counts.
