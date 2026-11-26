# Home Manuals MCP — Technical Specification

> Status: **draft v0.6**
> Sibling document: [functional_spec.md](./functional_spec.md)
>
> Each section says whether it is **Decided** (agreed with the user) or **Proposed** (a draft to review, section by section). Sections 1 to 4 and 10 are decided; 5 to 8 are proposed; open questions are in section 9.

## 1. Technology rule

**Decided.** Everything in this project is written in C# on .NET 11. No other language or runtime, in the server, the tests or the tooling.

- Target framework `net11.0`, C# 14 (the SDK default), pinned in `global.json`.
- Nullable reference types enabled and warnings treated as errors (`Directory.Build.props`).
- The MCP server uses the official C# SDK (`ModelContextProtocol`) over the **Streamable HTTP transport** (ASP.NET Core, `ModelContextProtocol.AspNetCore`). It runs as a long-lived service, not as a process spawned per session. See section 4 for why.
- The contract with the clients is the tool names, schemas and messages, not the implementation. See section 6.

## 2. Solution layout

**Proposed.**

```
Tinker.slnx
src/Tinker.Mcp/            ASP.NET Core host: HTTP transport, tool classes, configuration, telemetry, health
src/Tinker.Core/           Logic: models, services, ports, SQLite inventory, ingestion, path confinement
tests/Tinker.UnitTests/         Unit tests: pure logic, no disk, network or database
tests/Tinker.IntegrationTests/  Integration tests: services with real SQLite, files and PDFs
tests/Tinker.ModelTests/        Integration tests with the real embedding model (skipped without its files)
tests/Tinker.ContractTests/     Contract tests through a real MCP client
tests/Tinker.SystemTests/       System tests: complete flows against the Docker image (skipped without TINKER_SYSTEM_IMAGE)
tests/fixtures/manuals/    Public PDF manuals used by the tests
scripts/smoke-test.sh      Smoke test of the Docker image (section 8)
tools/Traceability/        Console tool: checks the criteria IDs of the functional spec against the test markers (section 8)
docs/                      Functional and technical specs
```

**Status.** `Tinker.Core`, `Tinker.Mcp` (web SDK, `WithHttpTransport`, `MapMcp("/mcp")`, `/health`), `Tinker.UnitTests`, `Tinker.IntegrationTests`, `Tinker.ModelTests` and `Tinker.ContractTests` exist, with `register_device` (UC-01 and UC-02), `delete_device` (UC-03 and UC-04), `find_devices` (UC-05 to UC-07) and `search_manuals` (UC-08 and UC-09) implemented: all four tools exist. The changes that `update_device` brought to the existing tools are built (optional manuals, normalized values, new statuses, `manual_unreadable`, brand cleaning in lookups, no-manuals message in `search_manuals`); `update_device` (UC-10 to UC-12) is built too: all five tools exist. The placeholder `ping` tool is gone. Configuration is read from the `Tinker` section (`Tinker__DataPath`, `Tinker__ManualsPath`). OpenTelemetry (logs and traces) is exported through OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

## 3. Observability

**Decided.**

