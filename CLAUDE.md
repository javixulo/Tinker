# Tinker

C# / .NET 10 MCP server (inventory of home devices + search in their manuals). Migration of the Python MCP in `reference/python-mcp/`.

## Rules

- Everything is **C# on .NET 10**. Python code in `reference/` is read-only: port it, never run or edit it.
- The specs are in `docs/`. Read `docs/functional_spec.md` and `docs/technical_spec.md` before changing behaviour, and update them when a decision is made. `docs/legacy/` is the old Python spec: background only.
- The contract with the clients is the tool names, schemas and messages (technical spec, legacy section 3.11), not the implementation. Do not change it without saying so.
- The MCP server uses the Streamable HTTP transport (technical spec, sections 1 and 4). Logs go to the console on stderr and to OpenTelemetry (OTLP); see technical spec, section 3.
- Messages in tool results are written for the AI that consumes them: what happened and what to do next. Never return a raw exception.
- Write documentation and code in English and communicate with the user in Spanish.

## Commands

```bash
dotnet build
dotnet run --project src/Tinker.Mcp
```

## Git and secrets

- `.env` holds `GITHUB_TOKEN` and is git-ignored. Never print it, commit it or put it in a URL.
- Pushing uses the local credential helper in `.git/token-helper.sh`.
- Do not commit or push unless asked.
