# Sample: a local sandbox to try the MCP by hand

Everything here is for trying `register_device` in Docker on your machine. Nothing in this folder is used by the server itself.

```
Sample/
  docker-compose.yml   builds the image from ../Dockerfile and runs it
  manuals/             the manuals the MCP can see (3 public PDFs). Add yours here
  data/                the database (inventory.db) appears here. Git-ignored
  up.sh  down.sh       optional shortcuts: start and wait for healthy / stop (down.sh --reset deletes the database)
  call.sh              calls register_device with the JSON you give it
  scenarios.sh         runs all the UC-01 / UC-02 cases in a row
  db.sh                runs SQL on the database
```

The `Dockerfile` is not here: it is in the repository root (`../Dockerfile`) and the compose file points to it. Docker builds the image from the repository, so you do not copy it anywhere.

## Steps

1. Requirements (the `manuals/` folder is git-ignored: if it is empty, `./up.sh` fills it with the public PDFs of `../tests/fixtures/manuals`, or copy them yourself with `cp -R ../tests/fixtures/manuals/. manuals/`):
    Docker running (with Colima: `colima start`), `jq` and `sqlite3`, and the model files in `../models/bge-small-en-v1.5/` (`model.onnx` and `vocab.txt`; see the main README).
2. `docker compose up --build` (or `docker-compose`, with `-d` to leave it in the background) — builds the image (the first time takes a few minutes) and starts the server. `./up.sh` does the same and waits until it is healthy.
3. `./scenarios.sh` — or call by hand, e.g. `./call.sh '{"brand":"Shelly","model":"Dimmer 2","manuals":["shelly/dimmer_2_guide.pdf"]}'`.
4. `./db.sh "SELECT * FROM devices"` — see what was stored. After a rejection nothing must have changed.
5. `docker logs tinker-sample` — the server log (warnings for rejections, errors for failures).
6. `docker compose down` to stop (`./down.sh` is the same); `./down.sh --reset` also deletes the database, to start from zero.

To use your own manuals, copy the PDFs into `manuals/` (subfolders are fine) and register them with their path relative to that folder.

## Connect a real MCP client

The server is at `http://localhost:8080/mcp` (Streamable HTTP). For Claude Code: `claude mcp add --transport http tinker-sample http://localhost:8080/mcp`.
