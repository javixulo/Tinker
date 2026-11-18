# Home Manuals MCP — Technical Specification

> Status: **draft v0.6**
> Sibling document: [functional_spec.md](./functional_spec.md)
>
> Each section says whether it is **Decided** (agreed with the user) or **Proposed** (a draft to review, section by section). Sections 1 to 4 are decided; 5 to 8 are proposed; open questions are in section 9.

## 1. Technology rule

**Decided.** Everything in this project is written in C# on .NET 10. No other language or runtime, in the server, the tests or the tooling.

- Target framework `net10.0`, C# 14 (the SDK default), pinned in `global.json`.
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
tests/fixtures/manuals/    Public PDF manuals used by the tests
scripts/smoke-test.sh      Smoke test of the Docker image (section 8)
docs/                      Functional and technical specs
```

**Status.** `Tinker.Core`, `Tinker.Mcp` (web SDK, `WithHttpTransport`, `MapMcp("/mcp")`, `/health`), `Tinker.UnitTests`, `Tinker.IntegrationTests`, `Tinker.ModelTests` and `Tinker.ContractTests` exist, with `register_device` (UC-01 and UC-02), `delete_device` (UC-03 and UC-04), `find_devices` (UC-05 to UC-07) and `search_manuals` (UC-08 and UC-09) implemented: all four tools exist. The changes that `update_device` brought to the existing tools are built (optional manuals, normalized values, new statuses, `manual_unreadable`, brand cleaning in lookups, no-manuals message in `search_manuals`); `update_device` (UC-10 to UC-12) is built too: all five tools exist. The placeholder `ping` tool is gone. Configuration is read from the `Tinker` section (`Tinker__DataPath`, `Tinker__ManualsPath`). OpenTelemetry is exported through OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

## 3. Observability

**Decided.**

- **OpenTelemetry (OTLP) for logs, traces and metrics.** Every MCP in the system exports through OTLP, so there is one pipeline and one place to look. Traces are correlated across services with the W3C `traceparent` header, provided the client propagates it.
- **Seq is the backend**, as a single container that accepts OTLP. Chosen over the Grafana stack (Loki, Tempo, Prometheus, collector) because it is one container and the target NAS has limited resources. A collector can be added later without changing the MCPs.
- **Console logging to stderr stays on** next to OTLP. Why: `docker logs` keeps working when Seq is down or unreachable.
- Exporter configuration uses the standard variables (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME`).

**Proposed additions: what is recorded.**

- One span per tool call, with the tool name, the outcome (`ok` or the error codes) and the `device_id` when known. Inside it, spans for the inventory access, the manuals index (index, delete, search) and the registration, which also records whether it was committed or rolled back.
- Spans and logs record identifiers and parameters, never the content of manuals, chunks or embeddings.
- Log levels: `Information` for a registration, update or deletion requested and completed; `Warning` for every rejection (with its code and values) and for a lookup that finds nothing; `Error`, with the exception, for every `failed`.
- The server never returns an exception to the client (section 6), so the full exception and the trace id are in the log, and only the trace id may travel in the message.
- Whether the query text of `search_manuals` and the values of rejected registrations may be recorded depends on the PII question in section 9.

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
- **Configuration through environment variables**, using the ASP.NET Core convention (`Tinker__DataPath`, `Tinker__ManualsPath`; names provisional), plus `ASPNETCORE_HTTP_PORTS` and the OpenTelemetry variables of section 3. Secrets go in an `env_file`, never in the image.
- **Timezone** through the `TZ` variable, as in all the user's images.
- **Healthcheck.** An HTTP endpoint (`/health`) used by the Docker `HEALTHCHECK`.
- **Restart policy.** `restart: unless-stopped`.
- **Image tags.** `latest`, and the user updates by hand. Accepted trade-off: a pull can bring in a breaking change without warning. Keeping the previous image around allows a quick rollback.

**Proposed additions.**

- The manuals folder is mounted **read-only** (`:ro`). The server never writes manuals, and a read-only mount is a second line of defence for the confinement of manual paths (section 5).
- The startup validation also checks that the database schema is at the expected version (section 7). **Built:** it checks that the embedding model files exist and that the embedding model of the index is the configured one.

