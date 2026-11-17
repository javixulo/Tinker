# Home Manuals MCP — Technical Specification

> Status: **draft v0.1**
> Sibling document: [functional_spec.md](./functional_spec.md)
> Source being migrated (Python, for reference only): [legacy/python_technical_spec_extract.md](./legacy/python_technical_spec_extract.md) and `reference/python-mcp/`

## 1. Technology rule

**Everything in this project is written in C# on .NET 10.** No Python, no other runtime, in the server, the tests or the tooling.

- Target framework `net10.0`, C# 14 (the SDK default), pinned in `global.json`.
- Nullable reference types enabled and warnings treated as errors (`Directory.Build.props`).
- The MCP server uses the official C# SDK (`ModelContextProtocol`) over the stdio transport. Logs go to stderr: stdout carries the protocol.
- Behaviour parity with the Python server is defined by the tool contract (names, schemas, messages), not by its implementation. See section 3.11 of the legacy extract.

## 2. Solution layout

```
Tinker.slnx
src/Tinker.Mcp/      MCP server (stdio)
docs/                     functional and technical specs
reference/python-mcp/     original Python code and evals, read-only reference
```

## 3. Open decisions

To be decided and written here as they are made:

- Vector store and embedding model (candidates: SQLite + `sqlite-vec` + FTS5 for hybrid search, Qdrant, pgvector).
- PDF text extraction (candidate: PdfPig; spike against the fixtures first).
- Tracing: OpenTelemetry .NET, daily JSONL files, optional Phoenix, PII redaction of spans.
- Test framework and how the MCP contract is tested.