- **OpenTelemetry (OTLP) for logs and traces.** Every MCP in the system exports through OTLP, so there is one pipeline and one place to look. Traces are correlated across services with the W3C `traceparent` header, provided the client propagates it.
- **Seq is the backend**, as a single container that accepts OTLP. Chosen over the Grafana stack (Loki, Tempo, Prometheus, collector) because it is one container and the target NAS has limited resources. A collector can be added later without changing the MCPs.
- **Console logging to stderr stays on** next to OTLP. Why: `docker logs` keeps working when Seq is down or unreachable.
- Exporter configuration uses the standard variables (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`). An empty endpoint exports nothing; the app works the same.
- **Logs and traces only; no metrics.** Seq does not accept OTLP metrics and the code registers none.
- **Seq has its own compose file and its own network** (`seq/docker-compose.yml`, network `seq-net`, UI on port 8081, ingestion on 5341, no authentication: trusted local network only). An MCP that wants to send its telemetry joins `seq-net` as an `external` network and sets `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf` (Seq has no gRPC) and `OTEL_EXPORTER_OTLP_ENDPOINT=http://seq:5341/ingest/otlp`; the SDK appends `/v1/logs` and `/v1/traces` itself. Seq must be started first. **Checked** with the sample sandbox: logs and spans of a `register_device` call show up in Seq.

**Decided: what is recorded.** Written after reviewing the code against the shared `app-telemetry` standard.

- **Resource attributes on every record:** `service.name` (`OTEL_SERVICE_NAME`, default `tinker-mcp`), `service.version` (the version of the assembly) and `deployment.environment.name` (from `OTEL_RESOURCE_ATTRIBUTES`; `docker-compose.yml` sets it to `production`). `docker-compose.yml` also fixes `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, because the SDK default is gRPC, which Seq does not accept, and the failure would be silent.
- **Spans.** The incoming HTTP request (ASP.NET Core instrumentation) is the root of the unit of work. Under it, one span per tool call (`register_device`, `update_device`, `delete_device`, `find_devices`, `search_manuals`) and child spans only for the meaningful steps: `extract_manual` (reading a PDF), `embed_manuals` (chunking and embedding), `run_transaction` (the database transaction) and `search_index` (embedding the query and ranking the chunks). A step that fails unexpectedly has the error status and `error.type`; a rejection is not a failed step. The registration, update and deletion also record the events `committed`, `rolled_back` and `rejected`.
- **Attributes this project adds** (the names are the contract with whoever reads the logs; renaming one is a change to say out loud):

  | Name | Where | Meaning | Personal data |
  |---|---|---|---|
  | `device.id` | spans and logs | `device_id` of the device, when known | No |
  | `device.brand`, `device.model` | spans of `register_device`; logs of registered and deleted devices | The brand and model the client gave | No: they are product names |
  | `search.query` | span of `search_manuals` | The query text of the client | No: the user asks about a device, not about themselves |
  | `error.type` | spans (OpenTelemetry convention) and logs | The error codes of section 6 (`invalid_fields`, `failed`, ...), or `manual_unreadable` in a step; absent on success | No |
  | `error.message` | logs of rejections | The message returned to the client, with the values that caused the rejection | No |
  | `matches.count` | spans of `find_devices` and `search_manuals` | How many devices or excerpts were found | No |
  | `chunks.count`, `manuals.count`, `manuals.added`, `manuals.removed`, `devices.count`, `criteria`, `manual.path`, `problem` | logs and the `embed_manuals` span | Counts and values that explain a log line | No |

  Before inventing a name, look for it in the OpenTelemetry conventions and in the lists of the other MCPs.
- **Decision on personal data:** the user decided that brand, model, query text and the values of a rejected request are recorded, because in this system they are about devices and manuals, not about people. If the system ever stores something personal, the decision is revisited and applied in one place. What is **never** recorded: the content of manuals, chunks or embeddings, whole request bodies, secrets and tokens.
- **Logs.** Messages are English, in the past tense, with a constant template and named fields; the names are the attributes above, so they are written with plain `ILogger` calls (the `[LoggerMessage]` generator cannot take names with dots). Levels: `Information` for a registration, update or deletion completed (and the startup checks passed), `Warning` for every rejection (with its code and values) and for a lookup that finds nothing, `Critical` for a failed startup check, `Error`, with the exception, for every `failed`. Reads that succeed are not logged: the span says they happened.
- The server never returns an exception to the client (section 6), so the full exception and the trace id are in the log, and only the trace id travels in the message. The log lines written inside a span carry its trace id and span id, through the OpenTelemetry logging bridge.
- **Request logs, without the noise of the health check.** The server writes one line per HTTP request (`RequestLogging`: method, path, status, duration) in place of the several of ASP.NET Core, which logs only from `Warning` up (`appsettings.json`). The level depends on the request: a successful health check is `Debug` (hidden by default; `Logging__LogLevel__Tinker.Mcp.RequestLogging=Debug` shows it), any other request is `Information`, and a server error is `Warning`, the health check included. Requests to `/health` are not traced; every other request is. A contract test checks it.
- **Trace context.** The server accepts the W3C `traceparent` header on every call and continues that trace (ASP.NET Core does it); without the header it starts its own trace and works the same. The client opens one root span per step and sends `traceparent` on every call it makes, since the server can only join a trace the client starts. Tools take no correlation id as a parameter: it would pollute the contract. If a client does not send `traceparent`, each MCP starts its own trace, and the steps can only be tied together by `device.id` and the time. **To check with the real client** whether it sends `traceparent`; the contract tests prove the server side (section 8).

## 4. Deployment

**Decided.** Each MCP is packaged as a Docker image and runs as its own container, so MCPs can be enabled, disabled and updated independently, and each one gets only the permissions, volumes and secrets it needs.

- **Target.** A home NAS for now, possibly a Linux server later. Device discovery (mDNS, multicast) is out of scope, so bridge networking is not a problem. Devices are added by hand.
- **Transport.** HTTP, because MCPs are long-lived services: faster start, state shared between sessions, one place for logs and health. The reverse proxy (TLS, auth) is the user's existing one and is out of scope for now.
- **Persistent data.** One data folder, mounted as a **bind mount** from the NAS (e.g. `/volume1/docker/tinker/data:/data`), so it is visible on the NAS and included in its backups. Nothing persistent lives inside the image. The data folder must be on a local disk of the host, not on NFS or SMB: SQLite file locking is not reliable over the network.
- **User and permissions.** The container runs as a **non-root user** set with `user:` in compose, matching the owner of the data folder.
- **Startup validation.** On start the server checks that everything it needs is in place and writes every problem to the logs, with what is wrong and how to fix it. At least:
  - the data folder exists and is writable by the running user (an actual write test, not just a permission read);
  - the manuals folder exists and is readable;
  - the database can be opened;
  - required configuration is present and valid.
- **Configuration through environment variables**, using the ASP.NET Core convention (`Tinker__DataPath`, `Tinker__ManualsPath`), plus `ASPNETCORE_HTTP_PORTS` and the OpenTelemetry variables of section 3. Secrets go in an `env_file`, never in the image.
- **Timezone** through the `TZ` variable, as in all the user's images.
- **Healthcheck.** An HTTP endpoint (`/health`) used by the Docker `HEALTHCHECK`: every 5 seconds while the container starts (60 s start period; `--start-interval` needs Docker Engine 25 or later) and every 5 minutes afterwards, with 3 retries, so a hung server is marked unhealthy after about 15 minutes (Docker shows it; it does not restart the container by itself). The check leaves nothing in the logs or the traces (section 3).
- **Restart policy.** `restart: unless-stopped`.
- **Image tags.** The image is `ghcr.io/javixulo/tinker`, with a tag per version (section 10); the user updates by hand, by changing the version they run. `latest` points only at the newest stable version, never at a pre-release. Keeping the previous image on the host allows a quick rollback: start the old tag again. The compose file pins the version in a required variable (`TINKER_VERSION`), with no default and never `latest`, so updating is a decision.

**Proposed additions.**

- The manuals folder is mounted **read-only** (`:ro`). The server never writes manuals, and a read-only mount is a second line of defence for the confinement of manual paths (section 5).
- The startup validation also checks that the database schema is at the expected version (section 7). **Built:** it checks that the embedding model files exist and that the embedding model of the index is the configured one.

**Built.** `Dockerfile` (multi-stage, .NET 11 `aspnet` runtime, `curl` only for the healthcheck; the native libraries of ONNX Runtime and SQLite are trimmed to those of the architecture of the image (`linux-x64` or `linux-arm64`), since a container always runs Linux on one of them) and `docker-compose.yml` (the image `ghcr.io/javixulo/tinker:${TINKER_VERSION:?…}`, with `build: .` as a comment; `cap_drop: [ALL]` and `no-new-privileges`; default host port `7843`, not a common one; `.env.template` lists every variable): `/data` (bind mount), `/manuals:ro`, the embedding model inside the image (section 7), `user:` from `TINKER_UID`/`TINKER_GID`, `TZ`, OTLP endpoint through `OTEL_EXPORTER_OTLP_ENDPOINT`, `restart: unless-stopped`, healthcheck on `/health`. Checked by building the image and running it as a non-root user: it starts, loads the model, registers a device through `/mcp`, keeps the data across a restart and rejects a duplicate. Seq is built as a separate compose (section 3); `docker-compose.yml` still exports nothing by default. Moved to .NET 11 (2026-11-25) and rechecked: the image builds, the smoke test passes for linux/amd64 and for linux/arm64 (the latter emulated, on an amd64 host, with the model loaded by ONNX Runtime), and the model and system tests pass on amd64. Not done yet: a published image (section 8, "Release").

## 5. Architecture

**Proposed.**

- **Tools are thin.** A tool class only binds the arguments, calls a service and returns its result. No rule lives in a tool class, so the contract and the logic can be tested apart.
- **Services** (in `Tinker.Core`): `DeviceRegistrar`, `DeviceFinder`, `DeviceRemover`, `ManualSearcher` and `DeviceUpdater`, one per tool. `DeviceNormalizer` is plain logic, like `ManualPathResolver`: the one place that turns the values of a client into the stored format (section 7), used by `DeviceRegistrar` and `DeviceUpdater`. **Ports** they depend on: `IInventory` (SQLite), `IManualsIndex` (section 7), `IManualFiles` (the manuals folder: it judges the path of a manual and says whether the file exists; implemented by `ManualPathResolver`), `IManualTextExtractor` and the `IEmbeddingGenerator<string, Embedding<float>>` abstraction of `Microsoft.Extensions.AI`, so the embedding model can be swapped. `ManualPathResolver` is the only place that builds a manual path from client input (see the confinement point below).
- **No LLM and no API key in the server.** Everything that needs a model of language stays in the client. The embedding model runs locally, inside the container.
- **Stateless HTTP.** The tools are plain request and response: they use no sampling, elicitation or subscriptions. So the transport runs in the stateless mode of the SDK (in 2.x, `HttpServerSessionMode.Stateless`, the default): no session id, nothing kept in memory per client, a restart of the container breaks nothing. The MCP endpoint is `/mcp`; `/health` is separate and not an MCP endpoint.
- **Manuals stay inside the manuals folder.** Every manual path that comes from a client is resolved by one class, `ManualPathResolver`, and nothing else builds such a path, also when a file is read for indexing. It applies the rules of UC-02 in the functional spec (an absolute path, a `..` in the path, or a link that leads outside the folder is not allowed). As built: the text is judged first: a leading `/`, `\\` or `~`, a drive letter (`C:`), a NUL or an encoded sequence (`%` followed by two hex digits) is "outside the folder"; then `..` as a component (splitting on `/` and `\\`) is "`..`"; then every component is walked, following links (including chains and links to a missing target), and the real path must be inside the real manuals folder. The stored path is the **real** one relative to the folder in POSIX form, so a link inside the folder to another file inside it is stored as that file, and `shelly/./a.pdf` and `shelly/a.pdf` are the same manual. For that reason a list of manuals is reduced to its distinct real paths, keeping the order of the first appearance of each, before anything else is done with it (a manual written twice counts as one). The one non-obvious requirement is that links are followed in **every component** of the path, not only in the last one: .NET has no `realpath`, so the class walks the components. The not-allowed answer is the same whether the file exists or not. Known limit: the check and the later read are not atomic, which is acceptable for a single-user home tool, and the read-only mount of section 4 limits the damage.
- **No state in the process** besides caches that can be rebuilt. All data is in the data folder.
- **Concurrency.** Requests arrive concurrently. SQLite runs in WAL mode with a busy timeout, and every write operation (register, update, delete) is one transaction. The heavy work (reading the PDF, chunking, embedding) happens before the transaction opens, so the write lock is held briefly.

## 6. Tool contract

**Proposed.** This is the contract with the clients. Changes to it must be said explicitly (`CLAUDE.md`). The input and output models are in section 2 of the functional spec; this section covers how they travel.

**Tools:** `register_device`, `find_devices`, `delete_device`, `search_manuals` and `update_device`, listed in this order on every connection. Every tool has a title and the annotation `openWorldHint=false` (nothing reaches outside systems). The other hints are in this table; they are hints for the client, never a security measure:

| Tool | `readOnlyHint` | `destructiveHint` | `idempotentHint` | Why |
|---|---|---|---|---|
| `find_devices`, `search_manuals` | true | | | They only read. |
| `register_device` | false | false | false | It only adds. Registering twice is rejected as `device_exists`, so it is not idempotent. |
| `update_device` | false | true | true | It replaces values and whole lists and drops the index content of removed manuals, so it is not purely additive. The same call twice leaves the same device. It edits at once, so the client confirms with the user first. |
| `delete_device` | false | true | false | It deletes at once, so the client confirms with the user first. The second call finds nothing. |

**Serialization.**

- JSON property names are `snake_case` (`device_id`, `device_type`, `purchase_date`, `n_results`), as the clients expect. C# types are records with `PascalCase` properties, and the server's JSON options use `JsonNamingPolicy.SnakeCaseLower`. The SDK default is `camelCase`, so a contract test checks the schemas.
- Dates are `yyyy-MM-dd` (`DateOnly`). `status` is a lowercase string (`active`, `inactive`, `archived`). An absent optional value is `null`.
- A `device_id` that comes in the `device` of `register_device` is ignored. In the `changes` of `update_device` it is an error (`invalid_fields`).
- **Absent and empty are different in `update_device`.** In `changes`, a field that is absent or `null` is not sent and keeps its value; a text that is empty or has only spaces, or an empty list, empties the field (functional spec, section 2.5). So `changes` is a `DeviceChanges` (dates as text, so an empty one clears them) deserialized without turning empty values into `null`, and `DeviceUpdater` receives "not sent" and "empty" as two different things. In `register_device` and `find_devices` a blank value still counts as not given. A contract test covers each case.
- The SDK uses the C# parameter names as the argument names of a tool, and the naming policy above does not apply to them. So the top-level parameters of `search_manuals` and `update_device` are written in snake_case in `SearchManualsTool` and `UpdateDeviceTool` (`device_id`, `query`, `n_results`, `changes`), with the analyzer rules turned off for those classes; the contract test checks the schema.
- Results are typed objects returned as the tool result, as structured content, with the schema generated from the C# types. The exact SDK settings are confirmed in a spike.

**Outcomes have no `ok` flag, and a rejection sets `isError`.** Every outcome is one typed result with no `ok` flag, never a protocol error. A rejection (or `failed`) has `error` or `errors`, and the server sets the MCP flag `isError: true` in one `tools/call` filter (`ToolErrorFlag`) that looks for a non-empty `errors` or an `error` in the structured content, so no tool class has to remember it; the structured content and its text copy are the same as without the flag. A success and an empty result (nothing found, nothing to return, with a `message`) are not errors. Reason: the client passes `isError` results to the model as something to correct or report, which is what the messages are written for (the `mcp-tool-design` skill, section 4); and an `ok` field duplicated what `isError` already says, so the two could disagree. The C# records keep a derived, JSON-ignored `Ok` property for the code and tests only. This is a change of contract (the `ok` field is gone), decided by the user. A protocol error is only for what the model cannot fix (unknown tool, malformed request).

**Structured content and text.** Every result is structured content with the schema generated from the C# types, and the same JSON as text for clients that only read text. A contract test checks both.

**No raw exception ever reaches the client.** Every service call runs inside one guard that catches what is unexpected, logs it with its trace id (section 3), and returns a `failed` error (a tool error).

**Be tolerant on input.** Tool parameters are nullable and have defaults, and our own code validates them. A missing `query` or an empty `device` gives an `invalid_fields` result (with the tool-error flag) with a message the AI can act on, not an SDK-level binding error.

**Descriptions are part of the contract.** The tool description and each parameter description are written for the AI that picks the tool: what it is for, what it needs, what it returns and what to do with the result. They are reviewed like the messages.

**Error codes.**

| Code | Tools | Meaning |
|---|---|---|
| `invalid_fields` | register, update, delete, search | Required or valid input is missing |
| `manual_not_allowed` | register, update | A manual path is outside the manuals folder or contains `..` |
| `manual_not_found` | register, update | An allowed manual path does not exist (in `update`, only a manual that is new for the device is checked) |
| `manual_unreadable` | register, update | An allowed manual that exists cannot be read: damaged, protected by a password or not a PDF. It is not saved and the whole call is rejected |
| `manual_in_use` | register, update | A manual already belongs to another device (carries that `device`) |
| `device_exists` | register, update | A device with the same brand and model exists (carries that `device`; in `update`, another device) |
| `device_not_found` | delete, search, update | No device matches |
| `ambiguous_device` | delete | Several devices match (carries those `devices`) |
| `failed` | all | An unexpected failure, not caused by the request |

**Errors of one registration.** All the reasons are returned together, in this order: `invalid_fields` (one error for the missing fields, another for an invalid status), `manual_not_allowed` (one per manual), `manual_not_found` (one error listing every missing manual), `manual_unreadable` (one error listing every manual that cannot be read, with its reason), `device_exists`, `manual_in_use` (one per manual).

**Errors of one update.** The same reasons and the same order, with one difference: `device_not_found` comes alone, because the other rules cannot be checked against a device that is not there.

**Messages.** Written in English for the AI: what happened and what to do next. They never give the location of the manuals folder nor say whether a file outside it exists. Templates:

| Code or case | Message |
|---|---|
| `invalid_fields` (register) | `Missing or blank required fields: {fields}. Ask the user for them and call register_device again.`, for a brand that is empty once normalized, `The brand has no valid characters: only letters, digits, spaces, hyphens and & are kept. Ask the user for the brand and call register_device again.`, or, for a `status` that is not a valid one, `Invalid status: '{status}'. It must be one of: active, inactive, archived. Fix it or leave it out and call register_device again.` |
| `invalid_fields` (update) | `Nothing to change: give at least one field in changes. Nothing was changed.`, `` `device_id` is missing: it is the id of the device to edit. Get it with find_devices. ``, `` `device_id` cannot be changed: remove it from changes. Nothing was changed. ``, `` `{field}` cannot be emptied: it is required. Give a new value or leave it out. Nothing was changed. `` (also for a brand that is empty once normalized), or the invalid status message above with `update_device`. When several are wrong, one error carries all the messages, as in search |
| `invalid_fields` (delete) | `No property was given. Give at least one property of the device (for example brand and model) to identify it. Nothing was deleted.` |
| `invalid_fields` (search) | `` `device_id` is missing: it is the id of the device whose manuals are searched. Get it with find_devices. ``, `` `query` is blank: it must be the text to look for in the manuals. `` or `` `n_results` must be 1 or more: it is the number of excerpts to return. ``. When several arguments are wrong, one error carries all the messages joined by a space, so the client fixes them in one go |
| `manual_not_allowed` | `Manual not allowed: {path} (outside the manuals folder). Use a path relative to the manuals folder.` or `Manual not allowed: {path} (its path cannot contain ``..``). Use a path relative to the manuals folder.` |
| `manual_not_found` | `Manual not found: {path}. Check the path with the user.` |
| `manual_in_use` | `Manual already belongs to another device: {path}. That device is in the error. Do not register the manual twice.` |
| `device_exists` | `A device with this brand and model already exists: {brand} {model}. It is in the error. Do not register it again.` |
| `manual_unreadable` | `Manual could not be read: {paths} ({reason}). These manuals were not saved, and neither was the device (in `update_device`: nothing was changed). Tell the user that the file is damaged, protected by a password or not a PDF, and ask for another copy.` The reason is one of `it is protected by a password`, `it is not a PDF`, `it is damaged` or, when the library cannot tell, `it cannot be read`. One error lists every manual, each with its reason |
| `manual_in_use` (update) | `Manual already belongs to another device: {path}. That device is in the error. Nothing was changed. If the user wants to move it, remove it from that device first.` |
| `device_exists` (update) | `Another device already has this brand and model: {brand} {model}. It is in the error. Nothing was changed. Check with the user whether they mean that device, or choose other values.` |
| `device_not_found` (update) | `The device id {id} does not exist. Nothing was changed. Use find_devices to get a valid device_id.` |
| `device_not_found` (delete) | `No registered device matches the properties ({listed}). Nothing was deleted. Ask the user for other details, or check the registered devices with find_devices.` |
| `ambiguous_device` | `{n} registered devices match the properties ({listed}). Nothing was deleted. Ask the user which one it is, or give more properties.` |
| `device_not_found` (search) | `The device id {id} does not exist. Use find_devices to get a valid device_id.` |
| `failed` | `{operation} failed because of an internal problem, not because of the request. Nothing was changed. Tell the user instead of retrying. Trace id: {trace_id}.` |
| `find_devices` empty | `No registered device matches ({listed}). Ask the user for other details, or register the device.` or `There are no registered devices. Register one with register_device.` |
| `search_manuals` empty | `The manuals of this device have no extractable text (for example, they are scanned), so nothing can be answered from them. Tell the user.` or, for a device without manuals, `This device has no manuals, so nothing can be answered. Tell the user, who can add manuals with update_device.` |

Outcome of `delete_device`: `device` (as it was before being deleted) and `error` (one, not a list). Its `ambiguous_device` error carries the matching `devices`.

Outcome of `update_device`: `errors` (a list, as in `register_device`) and `device` (as stored after the edit). `device_exists` and `manual_in_use` carry the other device involved.

`{listed}` is the properties given as `name: value` pairs, with the names of the contract, separated by commas (`brand: Samsung, location: Garage`); a list is given as its items separated by `;`.

## 7. Storage

**Proposed.**

**Source of truth.** The SQLite inventory is the source of truth. The manuals index is derived from it and from the manuals folder, and can be rebuilt.

**Inventory** (`<data>/inventory.db`), with `Microsoft.Data.Sqlite` and no ORM: the schema is small and the queries are few.

```sql
CREATE TABLE devices (
  device_id         INTEGER PRIMARY KEY AUTOINCREMENT,
  brand             TEXT NOT NULL,
  model             TEXT NOT NULL,
  brand_key         TEXT NOT NULL,   -- TextKeys.Fold(brand)
  model_key         TEXT NOT NULL,   -- TextKeys.Fold(model)
  device_type       TEXT,
  color             TEXT,
  location          TEXT,
  invoice           TEXT,
  purchase_date     TEXT,            -- yyyy-MM-dd
  warranty_end_date TEXT,            -- yyyy-MM-dd
  status            TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive','archived')),
  UNIQUE (brand_key, model_key)
);
CREATE TABLE device_manuals (
  device_id INTEGER NOT NULL REFERENCES devices(device_id) ON DELETE CASCADE,
  path      TEXT NOT NULL UNIQUE      -- relative, POSIX form
);
CREATE TABLE device_urls (
  device_id INTEGER NOT NULL REFERENCES devices(device_id) ON DELETE CASCADE,
  url       TEXT NOT NULL
);
```

- **`AUTOINCREMENT`** so a `device_id` is never reused after a deletion. Without it, a client holding an old id from `find_devices` could hit a new, different device.
- **The database is the guarantee.** The checks in code give clear reasons, but the `UNIQUE` constraints decide: two simultaneous registrations cannot both pass.
- **Normalization.** Values are stored normalized by `DeviceNormalizer` (functional spec, section 1), before anything else sees them: trimmed and with each run of spaces collapsed to one; `brand` without accents and without any character that is not a letter, a digit, a space, a hyphen or `&`, then kept as it is if it is entirely in uppercase and otherwise with the first letter of each word in uppercase; `model` in uppercase, keeping hyphens and slashes; `device_type`, `color` and `location` with the first character in uppercase. `manuals`, `invoice` and `urls` are not changed. A brand that is empty once normalized is `invalid_fields`. `brand_key` and `model_key` are the folded values of the **normalized** brand and model. The `UNIQUE` constraint is on the keys, so it does not depend on the format. A lookup applies the brand cleaning of `DeviceNormalizer` (accents and characters outside the allowed ones, not the casing) to the brand it receives before folding it, so `B.S.H.` finds `BSH`; it is done in `DeviceMatcher`, the one place that turns given properties into criteria, so `find_devices` and `delete_device` agree. A brand that is empty after the cleaning counts as not given.
- **Keys.** `brand_key` and `model_key` are `TextKeys.Fold` of the brand and the model: trimmed, each run of whitespace collapsed to one space, lowercase and without accents (decompose to FormD, drop the non-spacing marks, compose to FormC). Nothing else is ignored (no hyphens). `Fold` is the **only** text comparison of the project, so the duplicate rule of `register_device`, `find_devices` and `delete_device` always agree: `Philips Hué` and `Philips Hue` are one device. SQLite's `COLLATE NOCASE` ignores the case of ASCII letters only and cannot remove accents, which is why the keys are computed in code and stored.
- **Deletion.** `delete_device` finds its device the same way as `find_devices`: `DeviceMatcher` (in `Finding`) turns the given properties into criteria and is the one place that decides what matches, used by `DeviceFinder` and `DeviceRemover`. Then `IInventory.DeleteAsync(device_id)` deletes the row (manuals and URLs go by cascade) and `IManualsIndex.DeleteByDeviceAsync` deletes its chunks, both in one transaction; if the index fails the device row comes back. If the device disappears between the lookup and the delete (another request got there first), the answer is `device_not_found`. The manual files are never touched.
- **Lookups.** `find_devices` does not use SQL to compare text. `IInventory.ListAllAsync` returns every device and `DeviceFinder` filters in memory with `TextKeys.Fold`. A home inventory has tens of devices, so reading them all costs nothing; if it ever grows, store a folded key per column (`device_type`, `color`, `location`), so lookups can use SQL instead of reading every device.
- **Update.** `DeviceUpdater` loads the device, applies `DeviceNormalizer` to the values sent and compares the manuals given with the current ones: **new** (not in the device), **removed** and **kept**. Only the new manuals are checked (allowed, exists, not used by another device) and read, chunked and embedded, before the transaction opens; the kept ones are neither checked nor read again, so a file that was deleted from the manuals folder does not block other changes. If nothing differs from the stored values, nothing is written. Then, in one transaction: update the row, replace the rows of `device_manuals` and the lists, `IManualsIndex.DeleteByManualAsync(deviceId, manual)` for each removed manual, `AddAsync` for the new chunks, commit. The collision with another device is checked on the keys of the normalized brand and model (the device itself is excluded), and the `UNIQUE` constraints decide races, as in registration. If the device is deleted between the lookup and the write, the answer is `device_not_found`.
- **Searching.** `ManualSearcher` checks that the device exists, then asks the index. If the index returns nothing it looks the device up once more, so a device deleted in between is `device_not_found` and not "no extractable text".
- **Races.** If two registrations pass the checks together, the `UNIQUE` constraints decide: the loser is rolled back and gets `device_exists` or `manual_in_use`, as if the check had caught it.
- **Queries.** They are built from an allowlist of columns, with every value as a parameter. A value from the client is never concatenated into SQL.
- Manuals and URLs come back in the order they were given (insertion order).
- **Per connection:** `PRAGMA foreign_keys = ON` (SQLite does not keep it between connections), WAL mode and a busy timeout.
- **Schema versions** with `PRAGMA user_version` and ordered migration scripts embedded in the assembly, applied in a transaction at startup. A database with a newer version than the code stops the startup with a clear message. The system is not in production yet, so the schema may still change without migrating old data: delete the database and register again.

**Manuals index.** One port, `IManualsIndex`:

- `AddAsync(deviceId, chunks)`: adds the chunks of a device.
- `DeleteByDeviceAsync(deviceId)`: deletes every chunk of a device.
- `DeleteByManualAsync(deviceId, manual)`: deletes the chunks of one manual of a device, for `update_device`.
- `SearchAsync(deviceId, queryText, count)`: the **only** way to read content. It takes a `device_id` and always filters by it, so a search can never return chunks of another device. There is no method that searches without it. The index embeds `queryText` exactly as given and returns the best `count` chunks (`Chunk`: text, `device_id`, manual, page), most relevant first, by cosine similarity; equal scores keep the order of the manual. It has no minimum score. An empty list means the device has no chunks.

A chunk has its text, `device_id`, manual (relative path), page (from 1), the embedding and a deterministic id made of `device_id`, manual path and chunk number (counted over the whole manual, not per page), so a retry never duplicates chunks. 

**Initial implementation (decided for now).** The chunks live in the same SQLite file as the inventory, in their own tables, and the vectors are stored as a binary column. A search reads the chunks of the device and computes the cosine similarity in C#: it is always for one device, so that is some hundreds or a few thousand vectors and takes milliseconds. No vector database and no native extension. The model and the alternatives are in section 9.

```sql
CREATE TABLE chunks (
  chunk_id  TEXT PRIMARY KEY,         -- device_id, manual path and chunk number
  device_id INTEGER NOT NULL,
  manual    TEXT NOT NULL,            -- relative path
  page      INTEGER NOT NULL,         -- from 1
  text      TEXT NOT NULL,
  embedding BLOB NOT NULL
);
CREATE INDEX ix_chunks_device ON chunks(device_id);
CREATE TABLE index_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);  -- embedding model and dimensions
```

Sharing the file couples the two stores, so the coupling is kept as small as possible: `chunks.device_id` has **no foreign key** to `devices`, only `IManualsIndex` touches these tables, and nothing else knows how a chunk is stored. Moving the index elsewhere later means writing another implementation of the port, with the compensation rules below.

**What goes into the index.**

- Only PDF manuals. The text is read page by page (PdfPig; see the open question *PDF text extraction* for what is still to check with real manuals).
- Chunks never cross a page, never cut a word and never cut a sentence (unless a single sentence is longer than the maximum). `TextChunker` builds them in three steps, because the text of a PDF has no paragraphs: PdfPig returns lines wrapped by the layout.
    1. **Rebuild the units.** A unit is a bullet (`•`), a numbered step (`1.`), a warning (`⚠`), an entry of a settings list (`Factory Reset - Return Shelly to...`) or a heading with the text that follows it. Wrapped lines are joined, and the hyphen of a wrapped word is undone (`sin-⏎gle` becomes `single`, while `Wi-⏎Fi` keeps its hyphen; `sunrise/⏎sunset` gets no space). A heading is a short line without final punctuation followed by a clearly longer line, so the lines of a narrow column are not taken for headings. A line that ends in `:` stays with the next one. A blank line, if the PDF has one, ends a unit.
    2. **Pack whole units.** Units go into a chunk until the next one would pass the target (`TextChunker.DefaultTarget`, 500 characters). A list of steps or bullets may pass the target, up to the maximum (`DefaultMax`, 1500), so a procedure stays in one chunk. A unit longer than the maximum is cut by sentences, and a sentence longer than the maximum by words.
    3. **Join the tiny ones.** A chunk under `MinimumSize` (100) joins its neighbour when both fit in the maximum.
- **No overlap.** Chunks are whole units, so there is no text repeated between chunks. Overlap was tried (200 characters, aligned to units) and did not help: it dragged the end of an unrelated procedure or a page header into the next chunk, and pushed a relevant chunk out of the best results. Repeating the line that introduces a list when the list has to be cut was also tried and changed nothing on the manuals available.
- **Target of 500.** Tried with 500, 700 and 1000 on the sample manuals. The manuals of Shelly are lists of one-line entries (`Factory Reset - ...`), and a one-line entry gets lost in a chunk of 900 characters of other settings: for "reset factory settings" the right chunk was the 6th with 1000 and the 1st with 500. A bigger target did not pay off for procedures, because the list rule already keeps them whole. The value is a tuning decision that real manuals will change (see the open question *Regenerating the chunks and rebuilding the index* in section 9).
- **Known limits.** A procedure that crosses a page, or that has the text of a diagram between two of its steps (Fibaro, installation of the Switch 2: steps 1 to 3, the wiring diagrams, steps 4 to 7), is split in two chunks, and a client that needs it whole asks for several results. The garbage that the PDF itself has (page numbers glued to headings, letters of another alphabet, labels of diagrams) is not cleaned.
- Each chunk is embedded locally through `IEmbeddingGenerator`, with the model decided in the next subsection.
- The index records the name and dimensions of the embedding model that built it. A different configured model is a startup error, because vectors of two models cannot be compared: the index must be rebuilt.
- A manual with no extractable text (for example a scanned one) registers without error and produces no chunks. A device may also have no manuals at all: it registers with no chunks.
- **A manual that cannot be read** (damaged, protected by a password, not a PDF) is not the same as one with no text. `IManualTextExtractor` throws a specific exception (`ManualUnreadableException`, with the reason) that the guard does **not** turn into `failed`: the service turns it into a `manual_unreadable` error. Reading is the check, and it is done for every manual that is allowed and exists, together with the other validations, so the client gets every reason at once; the text read is kept for chunking, so the file is not read twice. The call is rejected as a whole, so nothing is saved, because a device registered with a manual that cannot be searched would look complete and silently fail later. As built in `PdfPigTextExtractor`: `PdfDocumentEncryptedException` is a password, and `PdfDocumentFormatException` is a file that is not a PDF when it has no `%PDF-` header in its first 1024 bytes, or a damaged one otherwise. Any other exception stays `failed`; the reason `it cannot be read` is not produced yet.
- Registering reads and embeds every manual synchronously inside the call.

**Consistency between the two stores.**

- **Registration.** The heavy work comes first: read the manuals, chunk them, embed the chunks. Then open the SQLite transaction, insert the device, add the chunks to the index and commit. If anything fails, roll back the transaction and delete the chunks already added for that `device_id`. No device is left half registered.
- **Deletion.** Open the transaction, delete the device row (the manuals and URLs go by cascade), delete its chunks from the index and commit. If the index fails, roll back: nothing is deleted. If the commit fails after the chunks were deleted, the index is re-derived for that device from its manuals (the index is rebuildable) and the failure is reported.
- **Update.** The heavy work for the new manuals comes first. Then open the transaction, update the device, delete the chunks of the removed manuals, add the new chunks and commit. If anything fails, roll back: the device and the chunks of the removed manuals are as they were, and the chunks already added for the new manuals are deleted.
- **With the initial implementation** the index is in the same SQLite file, so both stores are written in one transaction and the compensation steps above are not needed: commit or rollback covers all, and the service deletes the chunks in the same transaction as the device. The steps above are the rule for any implementation that lives elsewhere.

### Embedding model

**Decided.** `BAAI/bge-small-en-v1.5`: English only, 384 dimensions, about 130 MB, run on the CPU with ONNX Runtime (`BgeEmbeddingGenerator`, with `Microsoft.ML.Tokenizers` for the WordPiece vocabulary). Pooling is the CLS token followed by L2 normalization. A passage is embedded as is; a search query goes with the prefix `BgeEmbeddingGenerator.QueryPrefix` in front, which is the job of `ManualSearcher` (UC-08): it receives the prefix as a constructor argument (`Program.cs` passes `QueryPrefix` for the bge model and nothing for the model-free `hash` generator, which would otherwise match on the prefix words), trims the query and puts the prefix in front.

**Memory and speed (2026-11-26).** The ONNX Runtime memory arena and memory pattern are off: with them the session keeps the biggest buffers it ever needed for as long as it lives, which is the life of the server. The model embeds `Tinker__EmbeddingBatchSize` texts per run (1 to 64, default 4) with `Tinker__EmbeddingThreads` CPU threads (0 to 256, default 0: ONNX Runtime chooses). A value out of range stops the start. Neither changes the vectors (a model test checks it), so they can be tuned without rebuilding the index. They tune memory and speed; they put no limit on how long a registration takes. Measured on a 12-CPU x86 machine (one run each, 21 copies of the sample manuals): memory after registering went from 716 MiB with the arena and a batch of 16 to between 339 and 456 MiB in three runs, and registering 20 manuals from 98 s to about 70 s. The search responses of the two builds were identical for 15 queries over the three sample manuals. Not measured: the NAS, and the effect of the threads.

**Why this one, and the assumption behind it.** All the manuals are assumed to be in English, and the queries too (the client can translate a query before calling `search_manuals`). Under that assumption an English model is the simplest: smaller, faster to register on the NAS CPU, and better at retrieval than `all-MiniLM-L6-v2`. The functional question about languages (functional spec, section 3) is answered *English only, for now*.

**Where the files are.** `model.onnx` (from the `onnx` folder of the Hugging Face repository) and `vocab.txt`, in `Tinker__EmbeddingModelPath` (default `models/bge-small-en-v1.5`, git-ignored). **In Docker they are in the image:** a `model` stage of the `Dockerfile` downloads them from a pinned revision of the Hugging Face repository (`MODEL_REVISION`) and checks their SHA-256 (`MODEL_ONNX_SHA256`, `MODEL_VOCAB_SHA256`), so a changed file fails the build. The stage comes before the code, so the build cache keeps it. The image grows by about 130 MB, the first build needs network and the container needs none. Mounting a folder on `/models/bge-small-en-v1.5` replaces them. Outside Docker the files are downloaded by hand (README). If they are missing the server does not start and says which files and where to get them. `Tinker__EmbeddingModel=hash` selects the model-free `HashEmbeddingGenerator` (matches by shared words), used by the contract tests and handy in development.

**How to change the model later (for example, Spanish manuals).** The model is behind `IEmbeddingGenerator<string, Embedding<float>>`, so the change is local:

1. Pick a multilingual model. First candidate: `intfloat/multilingual-e5-small` (384 dimensions too, about 470 MB, about 100 languages). Its prefixes are `passage: ` and `query: `, not the bge ones.
2. Write another generator (copy `BgeEmbeddingGenerator`: only the files, the pooling, the prefixes and the model id change; e5 uses mean pooling, not CLS), and select it by name in `Program.cs` and in `StartupValidation.CheckEmbeddingModel`.
3. Move the query prefix behind the generator (an `EmbedQuery` method or a decorator) if `ManualSearcher` does not do it yet, so the search code does not know the model.
4. **Rebuild the index.** Vectors of two models cannot be compared, and the index records the model that built it (`index_meta`). The server refuses to start with another model over an index built with the first, and says so. Until a rebuild command exists (section 9), delete the data folder (or the `chunks` and `index_meta` tables) and register the devices again. Dimensions may differ: nothing else stores them.
5. Run the model tests with the new model: `dotnet test --project tests/Tinker.ModelTests`. They check the dimensions, that similar meaning is closer than different meaning, batching and truncation.
6. Update this subsection and the language answer in the functional spec.

If the manuals become mixed (some English, some Spanish), a multilingual model is also the right choice: a single model must embed queries and passages of the whole index.

## 8. Testing

**Proposed.**

- **Every acceptance criterion of the functional spec is covered by at least one automated test.** One test may cover several criteria, and one criterion may need several tests; what matters is that none is left uncovered. Each test says which criteria it covers with `[Trait("Criteria", "UC-03.AC1, UC-03.AC2")]`, using the IDs of the criteria tables in the functional spec (`UC-NN.ACn`), so coverage can be reviewed against the spec and checked by a tool: `dotnet run --project tools/Traceability` (from the repository root) fails when a marker names a criterion that is not in the spec, and warns about criteria that no test names (`--strict` makes that an error too). It is not part of the solution or of CI yet.
- **Approach: a test pyramid, and each rule is tested at the lowest level that can show it.** Many fast unit tests, fewer integration tests, very few contract, model and system tests. A test goes up a level only when the lower one cannot prove the thing, never to repeat what a lower level already proves. The number of tests per level is a consequence of this, not a goal: tests are not added to fill a level, and a test that only repeats another one at a higher level is deleted.
- **Where does a test go?** Ask in this order and stop at the first yes:
  1. Does it need the whole image, a restart or the real startup? **System.**
  2. Is it about what the client sees (names, schemas, `snake_case`, `isError`, the trace context)? **Contract.**
  3. Does it need the real embedding model? **Model.**
  4. Does it need real SQLite, real files, links or real PDFs (UNIQUE constraints, races, cascade, real rollback, the search by device, confinement, what is inside a PDF)? **Integration.**
  5. Otherwise (a validation, a normalization, a comparison, a message, a mapping of errors, what the service calls and in which order, what runs inside the transaction) it is a rule of the code. **Unit**, with the ports replaced by doubles.
- **Doubles replace ports, not rules.** A unit test of a service uses in-memory doubles of `IInventory`, `IManualsIndex`, `ITransactionRunner`, `IManualFiles` and `IManualTextExtractor` (`Fakes.cs`). The double of a port may be simplified, so what the real implementation does (the rules of `ManualPathResolver`, the constraints of SQLite, the extraction of a PDF) is also tested in its own unit or integration test. A failure injected into a double (a port that throws) is how unit tests check that the client gets `failed` without the cause and that the transaction is rolled back; that the database really rolls back is an integration test.
- **Signs that the pyramid is inverted**, to review from time to time: an integration test that builds a SQLite file and PDFs but only asserts a message or an error code; several integration tests that differ only in the input value (make it a unit `Theory`); a contract test that repeats a business scenario instead of checking the contract; a service that cannot be unit tested because it uses the disk or the clock directly (extract a port, as was done with `IManualFiles`).
- **Levels**, from the base of the pyramid up. Each level is its own project, so it can be run, cached and skipped on its own:
  - **Unit** (`Tinker.UnitTests`): no disk, network or database. Besides the pure logic (text chunking, normalization, `TextKeys`, `DeviceMatcher`, the text rules of `ManualPathResolver`), the services (`DeviceRegistrar`, `DeviceUpdater`, `DeviceRemover`, `DeviceFinder`, `ManualSearcher`) are tested here with in-memory doubles of their ports (`Fakes.cs`, including a manuals folder with its files and what the extractor reads from them): validation, normalization, collisions, error mapping, logging and what runs inside the transaction. It is the widest level and runs in about a second, so it is the one to run all the time.
  - **Integration** (`Tinker.IntegrationTests`): the services wired with the real pieces: a SQLite file, a temporary manuals folder, real PDFs. No network and no language model; the embeddings are `HashEmbeddingGenerator`, which returns predictable vectors, and failures are injected by wrapping a port (`TestEnvironment`). Only what needs the real infrastructure belongs here, and it should be clear from the test which piece makes it necessary: UNIQUE constraints and races, cascade, ids that are not reused, real rollback, the search by device in the index, links, and real PDFs (what the extractor finds in them, and why a file is unreadable).
  - **Model** (`Tinker.ModelTests`): integration with the real embedding model (`BgeEmbeddingGeneratorTests`, `SearchManualsRealModelTests`). It links `TestEnvironment.cs` from the integration tests instead of copying it, and is skipped when the model files are not in `models/bge-small-en-v1.5` (or `TINKER_TEST_MODEL_PATH`).
  - **Contract** (`Tinker.ContractTests`): see below.
  - **Smoke** (`scripts/smoke-test.sh [image]`, built): starts the image as a non-root user on throwaway volumes and checks that the container stays up (so the startup validation passed: data and manuals folders, database, embedding model), that `/health` answers and that `tools/list` returns the five tools. It does not call the tools. The same script runs on a developer machine and in CI.
  - **System** (`Tinker.SystemTests`, built): the Docker image running as a container with its own volumes, a non-root user and the real embedding model, spoken to with an MCP client as a real client would. It has no reference to the code. It checks what only the whole image can show, not what the lower levels already prove: (1) a device goes through register, find, search by meaning, update and delete; (2) the inventory and the search index survive a restart of the container; (3) a data folder that cannot be written stops the container with a message that says how to fix it. Each test starts its own container, driven with the `docker` command line (`TinkerContainer`). They are skipped unless `TINKER_SYSTEM_IMAGE` names the image (for example `docker build -t tinker-mcp . && TINKER_SYSTEM_IMAGE=tinker-mcp dotnet test --project tests/Tinker.SystemTests`), so a plain `dotnet test` stays green without Docker.
- **CI** runs every level on pushes to `main` and on pull requests (the system tests only outside pull requests), plus a scheduled run every 15 days; see "Continuous integration" below.
- **Contract tests** (`Tinker.ContractTests`) start the real server in process and talk to it with the SDK's MCP client over HTTP. They check the contract, not the business rules (those are proved below). They check: the tools exposed and their names, the schemas (`snake_case`), that a rejection is `isError` with the same structured content and a success is not, the annotations and titles, that the structured content is also sent as text, that no raw exception ever travels, and that the trace context is propagated (a call with `traceparent` continues that trace, a call without it starts its own).
- **Confinement tests** use a temporary folder with real links, including a link in an intermediate directory and a link whose target does not exist.
- **Startup validation tests:** a data folder that is not writable, a missing manuals folder, a database with a newer schema, an index built with another model.
- **Fixtures:** the public PDFs of `tests/fixtures/manuals/`, plus small generated PDFs for the edge cases (no text, several pages).
- **Framework:** xUnit v3 on Microsoft.Testing.Platform (`global.json` selects the runner, so `dotnet test` works on the .NET 11 SDK). Analyzer rules about test names and inline arrays are relaxed in `tests/Directory.Build.props`.

### Continuous integration

**Built.** `.github/workflows/ci.yml`, on GitHub Actions (`ubuntu-latest`). It runs on every push to `main`, on every pull request, on a schedule (every 15 days: the 1st and the 16th of each month, because cron cannot express an exact interval) and when launched by hand (`workflow_dispatch`), with read-only permissions (`contents: read`). It builds, tests and checks that the Docker image starts; it does not publish anything yet.

```
build ─┬─ unit-tests
       ├─ integration-tests
       ├─ contract-tests
       └─ model-tests

