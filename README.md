# Tinker

MCP server that keeps an inventory of home devices and answers from their manuals (PDF indexed in a vector store). Migration to **C# / .NET 10** of the MCP of the Python project `home_manuals_agent`.

## Requirements

- .NET SDK 10 (see `global.json`), or Docker.
- To run outside Docker: the embedding model files in `models/bge-small-en-v1.5/`: `model.onnx` (the `onnx` folder) and `vocab.txt` of [BAAI/bge-small-en-v1.5](https://huggingface.co/BAAI/bge-small-en-v1.5). They are git-ignored. Without them the server does not start (or set `Tinker__EmbeddingModel=hash` for development). The Docker image downloads them itself when it is built.

## Run

```bash
dotnet run --project src/Tinker.Mcp
```

The server speaks MCP over Streamable HTTP at `http://localhost:5000/mcp` (`/health` for the health check). Configuration is by environment variables: `Tinker__DataPath` (default `data`), `Tinker__ManualsPath` (default `manuals`), `Tinker__EmbeddingModelPath`, `Tinker__EmbeddingModel`. To use it from Claude Code:

```bash
claude mcp add --transport http tinker http://localhost:5000/mcp
```

## Docker

```bash
TINKER_DATA=/path/to/data TINKER_MANUALS=/path/to/manuals TINKER_UID=$(id -u) TINKER_GID=$(id -g) docker compose up -d --build
```

See `docker-compose.yml` for the variables. The data folder must be on a local disk and writable by `TINKER_UID`; the manuals are mounted read-only. The embedding model (about 130 MB) is downloaded when the image is built, from a pinned revision and checked against its SHA-256, so the first build needs network and the container needs none. The container has a health check on `/health` and `restart: unless-stopped`.

## Tests

```bash
dotnet test
```

## Layout

| Path | What |
|---|---|
| `src/Tinker.Core` | Logic: inventory, manuals index, registration |
| `src/Tinker.Mcp` | The MCP server (HTTP) |
| `tests/` | Logic and contract tests |
| `docs/` | Functional and technical specs |
| `reference/python-mcp` | Original Python code, read-only reference for the migration |

## Status

`register_device` (UC-01, UC-02), `find_devices` (UC-05 to UC-07) `delete_device` (UC-03, UC-04) and `search_manuals` (UC-08, UC-09) are implemented: all four tools.
