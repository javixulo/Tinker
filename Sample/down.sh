#!/usr/bin/env bash
# Stops the sandbox. Pass --reset to also delete the database (data/), to start from zero.
cd "$(dirname "$0")"
DC="docker compose"; $DC version >/dev/null 2>&1 || DC="docker-compose"
$DC down
[ "${1:-}" = "--reset" ] && rm -f data/inventory.db* && echo "Database deleted."
exit 0
