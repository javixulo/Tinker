# Tinker

MCP server that keeps an inventory of home devices and answers from their manuals (PDF indexed in a vector store). Migration to **C# / .NET 10** of the MCP of the Python project `home_manuals_agent`.

## Requirements

- .NET SDK 10 (see `global.json`), or Docker.
- The embedding model files in `models/bge-small-en-v1.5/`: `model.onnx` (the `onnx` folder) and `vocab.txt` of [BAAI/bge-small-en-v1.5](https://huggingface.co/BAAI/bge-small-en-v1.5). They are git-ignored. Without them the server does not start (or set `Tinker__EmbeddingModel=hash` for development).

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
TINKER_DATA=/path/to/data TINKER_MANUALS=/path/to/manuals TINKER_MODELS=/path/to/models TINKER_UID=$(id -u) TINKER_GID=$(id -g) docker compose up -d --build
```

See `docker-compose.yml` for the variables. The data folder must be on a local disk and writable by `TINKER_UID`; manuals and models are mounted read-only. The container has a health check on `/health` and `restart: unless-stopped`.

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

`register_device` is implemented (UC-01, UC-02). `find_devices`, `delete_device` and `search_manuals` are not yet.
