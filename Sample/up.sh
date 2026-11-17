#!/usr/bin/env bash
# Builds and starts the sandbox, and waits until it is healthy.
set -euo pipefail
cd "$(dirname "$0")"
[ -f ../models/bge-small-en-v1.5/model.onnx ] || { echo "Missing ../models/bge-small-en-v1.5/model.onnx and vocab.txt (see ../README.md)"; exit 1; }
[ -n "$(ls -A manuals 2>/dev/null)" ] || cp -R ../tests/fixtures/manuals/. manuals/   # the manuals folder is git-ignored: start with the public test PDFs
DC="docker compose"; $DC version >/dev/null 2>&1 || DC="docker-compose"
$DC up -d --build
for _ in $(seq 1 60); do
  [ "$(docker inspect -f '{{.State.Health.Status}}' tinker-sample 2>/dev/null)" = healthy ] && { echo "Up: http://localhost:8080/mcp"; exit 0; }
  sleep 2
done
echo "Not healthy yet. Look at: docker logs tinker-sample"; exit 1