**Built.** `Dockerfile` (multi-stage, .NET 10 `aspnet` runtime, `curl` only for the healthcheck) and `docker-compose.yml`: `/data` (bind mount), `/manuals:ro`, the embedding model inside the image (section 7), `user:` from `TINKER_UID`/`TINKER_GID`, `TZ`, OTLP endpoint through `OTEL_EXPORTER_OTLP_ENDPOINT`, `restart: unless-stopped`, healthcheck on `/health`. Checked by building the image and running it as a non-root user: it starts, loads the model, registers a device through `/mcp`, keeps the data across a restart and rejects a duplicate. Not done: the shared Docker network and Seq (section 9), a published image.

## 5. Architecture

**Proposed.**

- **Tools are thin.** A tool class only binds the arguments, calls a service and returns its result. No rule lives in a tool class, so the contract and the logic can be tested apart.
- **Services** (in `Tinker.Core`): `DeviceRegistrar`, `DeviceFinder`, `DeviceRemover`, `ManualSearcher` and `DeviceUpdater`, one per tool. `DeviceNormalizer` is plain logic, like `ManualPathResolver`: the one place that turns the values of a client into the stored format (section 7), used by `DeviceRegistrar` and `DeviceUpdater`. **Ports** they depend on: `IInventory` (SQLite), `IManualsIndex` (section 7), `IManualTextExtractor` and the `IEmbeddingGenerator<string, Embedding<float>>` abstraction of `Microsoft.Extensions.AI`, so the embedding model can be swapped. `ManualPathResolver` is plain logic, and the only place that builds a manual path from client input (see the confinement point below).
- **No LLM and no API key in the server.** Everything that needs a model of language stays in the client. The embedding model runs locally, inside the container.
- **Stateless HTTP.** The tools are plain request and response: they use no sampling, elicitation or subscriptions. So the transport runs in the stateless mode of the SDK (in 2.x, `HttpServerSessionMode.Stateless`, the default): no session id, nothing kept in memory per client, a restart of the container breaks nothing. The MCP endpoint is `/mcp`; `/health` is separate and not an MCP endpoint.
- **Manuals stay inside the manuals folder.** Every manual path that comes from a client is resolved by one class, `ManualPathResolver`, and nothing else builds such a path, also when a file is read for indexing. It applies the rules of UC-02 in the functional spec (an absolute path, a `..` in the path, or a link that leads outside the folder is not allowed). As built: the text is judged first: a leading `/`, `\\` or `~`, a drive letter (`C:`), a NUL or an encoded sequence (`%` followed by two hex digits) is "outside the folder"; then `..` as a component (splitting on `/` and `\\`) is "`..`"; then every component is walked, following links (including chains and links to a missing target), and the real path must be inside the real manuals folder. The stored path is the **real** one relative to the folder in POSIX form, so a link inside the folder to another file inside it is stored as that file, and `shelly/./a.pdf` and `shelly/a.pdf` are the same manual. For that reason a list of manuals is reduced to its distinct real paths, keeping the order of the first appearance of each, before anything else is done with it (a manual written twice counts as one). The one non-obvious requirement is that links are followed in **every component** of the path, not only in the last one: .NET has no `realpath`, so the class walks the components. The not-allowed answer is the same whether the file exists or not. Known limit: the check and the later read are not atomic, which is acceptable for a single-user home tool, and the read-only mount of section 4 limits the damage.
- **No state in the process** besides caches that can be rebuilt. All data is in the data folder.
- **Concurrency.** Requests arrive concurrently. SQLite runs in WAL mode with a busy timeout, and every write operation (register, update, delete) is one transaction. The heavy work (reading the PDF, chunking, embedding) happens before the transaction opens, so the write lock is held briefly.

## 6. Tool contract

**Proposed.** This is the contract with the clients. Changes to it must be said explicitly (`CLAUDE.md`). The input and output models are in section 2 of the functional spec; this section covers how they travel.

**Tools:** `register_device`, `find_devices`, `search_manuals`, `delete_device` (marked `destructive` in its MCP annotations; it deletes at once, so the client confirms with the user first) and `update_device` (`idempotent`: the same call twice leaves the same device).

**Serialization.**

