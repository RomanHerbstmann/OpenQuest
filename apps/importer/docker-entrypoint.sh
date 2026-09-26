#!/bin/sh
# Container entrypoint.
#
# Without arguments: wait until the API has set up the database schema (it runs
# the EF Core migrations on start), then sync all configured sources.
# If SYNC_INTERVAL_SECONDS > 0, keep running (`serve`): sync again at that interval and whenever
# an admin requests a sync. SYNC_ON_REQUEST=true does the latter with an interval of 0, too.
#
# With arguments: run that importer command instead, e.g.
#   docker compose run --rm importer sync de-muenster-trees --force
set -eu

if [ "$#" -gt 0 ]; then
    exec openquest-importer "$@"
fi

openquest-importer check --wait "${SCHEMA_WAIT_SECONDS:-600}"

interval="${SYNC_INTERVAL_SECONDS:-0}"
# Also answer sync requests from the admin (POST /admin/sync in the API). On by default when the importer keeps running.
if [ "$interval" -gt 0 ]; then on_request_default=true; else on_request_default=false; fi
on_request="${SYNC_ON_REQUEST:-$on_request_default}"

if [ "$interval" -le 0 ] && [ "$on_request" != "true" ]; then
    exec openquest-importer sync --all
fi

# Syncs every interval (0 = only on request); a failing sync is logged and recorded in sync_run and in the request.
exec openquest-importer serve --interval "$interval"
