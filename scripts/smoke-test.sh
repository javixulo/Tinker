#!/usr/bin/env bash
# Smoke test of the Docker image: it starts, passes its startup validation (data folder, manuals folder, database and
# embedding model), answers /health and lists the five tools over MCP. It does not exercise the tools.
#
# Usage: [EXPECTED_VERSION=0.1.0] scripts/smoke-test.sh [image]        (default image: tinker-mcp:latest)
set -euo pipefail

image="${1:-tinker-mcp:latest}"
name="tinker-smoke-$$"
expected_tools=(register_device update_device delete_device find_devices search_manuals)

# Throwaway volumes instead of host folders: bind mounts of temporary folders are not shared with Docker Desktop on macOS.
data_volume="$name-data"
manuals_volume="$name-manuals"
cleanup() {
  status=$?
  if [ "$status" -ne 0 ] && docker inspect "$name" >/dev/null 2>&1; then
    echo "--- container logs ---" >&2
    docker logs "$name" 2>&1 | tail -30 >&2 || true
  fi
  docker rm -f "$name" >/dev/null 2>&1 || true
  docker volume rm -f "$data_volume" "$manuals_volume" >/dev/null 2>&1 || true
  exit "$status"
}
trap cleanup EXIT

fail() { echo "SMOKE FAILED: $*" >&2; exit 1; }

echo "Starting $image ..."
# The container runs as a non-root user, like the compose file does with TINKER_UID and TINKER_GID. A new volume belongs
# to root, so its owner is set first with the image itself.
docker volume create "$data_volume" >/dev/null
docker volume create "$manuals_volume" >/dev/null
docker run --rm --user 0 --entrypoint chown -v "$data_volume:/data" "$image" 1000:1000 /data \
  || fail "the image could not be started (does it exist?)"
docker run -d --name "$name" --user 1000:1000 -p 127.0.0.1::8080 \
  -v "$data_volume:/data" -v "$manuals_volume:/manuals:ro" "$image" >/dev/null

port=""
for _ in $(seq 1 60); do
  if [ "$(docker inspect -f '{{.State.Running}}' "$name")" != "true" ]; then
    fail "the container stopped on its own (startup validation failed?)"
  fi
  port="$(docker port "$name" 8080/tcp 2>/dev/null | head -1 | sed 's/.*://')"
  if [ -n "$port" ] && curl -fsS -m 2 "http://127.0.0.1:$port/health" >/dev/null 2>&1; then
    break
  fi
  port=""
  sleep 1
done
[ -n "$port" ] || fail "/health did not answer within 60 seconds"
echo "ok: /health answers"

response="$(curl -fsS -m 10 -X POST "http://127.0.0.1:$port/mcp" \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}')" || fail "tools/list did not answer"
for tool in "${expected_tools[@]}"; do
  grep -q "\"name\":\"$tool\"" <<<"$response" || fail "the tool $tool is not listed"
done
echo "ok: the ${#expected_tools[@]} tools are listed"

# The release workflow sets this to the version of the tag: the handshake must report it, not a default.
if [ -n "${EXPECTED_VERSION:-}" ]; then
  handshake="$(curl -fsS -m 10 -X POST "http://127.0.0.1:$port/mcp" \
    -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":2,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"0"}}}')" \
    || fail "initialize did not answer"
  grep -q "\"serverInfo\":{[^}]*\"version\":\"$EXPECTED_VERSION\"" <<<"$handshake" \
    || fail "the handshake does not report the version $EXPECTED_VERSION: $handshake"
  echo "ok: the server reports version $EXPECTED_VERSION"
fi

echo "Smoke test passed."