- JSON property names are `snake_case` (`device_id`, `device_type`, `purchase_date`, `n_results`), as the clients expect. C# types are records with `PascalCase` properties, and the server's JSON options use `JsonNamingPolicy.SnakeCaseLower`. The SDK default is `camelCase`, so a contract test checks the schemas.
- Dates are `yyyy-MM-dd` (`DateOnly`). `status` is a lowercase string (`active`, `inactive`, `archived`). An absent optional value is `null`.
- A `device_id` that comes in the `device` of `register_device` is ignored. In the `changes` of `update_device` it is an error (`invalid_fields`).
- **Absent and empty are different in `update_device`.** In `changes`, a field that is absent or `null` is not sent and keeps its value; a text that is empty or has only spaces, or an empty list, empties the field (functional spec, section 2.5). So `changes` is a `DeviceChanges` (dates as text, so an empty one clears them) deserialized without turning empty values into `null`, and `DeviceUpdater` receives "not sent" and "empty" as two different things. In `register_device` and `find_devices` a blank value still counts as not given. A contract test covers each case.
- The SDK uses the C# parameter names as the argument names of a tool, and the naming policy above does not apply to them. So the top-level parameters of `search_manuals` and `update_device` are written in snake_case in `SearchManualsTool` and `UpdateDeviceTool` (`device_id`, `query`, `n_results`, `changes`), with the analyzer rules turned off for those classes; the contract test checks the schema.
- Results are typed objects returned as the tool result, as structured content, with the schema generated from the C# types. The exact SDK settings are confirmed in a spike.

**Outcomes are data, not protocol errors.** A rejection, an empty result and `failed` are all a normal tool result with `ok` and the `errors` or `error` of the functional spec. The MCP `isError` flag is not used for them. So the client always reads the same shape.

**No raw exception ever reaches the client.** Every service call runs inside one guard that catches what is unexpected, logs it with its trace id (section 3), and returns `ok=false` with a `failed` error.

**Be tolerant on input.** Tool parameters are nullable and have defaults, and our own code validates them. A missing `query` or an empty `device` gives an `invalid_fields` result with a message the AI can act on, not an SDK-level binding error.

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

Outcome of `delete_device`: `ok`, `device` (as it was before being deleted) and `error` (one, not a list). Its `ambiguous_device` error carries the matching `devices`.

Outcome of `update_device`: `ok`, `errors` (a list, as in `register_device`) and `device` (as stored after the edit). `device_exists` and `manual_in_use` carry the other device involved.

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
- **Lookups.** `find_devices` does not use SQL to compare text. `IInventory.ListAllAsync` returns every device and `DeviceFinder` filters in memory with `TextKeys.Fold`. A home inventory has tens of devices, so reading them all costs nothing; if it ever grows, store a folded key per column (see section 9).
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

- Only PDF manuals. The text is read page by page (the library is an open question).
- Chunks never cross a page, never cut a word and never cut a sentence (unless a single sentence is longer than the maximum). `TextChunker` builds them in three steps, because the text of a PDF has no paragraphs: PdfPig returns lines wrapped by the layout.
    1. **Rebuild the units.** A unit is a bullet (`•`), a numbered step (`1.`), a warning (`⚠`), an entry of a settings list (`Factory Reset - Return Shelly to...`) or a heading with the text that follows it. Wrapped lines are joined, and the hyphen of a wrapped word is undone (`sin-⏎gle` becomes `single`, while `Wi-⏎Fi` keeps its hyphen; `sunrise/⏎sunset` gets no space). A heading is a short line without final punctuation followed by a clearly longer line, so the lines of a narrow column are not taken for headings. A line that ends in `:` stays with the next one. A blank line, if the PDF has one, ends a unit.
    2. **Pack whole units.** Units go into a chunk until the next one would pass the target (`TextChunker.DefaultTarget`, 500 characters). A list of steps or bullets may pass the target, up to the maximum (`DefaultMax`, 1500), so a procedure stays in one chunk. A unit longer than the maximum is cut by sentences, and a sentence longer than the maximum by words.
    3. **Join the tiny ones.** A chunk under `MinimumSize` (100) joins its neighbour when both fit in the maximum.
