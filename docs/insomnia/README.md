# Insomnia collection

`tinker-mcp.insomnia.json` has one request per scenario of `register_device` (UC-01 and UC-02), `find_devices` (UC-05 to UC-07), `search_manuals` (UC-08 and UC-09) and `delete_device` (UC-03 and UC-04), plus `Health` (`GET /health`), `tools/list` and `initialize` (optional, see below). Each request is a JSON-RPC `POST` to the MCP endpoint, with the headers Streamable HTTP needs (`Accept: application/json, text/event-stream`).

**Import:** Insomnia → Import → this file. It also opens in Bruno and Postman.

**Re-importing.** If you import a newer version over a workspace you already have, Insomnia may keep the old, empty *Base Environment* and the `{{ _.base_url }}` variable has no value. Delete the old *Tinker MCP* collection first and import again, or open *Manage Environments* and set `base_url` by hand.

**URL:** the environment variable `base_url` is the host and port, with no path (default `http://localhost:8080`, the `Sample/` sandbox). The requests add `/mcp` or `/health`. Change it in *Manage Environments* to point at another server.

**Order and state.** A device registers once, so the results depend on what is in the database. For the expected results run the requests in order from an empty database (`Sample/down.sh --reset`, then `docker compose up -d`):

| Request | Expected |
|---|---|
| UC-01 · 1 and 2 | success (devices 1 and 2) |
| UC-02 · 1, 2 and 4c | tool error (`isError`); they reuse a manual that UC-01 already registered, so `manual_in_use` comes together with `device_exists` / `invalid_fields` |
| UC-02 · the rest | tool error (`isError`), with the error code its name says |
| UC-02 · 10 | five errors at once |
| UC-05 · 1 and 2 | success with Fibaro Double Smart only |
| UC-06 · 1, 2 and 3 | both devices; only Shelly Dimmer 2; both devices |
| UC-07 · 1 and 2 | success, empty list and a `message` |
| UC-07 · 3 | only from an empty database: success, empty list, "no registered devices". Run it before the registrations |
| UC-08 · 1 | success, up to 3 chunks of device 1 (`shelly/dimmer_2_guide.pdf`), most relevant first, each with `text`, `device_id`, `manual` and `page`; `message` is empty |
| UC-08 · 2 | success, 3 chunks that all have `device_id: 2` (`fibaro/fgs_2x3_en_manual.pdf`), although the query is about a dimmer |
| UC-08 · 3 and 3b | exactly 1 chunk; at most 5 chunks (the default) |
| UC-09 · 1 | tool error (`isError`), `device_not_found` (device 999) |
| UC-09 · 2, 2b and 2c | tool error (`isError`), `invalid_fields`: blank query; `n_results` 0; and all three arguments wrong at once (one error, three messages) |
| UC-04 · 1, 2 and 3 | tool error (`isError`): `invalid_fields`, `device_not_found`, `ambiguous_device` (both devices in the error). Nothing is deleted |
| UC-03 · 1, 1b and 2 | success: deletes Fibaro Double Smart; 1b registers Shelly RGBW2 again (setup, device 3); deletes it by type and brand |

**UC-04 and UC-03 go last, and UC-04 first.** UC-03 deletes the devices the other requests use, and UC-04 needs both of them. UC-04 · 4 (failure while deleting) cannot be provoked through the API; the logic tests cover it. The same goes for UC-09 · 3 (manuals without text: needs a scanned PDF, which the sample folder does not have) and UC-09 · 4 (unexpected failure). UC-08 and UC-09 only read, so they can be run any number of times, but they need devices 1 and 2 from UC-01, so they go before UC-03.

The two devices of UC-01 share a location and a color so the `find_devices` requests have something in common; UC-01 · 1 sets them for that reason.

The response is Server-Sent Events (`event: message` and a `data:` line with the JSON-RPC answer); the result is in `result.structuredContent`.

**Two blocks in the result.** A tool result carries the same data twice: `structuredContent` (the JSON object, with a schema) and `content` (the same JSON as text). The MCP specification asks for the text copy so that clients that do not understand structured output still get the result. It is normal and the two must be identical.

**`initialize`.** The MCP handshake: the client says which protocol version it speaks and the server answers with its name and capabilities. A real client always does it first. This server is stateless (no session id), so every request stands alone and `tools/call` works without it; the request is only here to see the handshake.
