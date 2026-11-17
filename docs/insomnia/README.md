# Insomnia collection

`tinker-mcp.insomnia.json` has one request per scenario of `register_device` (UC-01 and UC-02), plus `Health` (`GET /health`), `tools/list` and `initialize` (optional, see below). Each request is a JSON-RPC `POST` to the MCP endpoint, with the headers Streamable HTTP needs (`Accept: application/json, text/event-stream`).

**Import:** Insomnia → Import → this file. It also opens in Bruno and Postman.

**URL:** the environment variable `base_url` is the host and port, with no path (default `http://localhost:8080`, the `Sample/` sandbox). The requests add `/mcp` or `/health`. Change it in *Manage Environments* to point at another server.

**Order and state.** A device registers once, so the results depend on what is in the database. For the expected results run the requests in order from an empty database (`Sample/down.sh --reset`, then `docker compose up -d`):

| Request | Expected |
|---|---|
| UC-01 · 1 and 2 | `ok: true` (devices 1 and 2) |
| UC-02 · 1, 2 and 4c | `ok: false`; they reuse a manual that UC-01 already registered, so `manual_in_use` comes together with `device_exists` / `invalid_fields` |
| UC-02 · the rest | `ok: false`, with the error code its name says |
| UC-02 · 10 | five errors at once |

The response is Server-Sent Events (`event: message` and a `data:` line with the JSON-RPC answer); the result is in `result.structuredContent`.

**Two blocks in the result.** A tool result carries the same data twice: `structuredContent` (the JSON object, with a schema) and `content` (the same JSON as text). The MCP specification asks for the text copy so that clients that do not understand structured output still get the result. It is normal and the two must be identical.

**`initialize`.** The MCP handshake: the client says which protocol version it speaks and the server answers with its name and capabilities. A real client always does it first. This server is stateless (no session id), so every request stands alone and `tools/call` works without it; the request is only here to see the handshake.
