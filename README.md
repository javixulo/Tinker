# Tinker

MCP server that keeps an inventory of home devices and answers from their manuals (PDF indexed in a vector store). Migration to **C# / .NET 10** of the MCP of the Python project `home_manuals_agent`.

## Requirements

- .NET SDK 10 (see `global.json`)

## Run

```bash
dotnet build
dotnet run --project src/Tinker.Mcp
```

The server speaks MCP over stdio, so it is meant to be started by an MCP client. To use it from Claude Code:

```bash
claude mcp add home-manuals -- dotnet run --project src/Tinker.Mcp
```

## Layout

| Path | What |
|---|---|
| `src/Tinker.Mcp` | The MCP server |
| `docs/` | Functional and technical specs |
| `reference/python-mcp` | Original Python code, read-only reference for the migration |

## Status

Skeleton: the server starts and exposes a placeholder `ping` tool.
