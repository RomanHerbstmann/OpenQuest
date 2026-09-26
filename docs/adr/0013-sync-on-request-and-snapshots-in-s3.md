# ADR-0013: Sync on request, and the importer's snapshots in S3

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-09-26 |
| Decider | Backend developer (hackathon team) |
| Related | [ADR-0001](0001-baumkataster-datenbezug-und-rueckkanal.md), [ADR-0006](0006-eigene-asset-id-und-raeumliches-matching.md) (importer), [ADR-0010](0010-recurring-quests-and-sync-events.md) (`sync_finished`), [ERD](../data-model/erd.md) |

## Context

The importer syncs on start and once a day. Admins want two things: sync now (a source changed, a failed run needs another try with `force`) and look at the raw download of a run (what did the city really deliver?).
The API must not import anything itself (ADR-0003), and the importer is a separate Python process that may run elsewhere.

## Decision

**Sync on request.**
- The API only records the wish: `POST /admin/sync` inserts a `sync_request` per source (or `*` for all enabled ones) with the flags `force` and `accept_schema_change`, and sends `pg_notify('sync_requested', id)` in the
  same transaction. The database is the queue; there is no HTTP call from the API to the importer and no new port.
- The importer's `serve` command listens, claims the oldest pending request (`UPDATE ... FOR UPDATE SKIP LOCKED`, so several importers cannot run the same one), runs the sync with the same code as the CLI, and writes the
  outcome back: `status`, the `sync_run_id` (when it was one source) and the `error`. Requests that were running when the importer stopped are marked failed at start.
- One pending or running request per source key (partial unique index, `409 already_requested`). Nothing polls: the wait for a notification is also the timer of the scheduled sync, capped at five minutes so that a
  lost notification only delays a request. `serve` replaces the shell loop of the entrypoint, which had no way to be woken.
- `force` and `acceptSchemaChange` are admin decisions on purpose: the removal guard and the schema check exist to stop a broken download from wiping or corrupting the data, so the request says so explicitly, and the
  failed run (and the error in the request) is what the admin looks at first.

**Snapshots in S3.**
- The importer can keep its raw downloads in an S3-compatible bucket (`[snapshots] backend = "s3"`, MinIO in the compose setup) next to the local directory. Both stores use the same content-addressed keys, so
  `sync_run.snapshot_key` means the same in both; the S3 client is boto3, an optional extra.
- The API reads the same bucket (`Storage:SnapshotBucket`) through its own read-only `ISnapshotReader` and serves `GET /admin/sync/runs/{id}/snapshot`: the file as stored, or, for a run with reference files, a zip with the
  main file and the extras (built in memory). With the local directory the API cannot see the files and answers `404 snapshot_unavailable` with the reason.
- The bucket is a separate one from the photos so that photos and raw data can have different retention and access.

## Consequences

- Admins can re-run a failed sync and download what it saw without shell access. The importer has to run in `serve` mode to answer; without it requests stay pending (visible in `GET /admin/sync/requests`).
- The S3 credentials of the importer and the API are configured twice (importer environment, `Storage__*`); the compose setup has them side by side.
- The zip is built in memory: fine for the Münster data (tens of MB), to be streamed if a source gets much bigger.
- Nobody cleans old snapshots yet; a lifecycle rule on the bucket is the plan when it matters.