- **No overlap.** Chunks are whole units, so there is no text repeated between chunks. Overlap was tried (200 characters, aligned to units) and did not help: it dragged the end of an unrelated procedure or a page header into the next chunk, and pushed a relevant chunk out of the best results. Repeating the line that introduces a list when the list has to be cut was also tried and changed nothing on the manuals available.
- **Target of 500.** Tried with 500, 700 and 1000 on the sample manuals. The manuals of Shelly are lists of one-line entries (`Factory Reset - ...`), and a one-line entry gets lost in a chunk of 900 characters of other settings: for "reset factory settings" the right chunk was the 6th with 1000 and the 1st with 500. A bigger target did not pay off for procedures, because the list rule already keeps them whole. The value is a tuning decision that real manuals will change (see the open question about regenerating the chunks in the functional spec).
- **Known limits.** A procedure that crosses a page, or that has the text of a diagram between two of its steps (Fibaro, installation of the Switch 2: steps 1 to 3, the wiring diagrams, steps 4 to 7), is split in two chunks, and a client that needs it whole asks for several results. The garbage that the PDF itself has (page numbers glued to headings, letters of another alphabet, labels of diagrams) is not cleaned.
- Each chunk is embedded locally through `IEmbeddingGenerator`, with the model decided in the next subsection.
- The index records the name and dimensions of the embedding model that built it. A different configured model is a startup error, because vectors of two models cannot be compared: the index must be rebuilt.
- A manual with no extractable text (for example a scanned one) registers without error and produces no chunks. A device may also have no manuals at all: it registers with no chunks.
- **A manual that cannot be read** (damaged, protected by a password, not a PDF) is not the same as one with no text. `IManualTextExtractor` throws a specific exception (`ManualUnreadableException`, with the reason) that the guard does **not** turn into `failed`: the service turns it into a `manual_unreadable` error. Reading is the check, and it is done for every manual that is allowed and exists, together with the other validations, so the client gets every reason at once; the text read is kept for chunking, so the file is not read twice. The call is rejected as a whole, so nothing is saved.
- Registering reads and embeds every manual synchronously inside the call.

**Consistency between the two stores.**

- **Registration.** The heavy work comes first: read the manuals, chunk them, embed the chunks. Then open the SQLite transaction, insert the device, add the chunks to the index and commit. If anything fails, roll back the transaction and delete the chunks already added for that `device_id`. No device is left half registered.
- **Deletion.** Open the transaction, delete the device row (the manuals and URLs go by cascade), delete its chunks from the index and commit. If the index fails, roll back: nothing is deleted. If the commit fails after the chunks were deleted, the index is re-derived for that device from its manuals (the index is rebuildable) and the failure is reported.
- **Update.** The heavy work for the new manuals comes first. Then open the transaction, update the device, delete the chunks of the removed manuals, add the new chunks and commit. If anything fails, roll back: the device and the chunks of the removed manuals are as they were, and the chunks already added for the new manuals are deleted.
- **With the initial implementation** the index is in the same SQLite file, so both stores are written in one transaction and the compensation steps above are not needed: commit or rollback covers all, and the service deletes the chunks in the same transaction as the device. The steps above are the rule for any implementation that lives elsewhere.

### Embedding model

**Decided.** `BAAI/bge-small-en-v1.5`: English only, 384 dimensions, about 130 MB, run on the CPU with ONNX Runtime (`BgeEmbeddingGenerator`, with `Microsoft.ML.Tokenizers` for the WordPiece vocabulary). Pooling is the CLS token followed by L2 normalization. A passage is embedded as is; a search query goes with the prefix `BgeEmbeddingGenerator.QueryPrefix` in front, which is the job of `ManualSearcher` (UC-08): it receives the prefix as a constructor argument (`Program.cs` passes `QueryPrefix` for the bge model and nothing for the model-free `hash` generator, which would otherwise match on the prefix words), trims the query and puts the prefix in front.

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

- **Every acceptance criterion of the functional spec is covered by at least one automated test.** One test may cover several criteria, and one criterion may need several tests; what matters is that none is left uncovered. Each test says which criteria it covers (a trait or a comment), so coverage can be reviewed against the spec.
- **Levels.** Each level is its own project, so it can be run, cached and skipped on its own:
  - **Unit** (`Tinker.UnitTests`): pure logic, no disk, network or database (text chunking, brand normalization). The pure rules of a service belong here, not in an integration test.
  - **Integration** (`Tinker.IntegrationTests`): the services wired with the real pieces: a SQLite file, a temporary manuals folder, real PDFs. No network and no language model; the embeddings are `HashEmbeddingGenerator`, which returns predictable vectors, and failures are injected by wrapping a port (`TestEnvironment`).
  - **Model** (`Tinker.ModelTests`): integration with the real embedding model (`BgeEmbeddingGeneratorTests`, `SearchManualsRealModelTests`). It links `TestEnvironment.cs` from the integration tests instead of copying it, and is skipped when the model files are not in `models/bge-small-en-v1.5` (or `TINKER_TEST_MODEL_PATH`).
  - **Contract** (`Tinker.ContractTests`): see below.
  - **Smoke** (`scripts/smoke-test.sh [image]`, built): starts the image as a non-root user on throwaway volumes and checks that the container stays up (so the startup validation passed: data and manuals folders, database, embedding model), that `/health` answers and that `tools/list` returns the five tools. It does not call the tools. The same script runs on a developer machine and in CI.
  - **System** (not built yet): the Docker image running, spoken to as a client would, with the real model, for complete flows. Slow, so only on `main` and tags.
