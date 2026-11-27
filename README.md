# 🏠 Tinker

**Your home's memory, one question away.**

Washing machine, TV, dimmers, the remote nobody understands, the router, the heat pump... Every device came with a manual you will never read until the day something stops working. And on that day the PDF is somewhere in an old email, the invoice is in a drawer, and you can't remember if the warranty ended last month or next year. 😩

**Tinker fixes that.** It is a small server that plugs into your AI assistant (Claude or any other MCP client) and gives it two superpowers:

- 📦 **A living inventory of your home.** Every device with its brand, model, color, room, invoice, useful links, purchase date, warranty end date and status. All in one place, always up to date.
- 📖 **The manuals, ready to answer.** Drop in the PDF manuals and Tinker reads them, so you can ask in plain language and get the exact passage that answers you, with the manual and page it comes from.

No dashboards to learn, no forms to fill in, no folders to dig through. You just talk to your assistant. 💬

## ✨ What you can ask

> 🔧 *"How do I factory reset the Shelly Dimmer 2?"*
> Tinker finds the right passage in the right manual and tells you the page.

> 🛒 *"I just bought a Samsung QE55Q60 TV. Here is the manual."*
> It is registered, its manual is read and indexed, and it is ready to answer questions.

> 🛋️ *"What do I have in the living room?"*
> The whole list, in seconds.

> 🛡️ *"Which of my devices are still under warranty?"*
> Find out before you pay for a repair that was free.

> 🚚 *"The dryer moved to the garage."*
> The inventory follows your home.

> 💸 *"I sold the old TV, remove it."*
> Gone from the inventory, with its indexed manuals. (Your files are never touched.)

## 🎯 Everyday scenarios

| Scenario | How Tinker helps |
|---|---|
| 🚨 **"Something is broken and I don't know why"** | Ask about the error code or the symptom and get the troubleshooting section of that device's manual |
| ⚙️ **"I need to set it up again"** | Pairing, resetting, changing the wifi, programming a schedule: the steps, straight from the manual |
| 🧾 **"Do I still have the warranty? Where is the invoice?"** | Purchase date, warranty end date and invoice are stored with the device |
| 🔎 **"What exactly is this model?"** | Brand, model, color and room, so you can order the right spare part or accessory |
| 🏷️ **"I'm selling or giving it away"** | Mark it `inactive` or `archived`, or remove it, and keep the record you want |
| 🧰 **"I've got a lot of smart home gadgets"** | One inventory for dimmers, actuators, sensors and remotes, each with its own manuals |
| 🔁 **"Moving house or reorganizing"** | Update locations and statuses with a sentence instead of editing a spreadsheet |

## 🧰 Tools

Behind the scenes your assistant uses five tools. You never have to call them yourself.

| Tool | What it does | Typical use |
|---|---|---|
| `register_device` | Adds a device to the inventory, optionally with its manuals. Brand and model are required; the rest is optional. The content of the manuals is read and indexed. A device with the same brand and model, or a manual that already belongs to another device, is rejected | "I bought a Samsung QE55Q60 TV, here is its manual" |
| `find_devices` | Looks devices up by any of their properties (brand, model, location, status…), or lists all of them when no property is given. It returns each device with its `device_id` | "What do I have in the living room?", "Which devices are out of warranty?" |
| `search_manuals` | Searches the manuals of one device by meaning, not just by exact words, and returns the best excerpts with the manual and page they come from | "How do I reset the wifi of the dimmer?" |
| `update_device` | Edits a registered device: its data, its manuals or both. Only the fields sent change; lists such as manuals and URLs are replaced as a whole | "It moved to the garage", "Add the installation guide to this device" |
| `delete_device` | Deletes a device and everything indexed from its manuals. The manual files themselves are never touched. It cannot be undone, so the client asks you first | "I sold the TV, remove it" |

## 🛡️ Built to be trusted

