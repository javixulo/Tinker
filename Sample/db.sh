#!/usr/bin/env bash
# Runs SQL on the sandbox database:   ./db.sh "SELECT * FROM devices"
cd "$(dirname "$0")"
sqlite3 -header -column data/inventory.db "$1"