image  (after build: docker build, smoke test and, outside pull requests, system tests; nothing is pushed)

ci-ok  (after every job above, even if they fail: the only check the branch protection requires)
```

- **`build`** compiles the solution once in Release and uploads the test executables as an artifact.
- **`image`** builds the Docker image (the `Dockerfile` compiles and downloads the model by itself, so none of the jobs above exercises it) and runs the smoke test and the system tests (section 8) against it. It needs `build` only for the system test executable, and pushes nothing. The system tests are the slowest level, so they do not run on pull requests: they run on pushes to `main`, on the scheduled run and when launched by hand. **The gap before merge** is covered by launching the workflow by hand on the branch (`workflow_dispatch`) when a change touches the image, the data folders or the startup; the branch protection does not require it.
- **The four test jobs** need `build` and run in parallel, each one a test project (section 8, "Levels"). A failure in one does not hide the result of the others.

**Decisions behind it.**

- **The test jobs run the executables from the artifact; they do not run `dotnet test`.** An xUnit v3 project is an executable and its `bin/Release` holds everything it needs (the tests, the code under test, its dependencies, the fixtures). So the jobs do not check out the repository, restore NuGet packages or compile. Using `dotnet test --no-build` on a fresh machine was tried first and finds no test projects, because the test framework packages are not there.
- **The artifact is a `tar.gz`**, not the folders, because `upload-artifact` drops the executable bit of the test binaries.
- **Only the `linux-x64` native libraries are kept.** ONNX Runtime and SQLite ship their libraries for every platform (about 300 MB per test project); the jobs run on `linux-x64`, so `build` deletes the rest before packing. The artifact goes from about 600 MB to about 60 MB. It lives one day.
- **`contract-tests` is the only job that checks out**, and only `src/Tinker.Mcp` (sparse checkout): `WebApplicationFactory` resolves the content root of the server project from the source tree.
- **`model-tests` downloads the embedding model** from the same pinned Hugging Face revision as the `Dockerfile`, checks its SHA-256 and caches it (`actions/cache`, keyed by the revision), then points the tests to it with `TINKER_TEST_MODEL_PATH`. So CI tests the model the image ships, and a changed file fails the job. The `Dockerfile` is the only place where the revision and the checksums are written: `build` reads its `ARG` lines and passes them to `model-tests` as job outputs.
- **One SDK version.** Every job installs the SDK that `global.json` pins; the package carries `global.json` so the test jobs, which do not check out, can read it.
- **`scripts/ci-run-tests.sh <project>` runs each level.** It is in the package and runs the executable with a JUnit result file (`test-results/<project>.xml`), writes the totals to the job summary and **fails when no test ran or all of them were skipped**, which a test runner reports as success (a deleted level, a filter that matches nothing, a model or an image that was not provided). Each job uploads `test-results/` as an artifact (`test-results-<level>`, 7 days, also when it fails) so a red run can be diagnosed without running it again.
- **`ci-ok` is the only required check.** It depends on every job, runs `if: always()` and fails unless all of them succeeded, so a skipped or cancelled job cannot let a broken change through and the branch protection does not change when jobs are added or renamed (only its `needs` does).
- **Hardening.** Every action is pinned to a full commit SHA with its version in a comment, and `.github/dependabot.yml` proposes the updates (actions, the NuGet packages of every project, the SDK pinned in `global.json`, and the base images of the `Dockerfile`). Updates come **grouped**, one pull request per ecosystem, **every 15 days** (the 1st and the 16th of each month; Dependabot has no exact 15-day interval, so it uses `interval: cron`). **Held back:** new major versions of the actions (they can change inputs, artifact format or runtime, so they are updated by hand after reading the release notes and running the workflow; this leaves the Node 20 deprecation warnings to be handled by hand) and new major versions of the SDK in `global.json` and of `dotnet/sdk` and `dotnet/aspnet` (the project is .NET 11, section 1; moving to .NET 12 is a decision, not an update) and new major versions of `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*` and `Microsoft.Data.Sqlite` (these packages have the version of the runtime they go with, so their next major needs .NET 12). Remove those ignores when the project moves on to the next .NET, which is one change: `global.json`, `Directory.Build.props`, the base images, the versions of the platform packages in `Directory.Packages.props` (and the ones the new SDK already brings, which must then be removed or NuGet reports NU1510, an error here) and the ignores. **Package versions are written once, and fixed.** `Directory.Packages.props` (NuGet central package management) holds the version of every package; the `.csproj` files reference a package without a version, so all projects share it and an update is one line. The versions are exact, with no ranges. Reason: a range (`1.*`, `10.*`) changes the build silently at every restore, and Dependabot only reacts to it when a newer version falls outside the range. A new package is added to `Directory.Packages.props` first, then referenced. The `Dockerfile` copies `Directory.Packages.props` before it restores, and the NuGet entry of `dependabot.yml` is `directory: /`. **Not verified:** that Dependabot finds every project through the central file with `directory: /`; after the next run, the update job in the repository's Insights, Dependency graph, Dependabot page should list the packages of all the projects; if it does not, list the project folders again in `directories`. Every job has `timeout-minutes`. `concurrency` cancels the obsolete run of a pull request and never one on `main` or a scheduled one. The workflows use `pull_request`, never `pull_request_target`, and no job has a secret or a write permission.
- **The scheduled run** exists because the pipeline depends on things the code does not control (Hugging Face, the base images of the `Dockerfile`, NuGet). With no change in the code, it catches an expired download, a moved dependency or a changed base image, and it runs the system tests too.

**Release dry run (built).** `.github/workflows/release-dry-run.yml` (its actions are pinned to a SHA and the job has a timeout, as in `ci.yml`), launched by hand only (`workflow_dispatch`, inputs: `version`, default `0.0.1-beta.1`, and `platforms`, default `linux/amd64,linux/arm64`). It rehearses publishing and **publishes nothing**: it has `contents: read` only, no login to any registry and no permission to write packages. It starts a throwaway registry (`registry:2`) as a service of the job, builds the image for both platforms with buildx and pushes it there, pulls it back, runs the smoke test and the system tests on the pulled image, and writes the tags it would get (`latest` only for versions without a suffix, so never for a beta) and the pushed manifest to the job summary. It shows that the multi-platform build, the tags and the push and pull mechanics work. It cannot show that the login and the permissions of the real registry work; that is only known by publishing for real.

To build several platforms without compiling under emulation, the `build` and `model` stages of the `Dockerfile` run on the build machine (`--platform=$BUILDPLATFORM`): their output (portable assemblies, which carry the native libraries of every platform, and data files) is the same for every architecture.

**Release (decided 2026-11-25, written after Vault's, which has published three versions with it; this one has not run yet).** The skills `container-delivery` and `versioning-and-changelog` are followed. Delivery here means **publishing the image**; deploying it to the NAS is the user's compose file. `.github/workflows/release.yml` is started only by a tag `v*.*.*`, with `contents: read` by default; only `publish` gets `packages: write` and only `github-release` gets `contents: write`. Every action is pinned to a commit.

```
verify          the tag is a semantic version with a leading v; the commit is in main; CHANGELOG.md has an entry for the version;
                unit, integration and contract tests