- 🤝 **It never guesses.** Deleting only happens when what you said points to exactly one device; otherwise Tinker gives the candidates back so the assistant can ask you.
- 🔤 **It understands how you write.** Case, accents and extra spaces don't matter: `Philips Hué` and `philips  hue` are the same device.
- 🧯 **Nothing is left half done.** If a registration, update or deletion fails, everything is rolled back, and the assistant gets a clear explanation of what happened and what to do next.
- 📁 **Your files are yours.** Tinker only reads inside your manuals folder and never modifies, moves or deletes your PDFs.
- 🔒 **It runs at home.** Your inventory and the search over your manuals stay on your own machine or NAS: there is no cloud service and no API key.

The full behaviour (use cases and acceptance criteria) is in [`docs/functional_spec.md`](docs/functional_spec.md).

📚 Want to know how it works, run it or contribute? Go to the [technical guide](#technical-guide).

---

# Technical guide

Tinker is written in **C# / .NET 11**.

## Requirements

- .NET SDK 11 (see `global.json`), or Docker.
- To run outside Docker: the embedding model files in `models/bge-small-en-v1.5/`: `model.onnx` (from the `onnx` folder) and `vocab.txt` of [BAAI/bge-small-en-v1.5](https://huggingface.co/BAAI/bge-small-en-v1.5). They are git-ignored. Without them the server does not start (or set `Tinker__EmbeddingModel=hash` for development). The Docker image downloads them itself when it is built.

## Run

```bash
dotnet run --project src/Tinker.Mcp
```

The server speaks MCP over Streamable HTTP at `http://localhost:5000/mcp` (`/health` is the health check). To use it from Claude Code:

```bash
claude mcp add --transport http tinker http://localhost:5000/mcp
```

### Configuration

Environment variables, using the ASP.NET Core convention:

| Variable | Default | Meaning |
|---|---|---|
| `Tinker__DataPath` | `data` | Folder for the SQLite database (inventory and manuals index) |
| `Tinker__ManualsPath` | `manuals` | Folder with the PDF manuals. Manuals are given to the tools as paths relative to it |
| `Tinker__EmbeddingModelPath` | `models/bge-small-en-v1.5` | Folder with `model.onnx` and `vocab.txt` |
| `Tinker__EmbeddingModel` | `bge-small-en-v1.5` | `hash` selects a model-free generator for development and tests |
| `Tinker__EmbeddingBatchSize` | `4` | Texts the model reads in one run, 1 to 64. A bigger batch uses more memory |
| `Tinker__EmbeddingThreads` | `0` | CPU threads of the model, 0 to 256. `0` lets ONNX Runtime choose; lower it to leave CPU for other services |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | unset | When set, logs and traces are exported through OTLP. For Seq: `http://seq:5341/ingest/otlp` |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (`http/protobuf` in `docker-compose.yml`) | Seq only accepts `http/protobuf`: set it when exporting to Seq |
| `OTEL_RESOURCE_ATTRIBUTES` | unset | For example `deployment.environment.name=production`; the server adds `service.name` and `service.version` itself |

On start the server validates its configuration (data folder writable, manuals folder readable, database schema, embedding model) and logs what is wrong and how to fix it.

## Docker

Tinker runs as a container. Save this as `docker-compose.yml` (the repository's own file has the same shape, with more comments) and give a value to the `${VARIABLES}`:

```yaml
services:
  tinker:
    image: ghcr.io/javixulo/tinker:${TINKER_VERSION}   # required: latest, or a version such as 0.1.0
    container_name: tinker-mcp
    restart: unless-stopped
    user: "${TINKER_UID:-1000}:${TINKER_GID:-1000}"   # who the server runs as: must be able to write the data folder
    group_add:
      - "${TINKER_MANUALS_GID:-${TINKER_GID:-1000}}"  # extra group, to read the manuals (see "Permissions")
    ports:
      - "${TINKER_PORT:-7843}:8080"    # MCP endpoint (Streamable HTTP) and /health
    cap_drop: [ALL]
    security_opt: ["no-new-privileges:true"]
    environment:
      TZ: ${TZ:-Europe/Madrid}
      Tinker__EmbeddingBatchSize: ${TINKER_EMBEDDING_BATCH_SIZE:-4}   # more uses more memory
      Tinker__EmbeddingThreads: ${TINKER_EMBEDDING_THREADS:-0}        # 0: automatic
      # Logs and traces (OpenTelemetry). Empty exports nothing; logs still go to `docker logs`.
      OTEL_SERVICE_NAME: tinker-mcp
      OTEL_EXPORTER_OTLP_ENDPOINT: ${OTEL_EXPORTER_OTLP_ENDPOINT:-}                  # for Seq: http://seq:5341/ingest/otlp
      OTEL_EXPORTER_OTLP_PROTOCOL: ${OTEL_EXPORTER_OTLP_PROTOCOL:-http/protobuf}     # Seq only accepts http/protobuf
      OTEL_RESOURCE_ATTRIBUTES: deployment.environment.name=production
    volumes:
      - ${TINKER_DATA:-./data}:/data             # SQLite database, on a local disk
      - ${TINKER_MANUALS:-./manuals}:/manuals:ro # your manuals, read-only
```

Give the variables a value in a `.env` next to the file (copy `.env.template`), in the environment of the shell that runs compose, or in the variables of your stack editor (Portainer, Dockge...):

| Variable | Example | Meaning |
|---|---|---|
| `TINKER_VERSION` | `latest` | **Required.** Version of the image: `latest` (the newest stable one) or an exact version such as `0.1.0`. A pre-release needs its exact version. See [Updating](#updating-and-going-back) |
| `TINKER_DATA` | `/srv/tinker/data` | Optional (default `./data`). Folder of the database, on a **local disk** (SQLite locking is not reliable over NFS or SMB). See [Permissions](#permissions) |
| `TINKER_MANUALS` | `/srv/tinker/manuals` | Optional (default `./manuals`). Your PDF manuals, mounted read-only. See [Permissions](#permissions) |
| `TINKER_PORT` | `7843` | Optional (default 7843). Port Tinker listens on, on this machine |
| `TINKER_UID`, `TINKER_GID` | `1000`, `1000` | Optional (default 1000 and 1000). User id and main group id the container runs as. They must be able to write the data folder |
| `TINKER_MANUALS_GID` | `1005` | Optional (default: `TINKER_GID`). Id of one more group for the container, the one that can read your manuals |
| `TZ` | `Europe/Madrid` | Optional. Timezone |
| `TINKER_EMBEDDING_BATCH_SIZE` | `4` | Optional (default 4). Texts the model reads in one run, 1 to 64; a bigger batch uses more memory |
| `TINKER_EMBEDDING_THREADS` | `0` | Optional (default 0, automatic). CPU threads of the model |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://seq:5341/ingest/otlp` | Optional. Where to send logs and traces; empty sends nothing |

An example of the matching `.env`, with a value for every variable. Only the first one is required; change the others only if you need to:

```ini
# Required
TINKER_VERSION=latest

# Optional (these are the defaults, except the data and manuals folders, TINKER_MANUALS_GID and the OTLP endpoint, which are examples)
TINKER_DATA=/srv/tinker/data
TINKER_MANUALS=/srv/tinker/manuals
TINKER_PORT=7843
TINKER_UID=1000
TINKER_GID=1000
TINKER_MANUALS_GID=1005
TZ=Europe/Madrid
TINKER_EMBEDDING_BATCH_SIZE=4
TINKER_EMBEDDING_THREADS=0
OTEL_EXPORTER_OTLP_ENDPOINT=http://seq:5341/ingest/otlp
```

```powershell
Copy-Item .env.template .env      # then edit it
docker compose up -d
docker compose logs tinker        # "Startup checks passed"
```

Then, to use it from Claude Code:

```bash
claude mcp add --transport http tinker http://localhost:7843/mcp
```

### Permissions

Most problems running Tinker are permission problems, so check this before the first start.

#### Who needs permissions

The process inside the container reads and writes your folders. Linux judges it only by the **numbers** the compose file gives it:

| Compose | What it is | In Tinker |
|---|---|---|
| `user: "UID:GID"` | The user id and the **main** group id of the process | `TINKER_UID`, `TINKER_GID` |
| `group_add` | **Additional** group ids of the process | `TINKER_MANUALS_GID` |

- **The groups a user has on the host do not reach the container.** If your NAS user belongs to three groups, the container still gets only the main group of `user` and those listed in `group_add`. Docker does not read the users or groups of the host.
- **A user on the host is optional.** A dedicated one (say `tinker`) is a good way to reserve a user id and to set permissions in the web interface of a NAS, but the container never logs in as it. What counts is that the folders grant access to the numbers in the compose file.
- **The people and apps that add manuals** (you, a sync app) need write access to the manuals folder for their own work. Tinker needs none.

To find the numbers on the host: `id <user>` lists the id of a user and of all its groups, and `getent group <group>` shows the id of a group (the third field).

#### What each folder needs

"The container" means: its user id, its main group **or** one of its additional groups. One of them is enough.

| Folder in the container | What | The container needs |
|---|---|---|
| `/data` (`TINKER_DATA`) | The folder | Read, write and enter (`rwx`) |
| | The files already in it (`inventory.db` and the temporary files SQLite puts next to it) | Read and write (`rw`) |
| `/manuals` (`TINKER_MANUALS`) | The folder itself | Read and enter (`r-x`) |
| | Every subfolder on the way to a manual | Enter (`x`); give it `r-x` like the folder, so you can treat them all the same |
| | Every PDF | Read (`r`) |
| `/models/bge-small-en-v1.5` | The model, already inside the image | Nothing to do. If you mount your own files there, they must be readable |

- **Write access to the manuals is not needed.** They are mounted read-only (`:ro`), so Tinker cannot change them even if its group could.
- **New manuals and subfolders need the same access.** Check what a file gets when you or your sync app create it: a default ACL on the folder, or the setgid bit with a suitable umask, keeps the group.
- **The folders above the mounted ones do not matter** to the container: Docker mounts the folder itself.
- **The database belongs to the user that created it.** Tinker creates `inventory.db` writable by its owner and readable by the rest (`rw-r--r--`, unless a default ACL or the umask says otherwise). **If you change `TINKER_UID` later**, give the files to the new user (`chown -R <uid>:<gid> <data folder>`). If you do not, the server starts normally and then every change fails.

#### Example

Data is writable by the group 1001 and the manuals are readable by the group 1005; the container runs as the user 1009:

```ini
TINKER_UID=1009
TINKER_GID=1001
TINKER_MANUALS_GID=1005
```

The process gets the user id 1009 and the groups 1001 and 1005. If one group can do both, leave `TINKER_MANUALS_GID` unset. If you need yet another group, add a line to `group_add`.

#### Checking and troubleshooting

Look at what the container really got (the container must be recreated, not only restarted, for a change to apply):

```bash
docker inspect tinker-mcp --format 'user={{.Config.User}} group_add={{.HostConfig.GroupAdd}}'
docker exec tinker-mcp id        # only while the container is running
```

And at what the folders grant (`getfacl` also shows ACLs, which `ls -l` hides; with an ACL, the group digits of `ls -l` are the ACL mask, not the rights of the owning group):

```bash
ls -ldn /path/to/folder
getfacl -n /path/to/folder
```

| What you see | Cause |
|---|---|
| The server does not start: `The data folder '/data' is not writable by the running user` | The container cannot write the data folder |
| The server does not start: `The manuals folder '/manuals' cannot be read by the running user` | The container lacks `r-x` on the manuals folder. A missing `group_add` is a likely reason |
| `manual_not_found` for a file that is there | The path is wrong (it is relative to the manuals folder, and case matters), or the container cannot enter one of its subfolders |
| A `failed` error when registering or updating with a manual | Among other causes, the PDF is not readable by the container |
| It starts, but every change gives a `failed` error and the log says `attempt to write a readonly database` | The database files are not writable by the container, for example after changing `TINKER_UID` |

At start Tinker checks the data folder and the manuals folder themselves, not the subfolders and files inside them.

### Running notes

The container runs as a non-root user, has a health check on `/health` and `restart: unless-stopped`, and needs no network access. TLS and authentication are the job of your reverse proxy, not Tinker's. To build the image from this repository instead of pulling it, uncomment `build: .` in the repository's compose file and run `docker compose up -d --build`; the first build needs network to download the embedding model (about 130 MB, from a pinned revision, checked against its SHA-256).

**The image.** `ghcr.io/javixulo/tinker` (GitHub's container registry), for `linux/amd64` and `linux/arm64`. Tags: `0.1.0` (never moves), `0.1` (the newest patch), `latest` (the newest stable version; never a pre-release) and `sha-<commit>`. The package is public: no login is needed.

#### Updating and going back

With `TINKER_VERSION=latest`, update by downloading the image again and recreating the container: `docker compose pull && docker compose up -d` (in a stack editor, its pull action and then up). **`latest` does not update by itself**: a restart, or `up` alone with the image already on the machine, keeps the old one. With an exact version, change `TINKER_VERSION` and run the same two commands.

Read [`CHANGELOG.md`](CHANGELOG.md) before updating: it says what changed in each version, configuration included. While the version is 0.x, a new minor version may break compatibility, and `latest` follows it too. **Before updating, note the version you run and copy the data folder** (with the container stopped). To go back, set that exact version; if the new version changed the database schema, the old one does not start until you also restore the copy. Pulling updates the image only, not your compose file: when the changelog mentions a change to it, edit yours. The version that runs is in the image label (`docker inspect --format '{{ index .Config.Labels "org.opencontainers.image.version" }}' tinker-mcp`), in the logs and in the MCP handshake.

The image bundles the embedding model [BAAI/bge-small-en-v1.5](https://huggingface.co/BAAI/bge-small-en-v1.5) (MIT license), unmodified, so the semantic search works offline out of the box.

## Tests

```bash
dotnet test
```

Every acceptance criterion of the functional spec is covered by at least one automated test. The tests are split by level, each in its own project:

| Project | Level | What it checks |
|---|---|---|
| `Tinker.UnitTests` | Unit | Pure logic: no disk, network or database |
| `Tinker.IntegrationTests` | Integration | The services with a real SQLite database, files and PDFs (embeddings are the deterministic hash generator) |
| `Tinker.ModelTests` | Integration with the model | The real embedding model; skipped when its files are missing |
| `Tinker.ContractTests` | Contract | The real server in process, spoken to with an MCP client over HTTP: tool names, schemas and messages |
| `Tinker.SystemTests` | System | The Docker image running with the real model: a device's whole life, data surviving a restart, a clear failure on a bad data folder. Skipped unless `TINKER_SYSTEM_IMAGE` is set |

Run one level with `dotnet test --project tests/Tinker.UnitTests` (or the name of another project). The system tests need the image: `docker build -t tinker-mcp . && TINKER_SYSTEM_IMAGE=tinker-mcp dotnet test --project tests/Tinker.SystemTests`.

### Continuous integration

[![CI](https://github.com/javixulo/Tinker/actions/workflows/ci.yml/badge.svg)](https://github.com/javixulo/Tinker/actions/workflows/ci.yml)

GitHub Actions ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) builds the solution once on every push to `main` and every pull request, then runs the four test projects in parallel. Another job builds the Docker image, runs `scripts/smoke-test.sh` against it and, on pushes to `main`, the system tests (you can run it too: `docker build -t tinker-mcp . && scripts/smoke-test.sh tinker-mcp`). The model tests download the same pinned embedding model that the Docker image ships. Pushing a version tag (`v0.1.0`, or `v0.1.0-beta.1` for a pre-release) runs [`release.yml`](.github/workflows/release.yml): it checks the tag and the [changelog](CHANGELOG.md) entry, tests the image and publishes it to `ghcr.io/javixulo/tinker` for `linux/amd64` and `linux/arm64`, then creates the GitHub release. Before tagging, rename `Unreleased` in `CHANGELOG.md` to the version and commit it. A manual workflow, [`release-dry-run.yml`](.github/workflows/release-dry-run.yml), rehearses publishing the image (multi-platform build, tags, push and pull, smoke and system tests) against a throwaway registry inside the job. It publishes nothing. More detail in [`docs/technical_spec.md`](docs/technical_spec.md), section 8.

## Architecture

```
 MCP client (Claude, ...)
        │  Streamable HTTP  (/mcp)
        ▼
┌────────────────────────────────────────────┐
│ Tinker.Mcp   ASP.NET Core host             │
│   tool classes (thin: bind args, call a    │
│   service, return its result)              │
└──────────────────┬─────────────────────────┘
                   ▼
┌────────────────────────────────────────────┐
│ Tinker.Core   all the logic                │
│   services: DeviceRegistrar, DeviceFinder, │
│   DeviceUpdater, DeviceRemover,            │
│   ManualSearcher                           │
│   ports: IInventory, IManualsIndex,        │
│   IManualTextExtractor, embeddings         │
└─────┬──────────────────┬──────────────┬────┘
      ▼                  ▼              ▼
  SQLite             Manuals folder   Embedding model
  inventory +        (PDF, read-only) (bge-small-en-v1.5,
  manuals index                        ONNX, on CPU)
```

The main design decisions:

- **Thin tools, logic in `Tinker.Core`.** A tool class only binds arguments and calls a service, so the contract and the rules can be tested apart. Services depend on ports, so storage, text extraction and the embedding model can be swapped.
- **Stateless Streamable HTTP.** The MCP is a long-lived service with no per-client state in memory: a restart breaks nothing. Each MCP is meant to run as its own Docker container.
- **One SQLite file** (`<data>/inventory.db`) holds the inventory and the manuals index. Chunks are linked to their device by `device_id`, and a register, update or delete is a single transaction, so the two stores never disagree. A search always filters by `device_id`: it can never return chunks of another device.
- **Local semantic search.** On registration each manual is read page by page (PdfPig), split into chunks that follow the structure of the text (bullets, steps, warnings), embedded locally and stored as vectors. A query is embedded the same way and ranked by cosine similarity. There is no LLM and no API key in the server.
- **Manuals stay inside the manuals folder.** One class, `ManualPathResolver`, resolves every manual path that comes from a client, rejecting absolute paths, `..`, encoded sequences and links that lead outside the folder, and without revealing whether a file outside exists.
- **Outcomes are data, not protocol errors.** Rejections, empty results and unexpected failures are normal tool results (`error` or `errors`, `message`) with `snake_case` fields; no raw exception ever reaches the client.
- **Observability.** Logs go to the console on stderr and, when configured, to OpenTelemetry (OTLP) together with traces, with one span per tool call. A `traceparent` header from the client is continued, so one step is one trace. Manual content is never recorded.

More detail, including the storage schema, the error codes and messages, and how to change the embedding model, is in [`docs/technical_spec.md`](docs/technical_spec.md).

## Project structure

| Path | What |
|---|---|
| `src/Tinker.Mcp/` | The MCP server: host and HTTP transport (`Program.cs`), the five tool classes (`Tools/`), configuration (`TinkerOptions.cs`) and startup validation (`Startup/`) |
| `src/Tinker.Core/` | The logic, one folder per area: `Registration/`, `Finding/`, `Updating/`, `Removal/`, `Searching/` (a service per tool), `Manuals/` (path confinement, PDF text extraction, chunking), `Embeddings/` (BGE ONNX and hash generators), `Storage/` (SQLite inventory and manuals index, ports), `Models/` (devices, results, messages) |
| `tests/Tinker.UnitTests/` | Unit tests: pure logic |
| `tests/Tinker.IntegrationTests/` | Integration tests: services with real SQLite, files and PDFs |
| `tests/Tinker.ModelTests/` | Integration tests with the real embedding model (skipped without its files) |
| `tests/Tinker.ContractTests/` | Contract tests through a real MCP client |
| `tests/Tinker.SystemTests/` | System tests against the Docker image |
| `tests/fixtures/manuals/` | Public PDF manuals used by the tests |
| `docs/` | Functional spec (what the tools do) and technical spec (how and why) |
| `Dockerfile`, `docker-compose.yml` | Container image and deployment |
| `scripts/smoke-test.sh` | Smoke test of the Docker image: it starts, `/health` answers and the tools are listed |

## License

[MIT](LICENSE) © Francisco Javier Montoro Martínez ([LinkedIn](https://www.linkedin.com/in/javier-montoro/)). The Docker image also bundles the BAAI/bge-small-en-v1.5 model, which is MIT-licensed too.
