# ADR-0009: Tree cards and their rarity depend on the tree's district

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-09-26 |
| Decider | Backend developer (hackathon team) |
| Related | [ADR-0004](0004-event-driven-writeback.md) (events), [ADR-0007](0007-cities-and-districts-drawn-by-admins.md) (districts), [ERD](../data-model/erd.md) |

## Context

Players should collect cards for the trees they photograph. Cards come in rarities. A tree that is everywhere in a district (in Münster a lime tree
is every fourth tree) should mostly give ordinary cards; a scarce tree should have a much better chance of a rare one, and findings that improve the data
(a genus that was unknown, a problem with the tree) should be rewarded, too. The rule has to work for any city and be adjustable.

## Decision

- **A card per approved submission**, handed out by an event handler on `SubmissionApproved` (like the points). The card shows the genus of the tree; for a
  genus quest it is the genus the player found out. No genus known and none found out: no card. Collecting is per genus, the tree book lists genera.
- **Rarity is rolled from how frequent the genus is in the tree's district.** The share of the genus among the district's trees with a known genus
  is put into one of four classes (abundant, common, scarce, very scarce; a genus that does not grow there is very scarce). Each class has fixed chances for
  common, uncommon, rare and legendary cards (`RarityProfile`). New information (an unknown or different genus) and reported problems (damaged, dead,
  gone) each move the class one step towards scarce.
- **The dice roll is derived from the submission's id**, so it is reproducible: a redelivered event gives the same card, a test can predict it. One card per
  submission (unique index) makes the handler idempotent.
- **Genus statistics are counted from the assets inside the district's outline** (PostGIS `ST_Covers`) and stored per district. They are recalculated
  when missing, when the district was redrawn or changed, and when older than a day, because the importer updates assets independently. Districts with too few trees
  of known genus have no reliable statistics; nothing counts as scarce there.
- **The numbers live in the core** (`RarityProfile`, `CardRarity`, pure and unit-tested) and can be overridden per deployment by configuration
  (`Gamification:Rarity`), so a city can tune how generous the game is without code changes.
- Trees outside every district still give cards, with the ordinary chances.

## Consequences

- Every district gives its own scarcity: a Ginkgo is a rare find in a district of lime trees and unremarkable in a park district.
- Redrawing districts changes the statistics of later cards, not of cards already handed out (a card stores the class and share it was rolled with).
- The frontend needs names and art for genera; the API delivers the Latin genus only.
- Legendary cards are only possible for scarce genera or with boosts; with the default numbers an ordinary tree never gives one.
- Later parts (new-tree reports, seasonal or weekly quests) can add reasons and boosts without changing how cards are stored.