- **CI** runs the four projects on every push to `main` and every pull request; see "Continuous integration" below.
- **Contract tests** (`Tinker.ContractTests`) start the real server in process and talk to it with the SDK's MCP client over HTTP. They check: the tools exposed and their names, the schemas (`snake_case`), that every outcome is a normal result and not `isError`, that no raw exception ever travels, and that the trace context is propagated.
- **Confinement tests** use a temporary folder with real links, including a link in an intermediate directory and a link whose target does not exist.
- **Startup validation tests:** a data folder that is not writable, a missing manuals folder, a database with a newer schema, an index built with another model.
- **Fixtures:** the public PDFs of `tests/fixtures/manuals/`, plus small generated PDFs for the edge cases (no text, several pages).
- **Framework:** xUnit v3 on Microsoft.Testing.Platform (`global.json` selects the runner, so `dotnet test` works on the .NET 10 SDK). Analyzer rules about test names and inline arrays are relaxed in `tests/Directory.Build.props`.

### Continuous integration

**Built.** `.github/workflows/ci.yml`, on GitHub Actions (`ubuntu-latest`). It runs on every push to `main` and on every pull request, with read-only permissions (`contents: read`). It builds, tests and checks that the Docker image starts; it does not publish anything yet.

```
build ─┬─ unit-tests
       ├─ integration-tests
       ├─ contract-tests
       └─ model-tests

image  (independent of build: docker build + smoke test, nothing is pushed)
```

- **`build`** compiles the solution once in Release and uploads the test executables as an artifact.
- **`image`** builds the Docker image (the `Dockerfile` compiles and downloads the model by itself, so none of the jobs above exercises it) and runs the smoke test (section 8) against it. It does not depend on `build` and pushes nothing.
- **The four test jobs** need `build` and run in parallel, each one a test project (section 8, "Levels"). A failure in one does not hide the result of the others.

**Decisions behind it.**

- **The test jobs run the executables from the artifact; they do not run `dotnet test`.** An xUnit v3 project is an executable and its `bin/Release` holds everything it needs (the tests, the code under test, its dependencies, the fixtures). So the jobs do not check out the repository, restore NuGet packages or compile. Using `dotnet test --no-build` on a fresh machine was tried first and finds no test projects, because the test framework packages are not there.
- **The artifact is a `tar.gz`**, not the folders, because `upload-artifact` drops the executable bit of the test binaries.
- **Only the `linux-x64` native libraries are kept.** ONNX Runtime and SQLite ship their libraries for every platform (about 300 MB per test project); the jobs run on `linux-x64`, so `build` deletes the rest before packing. The artifact goes from about 600 MB to about 60 MB. It lives one day.
- **`contract-tests` is the only job that checks out**, and only `src/Tinker.Mcp` (sparse checkout): `WebApplicationFactory` resolves the content root of the server project from the source tree.
- **`model-tests` downloads the embedding model** from the same pinned Hugging Face revision as the `Dockerfile`, checks its SHA-256 and caches it (`actions/cache`, keyed by the revision), then points the tests to it with `TINKER_TEST_MODEL_PATH`. So CI tests the model the image ships, and a changed file fails the job. When the revision changes in the `Dockerfile`, change it in the workflow too.

**Not built yet.** The system tests against the image, and publishing it to GitHub Container Registry (`ghcr.io`) when a `v*` tag is pushed, with the version and `latest` as tags (not `latest` for pre-releases) and for `linux/amd64` and `linux/arm64`. The first publication would use a pre-release tag (for example `v0.1.0-rc1`): the package is born private, so it can be pulled and checked before making it public. In pull requests only `amd64` would be built, since `arm64` is emulated and slow. The system tests are slow, so they would run on `main` and tags, not on every pull request, and the smoke test stays right before publishing `latest`.

