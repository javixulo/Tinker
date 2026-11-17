#!/usr/bin/env bash
# Calls register_device with the JSON of the `device` you give and prints the result.
#   ./call.sh '{"brand":"Shelly","model":"Dimmer 2","manuals":["shelly/dimmer_2_guide.pdf"]}'
# The URL can be changed with TINKER_URL (default http://localhost:8080/mcp).
set -euo pipefail
URL="${TINKER_URL:-http://localhost:8080/mcp}"
BODY=$(jq -nc --argjson d "${1:-null}" '{jsonrpc:"2.0",id:1,method:"tools/call",params:{name:"register_device",arguments:(if $d == null then {} else {device:$d} end)}}')
curl -s -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' "$URL" -d "$BODY" \
  | sed -n 's/^data: //p' | jq '.result.structuredContent // .error // .'