publish         build image (amd64, loaded) → smoke test (also checks the version in the handshake) → system tests →
                vulnerability scan (warns) → login with GITHUB_TOKEN → push linux/amd64 + linux/arm64 with SBOM and provenance
github-release  GitHub release whose notes are the entry of that version in CHANGELOG.md (pre-release if the version has a suffix)
```

- **Where.** GitHub Container Registry, `ghcr.io/javixulo/tinker`, as in Vault (the workflow logs in with its built-in token, so no registry password is stored; the package is linked to the repository; public pulls have no limit). The repository and the package are public: the tag list can be read without logging in. A new package on ghcr.io can start **private**; if it does, it is made public by hand in its settings once, because the workflow cannot change it (the job summary says so).
- **Publish what was tested.** The image is built first and loaded; the multi-platform push reuses its layers from the build cache, so the `linux/amd64` image that is published is the one the smoke and system tests ran against (the digest is in the job summary). The `linux/arm64` image comes from the same sources but **is not run** by the workflow; the first arm64 host to run it is the real test. The `build` and `model` stages run on the build machine (`--platform=$BUILDPLATFORM`), so arm64 does not compile under emulation. The model tests are not repeated: the image carries the model and the system tests run on the image with the real model.
- **Tags** (`docker/metadata-action`): the full version (`0.1.0`, never moves), `0.1` (moves with patches), `0` (skipped by the action while the major is 0) and `sha-<commit>`; `latest` only for a version without a suffix, and it points at **the last stable version published** (tag stable versions in order: tagging an older line after a newer one would move it back). A pre-release is expected to get only its full version and the commit tag (to be confirmed by the dry run, which prints the tags). A version tag is never overwritten: a wrong release is replaced by the next one.
- **Evidence.** SBOM and provenance (`mode=max`) are attached by the build. Signing is out of scope until someone verifies signatures.
- **Vulnerability scan.** Trivy over the tested image, HIGH and CRITICAL with an available fix, written to the job summary. It never blocks: a release must not depend on when someone else publishes a fix.
- **Rehearsal.** `release-dry-run.yml` (manual, no credentials, no write permission) does the multi-platform build, the push to a throwaway registry, the pull back and the tests, and prints the tags it would publish. It cannot test the login, the permissions or the visibility on ghcr.io: **the first real release is the only test of those, so it is a pre-release (`v0.1.0-beta.1`)**.
- **Not run.** The workflow has not run on GitHub, and `actionlint` is not installed on the development machine, so neither it nor `release-dry-run.yml` has been linted.
- **Failed release.** The workflow that runs is the file at the tagged commit, so a fix to the workflow must be in `main` before the tag is made. If it fails before `publish` pushes, nothing was published: delete the release and the tag and repeat with the same number. If the image was already pushed (any tag of the version exists in the registry), the number is spent: publish the next one. If only `github-release` failed, re-run that job (it edits the release if it exists and creates it if not). A release created from the web page exists before its image does and stays so if the run fails.
- **Continuous delivery to the NAS** is out of scope by decision: the pipeline never touches the NAS (it would need a credential for it, and the NAS is not reachable from GitHub). The NAS pulls: the user sets `TINKER_VERSION` and runs `docker compose pull && docker compose up -d`. Following `latest` with an automatic updater is possible but not recommended: pin an exact version and update on purpose.
- **Rolling back.** The previous image stays on the host until removed; going back is setting the previous `TINKER_VERSION`. Exact version tags never change meaning. The running version is in the image label, the logs and the MCP handshake (section 10).
- **Who can release.** The tag starts the release, so creating a tag matching `v*` is the release permission, restricted to the owner with a tag ruleset (section 10, "Repository settings").

## 9. Open questions

| # | Title | Description | Status | Priority | Comments |
|---|---|---|---|---|---|
| 1 | Docker network | Whether the MCPs share a Docker network among themselves, besides the network each one joins to reach Seq. | Half-way | Low | Settled: Seq has its own compose file and its own network `seq-net`, which MCPs join as an external network (section 3). Still open: whether the MCPs also share a network among themselves. |
| 2 | Startup validation failure | Whether a failed check (e.g. the data folder is not writable) makes the process exit with an error, or lets it run degraded. Exiting makes the problem obvious but, with `restart: unless-stopped`, the container will restart in a loop. | Open | Medium | Leaning to exit, since an MCP that cannot write its data is not useful. To be confirmed by the user. |
| 3 | stdio transport | Whether to keep stdio as a second transport for local development and for clients that cannot speak HTTP. The MCP logic would not change, only the startup. | Open | Low | |
| 5 | Vector store | Which store holds the vectors. Section 7 decides for now: same SQLite file, vectors as a binary column, similarity in C#. Reasons to look again: searching across all manuals at once, with tens of thousands of chunks or more; a different backend; or the coupling of both stores in one file turning out to hurt. Alternatives: SQLite with `sqlite-vec` (same file, but needs a native extension; its state from .NET is to be checked in a spike), Qdrant (a separate container and two stores to keep consistent) and pgvector (a PostgreSQL container, the heaviest option for the NAS). FTS5 can be added to any SQLite option for hybrid search with keywords. | Half-way | Low | Settled: the initial implementation of section 7. Still open: whether and when to move to another backend, which depends on the triggers above. |
| 6 | `Microsoft.Extensions.VectorData` | Microsoft's abstraction over vector stores (`VectorStore`, typed collections, filters, hybrid search), with connectors for SQLite, Qdrant and Postgres. It would be the way to switch the backend: write a second implementation of `IManualsIndex` on top of it. To check before using it: the maturity of the connectors (some may still be preview), the native SQLite dependency that some report as flagged by NuGet, and whether the SQLite connector can share a transaction with the inventory (probably not, since it manages its own connection), which would bring back the compensation rules of section 7. | Blocked | Low | Blocked by the question *Vector store*: it only matters if the backend is changed. |
| 7 | PDF text extraction | Whether PdfPig (`ContentOrderTextExtractor`, page by page) gives good enough text with real manuals. | Half-way | Medium | Settled: PdfPig is in use and works with the fixtures and with generated PDFs, including one without text. Still to check with real manuals: multi-column pages and quality of the text. |
| 8 | Case-insensitive file systems | On macOS (and Windows) `Shelly/A.pdf` and `shelly/a.pdf` are the same file but would be stored as two manuals. Whether the resolver should canonicalize the case. | Open | Low | Docker on the NAS is Linux, so it is not a problem there. |
| 9 | Registration time | Registering reads and embeds every manual inside the call. On the NAS CPU, a long manual may take longer than a client is willing to wait. Options: leave it synchronous and measure, report progress, or register in the background and return before the content is searchable (which would change the functional contract). | Open | Medium | Needs a measurement on the NAS with real manuals. |
| 10 | Limit of `n_results` | Whether there is an upper bound, so a client cannot ask for the whole manual. Today there is none: a value above the number of chunks returns every chunk of the device. Proposal to consider: a maximum of 20, and a value above it is either rejected with `invalid_fields` or clamped to 20, with `has_more` in the result so the client knows there is more. The same idea as the question *Pagination of find_devices*. | Open | Medium | Changes the contract of `search_manuals` (the result and, if rejected, the messages): to be said explicitly when decided. |
| 11 | Regenerating the chunks and rebuilding the index | How the manuals are split into chunks (target size, maximum size, overlap, how lists are kept together) is a tuning decision that real manuals will keep changing, and a new value only applies to devices registered afterwards: the existing chunks are stale, and today the only way to refresh them is to delete the data and register the devices again. The same step is needed when the embedding model changes or the index is damaged. A command to re-derive the chunks and the index from the inventory and the manuals folder (the manual files stay as they are and are read again), for one device or all of them, would let the values be tuned without losing the inventory. To decide: whether it is an MCP tool (the client could call it by mistake, and it can take minutes) or an operator command or startup mode outside the MCP; whether the chunking values are arguments or configuration; and what a search returns while the rebuild runs. | Open | Medium | To decide by the user. Sections 7 and "Embedding model" point here. |
| 12 | Access control and rate limits | The server is plain HTTP on the local network. Who may call which tool, and how often, is undecided. Today the user's reverse proxy is assumed to handle TLS, authentication and limits (section 4, Transport), but the server does not validate `Host` or `Origin`, and it is not confirmed which interface it listens on. The tool design guidance expects the spec to state both, even if the answer is "the proxy handles it". | Half-way | High | **Decided (2026-11-26):** the user's reverse proxy (nginx; whether plain nginx or Nginx Proxy Manager is still to be seen) does the access control. Still to do when it is configured: Tinker reachable only through the proxy (a shared Docker network instead of the published port; other containers of that network would still reach it), unknown `Host` names rejected, HTTPS and authentication, requests with an `Origin` header rejected, a `proxy_read_timeout` above the 60 s default (registering manuals can take longer, see the question *Registration time*) and no response buffering. To check: `AllowedHosts` (`src/Tinker.Mcp/appsettings.json` only sets log levels, so there is no host filtering) and the listening interface (`ASPNETCORE_HTTP_PORTS=8080` in the `Dockerfile` binds every interface of the container, and `docker-compose.yml` publishes `${TINKER_PORT:-8080}:8080` on every interface of the host). |
| 13 | Pagination of `find_devices` | `find_devices` without properties returns every device, with no limit or next page. The user doubts there will be more than about 100 devices, so it may never matter. Proposal to consider: an optional `limit` (default 100, maximum 100), an `offset`, and `total` and `has_more` in the result. | Open | Low | Changes the contract of `find_devices` if adopted: to be said explicitly. Related to the question *Limit of `n_results`*. |

## 10. Versioning and the public surface

**Decided (2026-11-25), as in Vault.**

- **Format.** MAJOR.MINOR.PATCH, with an optional pre-release suffix (`0.1.0-beta.1`). The project is **0.x**: anything may break, and a breaking change raises the MINOR. The step to 1.0.0 is a decision of the user ("the surface is stable and we support it"), not an automatic result. The tag is checked with the pattern `v<number>.<number>.<number>` plus an optional suffix; strict SemVer (no leading zeros, no empty parts) is not enforced, and build metadata (`+…`) is rejected.
- **One source: the git tag** (`v0.1.0`). The release workflow strips the `v` and passes it to the build as `VERSION`, which sets the assembly version (`-p:Version`) and the image label `org.opencontainers.image.version`. The running server reports it in its logs and traces (`service.version`) and in the MCP handshake (`serverInfo.version`). The library reports `0.0.0.0` unless it is told, so `Program.cs` sets it from the assembly, a contract test checks it and the smoke test checks it on the image. Without the argument (a local or CI build) the version is `0.0.0-dev`, so nobody mistakes it for a release. No version is typed in a project file.
- **The next number** is proposed from the Conventional Commit types since the last tag (`fix` gives patch, `feat` gives minor, a breaking marker gives minor while 0.x), and **the user confirms it** after reading the list against the public surface below.
- **Changelog.** `CHANGELOG.md`, newest first, with an `Unreleased` section; breaking changes first with how to migrate; changes to configuration, the user, the port or the mounted folders always mentioned. The GitHub release notes are the entry of that version, copied by the workflow (the heading must start with `## [x.y.z]`; the workflow fails before publishing if the entry is missing or empty). Published entries are not edited.
- **Public surface** (a change here is breaking; anything else is internal): the **tool names**, their parameters and result schemas, and the error codes and messages (section 6); the **container's configuration**: the environment variables (`Tinker__DataPath`, `Tinker__ManualsPath`, `Tinker__EmbeddingModelPath`, `Tinker__EmbeddingModel`, `Tinker__EmbeddingBatchSize`, `Tinker__EmbeddingThreads` and the OpenTelemetry variables; **their names are final**), the folders `/data`, `/manuals` and `/models/bge-small-en-v1.5`, the port 8080, `/mcp` and `/health`, and the non-root user; the **image name and tags**; the data on disk (the database schema and its migrations: today the schema may still change without migrating old data, which a 0.x version allows; from the first stable version a change of schema needs a migration); the fields of the telemetry (section 3); and the **variables of the compose file** (`TINKER_VERSION`, `TINKER_DATA`, `TINKER_MANUALS`, `TINKER_PORT`, `TINKER_UID`, `TINKER_GID`, `TINKER_EMBEDDING_BATCH_SIZE`, `TINKER_EMBEDDING_THREADS`), because renaming one breaks an existing `.env`. The server name in the handshake is also kept (`Tinker.Mcp`).
- **The first publication** is `0.1.0-beta.1`: nothing was published before it, so changes to configuration made until then break nobody.

**Repository settings (set by hand on GitHub; not checked).** The release depends on them, and the API tools cannot change them. Order matters: a required check can only be chosen after the workflow has run once.

- A ruleset for `main`: require a pull request and the check `CI result` (`ci-ok`), block force pushes and deletion; the owner is a bypass actor.
- A ruleset for the tags `v*` that only the owner can create: the tag starts the release.
- Actions, General: require approval for workflows of pull requests from forks (all external contributors), and keep the default workflow permission read-only.
- After the first publication: the package `ghcr.io/javixulo/tinker` is public (or consumers are told to log in) and linked to the repository.