## 9. Open questions

To be decided and written here as they are made.

- **Docker network.** Whether each MCP has its own compose file and all join an external shared network (e.g. `tinker-net`, created once), or everything lives in a single compose file. The first keeps MCPs independently updatable, the second is simpler. The shared network is needed in either case for the MCPs to reach Seq.
- **Startup validation failure.** Whether a failed check (e.g. the data folder is not writable) makes the process exit with an error, or lets it run degraded. Exiting makes the problem obvious but, with `restart: unless-stopped`, the container will restart in a loop. Leaning to exit, since an MCP that cannot write its data is not useful.
- **stdio transport.** Whether to keep stdio as a second transport for local development and for clients that cannot speak HTTP. The MCP logic would not change, only the startup.
- **Traces and personal data.** Whether the system needs more than OpenTelemetry + Seq (for example trace files kept on disk, or redaction of spans). The query text and the values of a rejected registration can contain personal data, so whether they are recorded, redacted or left out is part of this question.
- **Vector store (to revisit).** Decided for now: same SQLite file, vectors as a binary column, similarity in C# (section 7). Reasons to look again: searching across all manuals at once, with tens of thousands of chunks or more; a different backend; or the coupling of both stores in one file turning out to hurt. The alternatives are SQLite with `sqlite-vec` (also in the same file, but needs a native extension; its state from .NET is to be checked in a spike), Qdrant (a separate container and two stores to keep consistent) and pgvector (a PostgreSQL container, the heaviest option for the NAS). FTS5 can be added to any SQLite option for hybrid search with keywords.
- **`Microsoft.Extensions.VectorData`.** Microsoft's abstraction over vector stores (`VectorStore`, typed collections, filters, hybrid search), with connectors for SQLite, Qdrant and Postgres. It would be the way to switch the backend if the question above is reopened: write a second implementation of `IManualsIndex` on top of it. To check before using it: the maturity of the connectors (some may still be preview), the native SQLite dependency that some report as flagged by NuGet, and whether the SQLite connector can share a transaction with the inventory (probably not, since it manages its own connection), which would bring back the compensation rules of section 7.
- **Embedding model.** Decided: `bge-small-en-v1.5`, English only (section 7, "Embedding model", with the steps to change it). To revisit if manuals in other languages appear.
- **PDF text extraction.** PdfPig is in use (`ContentOrderTextExtractor`, page by page) and works with the fixtures and with generated PDFs, including one without text. Still to check with real manuals: multi-column pages and quality of the text.
- **Unreadable manuals.** Decided: `manual_unreadable` (section 6), the call is rejected as a whole and the manual is not saved. Built in `PdfPigTextExtractor`: `PdfDocumentEncryptedException` is a password, and `PdfDocumentFormatException` is a file that is not a PDF when it has no `%PDF-` header in its first 1024 bytes, or a damaged one otherwise. Any other exception stays `failed`; the reason `it cannot be read` is not produced yet.
- **Case-insensitive file systems.** On macOS (and Windows) `Shelly/A.pdf` and `shelly/a.pdf` are the same file but would be stored as two manuals. Docker on the NAS is Linux, so it is not a problem there; to decide whether the resolver should canonicalize the case.
- **Registration time.** Registering reads and embeds every manual inside the call. On the NAS CPU, a long manual may take longer than a client is willing to wait. Options: leave it synchronous and measure, report progress, or register in the background and return before the content is searchable (which would change the functional contract).
- **Comparing text (case and accents).** Decided: `TextKeys.Fold` everywhere (section 7, "Keys"). Open: whether to store folded keys for the other columns (`device_type`, `color`, `location`) if the inventory ever grows, so lookups can use SQL instead of reading every device.
- **Limit of `n_results`.** Whether there is an upper bound (for example 20), so a client cannot ask for the whole manual. Today there is none: a value above the number of chunks returns every chunk of the device.
- **Rebuilding the index.** Whether there is a maintenance command (or a startup mode) that re-derives the index from the inventory and the manuals folder, for a changed model or a damaged index.
- **Slower model tests.** Locally they run with the rest (a few seconds). In CI they already run in their own project and job, so a slow model never blocks the fast checks.
