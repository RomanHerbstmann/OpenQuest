#!/bin/sh
# Container entrypoint.
#
# Without arguments: apply migrations, then sync all configured sources.
# If SYNC_INTERVAL_SECONDS > 0, keep running and sync again at that interval.
#
# With arguments: run that importer command instead, e.g.
#   docker compose run --rm importer sync de-muenster-trees --force
set -eu

if [ "$#" -gt 0 ]; then
    exec openquest-importer "$@"
fi

openquest-importer migrate

interval="${SYNC_INTERVAL_SECONDS:-0}"
if [ "$interval" -le 0 ]; then
    exec openquest-importer sync --all
fi

while true; do
    # A failed sync is logged and recorded in sync_run; try again next interval.
    openquest-importer sync --all || echo "Sync failed, retrying in ${interval}s" >&2
    sleep "$interval"
done
