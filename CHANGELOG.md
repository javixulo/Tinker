# Changelog

All notable changes to Tinker are written here, newest version first. What counts as a change for a user (the public surface)
is defined in [`docs/technical_spec.md`](docs/technical_spec.md), section 10.

**How a release uses this file.** The release workflow copies the entry of the version into the GitHub release, finding the
heading `## [x.y.z] - date` (it must start exactly with `## [x.y.z]`). Before creating a tag, rename `Unreleased` to the
version and the date, add a new empty `Unreleased` above it, and commit. The workflow fails before publishing anything if the
entry is missing.

## [Unreleased]

## [0.1.0] - 2026-11-27

The first stable version: `latest` now points to it. The server works as in `0.1.0-beta.3`; what changes is the compose
file and the documentation. Nothing has to be migrated from the pre-releases, and from this version on a change of the
database schema comes with a migration. While the version is 0.x, a new minor version may still break compatibility.

### Added

- The compose file gives the container one more group with the new optional variable `TINKER_MANUALS_GID`, for manuals that
  belong to another group than the data folder. The groups of a user of the host do not reach the container. If your own
  compose file already uses `group_add`, it keeps working. Updating the image does not change your compose file.
- README: a "Permissions" section with what each folder needs, how to check it and what each symptom means.

### Changed

- `latest` is a supported value of `TINKER_VERSION`. It does not update by itself: run
  `docker compose pull && docker compose up -d`.

### Known limitations

- Some of the text of a PDF may be read wrong or not at all (ligatures, or text that is only drawn on the page), so a search
  may not find it.
- A PDF that the container cannot read gives a `failed` error, and a manual in a subfolder it cannot enter gives
  `manual_not_found`. The README says how to check the permissions.
- No authentication: do not expose the server without a proxy that controls the access.

## [0.1.0-beta.3] - 2026-11-26

### Fixed

- The server no longer starts when it cannot read the manuals folder: it only checked that the folder existed, and then
  answered `manual_not_found` for manuals that were there. The log now says to give the permission, and that in a container
  only the groups set in compose (`user`, `group_add`) count.
- README: the compose example takes the user and group from `TINKER_UID` and `TINKER_GID`.

## [0.1.0-beta.2] - 2026-11-26

### Changed

- The health check of the image runs every 5 minutes instead of every 30 seconds (every 5 seconds while it starts, which needs
  Docker Engine 25 or later).
- Much less memory after reading manuals: the model no longer keeps its biggest buffers, and reads 4 texts at a time. Two new
  settings tune it: `TINKER_EMBEDDING_BATCH_SIZE` and `TINKER_EMBEDDING_THREADS` in compose (`Tinker__EmbeddingBatchSize` and
  `Tinker__EmbeddingThreads` in the container). The index does not need to be rebuilt.
- A smaller image: each architecture only carries its own native libraries.
- The descriptions of `register_device` and `update_device` warn the assistant that reading manuals is slow, ask it to add
  them one at a time, and to check with `find_devices` before retrying a call that timed out.

### Fixed

- The health check no longer fills the logs and the traces. `/health` is not traced. Each request now writes one log line
  instead of the several of ASP.NET Core, and the line of a successful health check is `Debug`, so it is hidden by default.

## [0.1.0-beta.1] - 2026-11-25

Initial test only: the first pre-release, to check the publication of the image and to run it for the first time on a Docker host. It is not the
stable version and `latest` does not point to it: use its exact version.

Tinker is an MCP server that keeps the inventory of the devices of a home and searches their PDF manuals. It runs as a
Docker container and talks to MCP clients (Claude, for example) over HTTP.

### Tools

- `register_device`: adds a device to the inventory and indexes its manuals.
- `update_device`: changes the properties of a registered device.
- `delete_device`: removes a device and its indexed manuals.
- `find_devices`: lists the devices, optionally filtered by their properties.
- `search_manuals`: searches the manuals of a device by meaning and returns the passages that answer the question.

### Running it

- The image is `ghcr.io/javixulo/tinker`, run with the `docker-compose.yml` of the repository (copy `.env.template` to `.env`; `TINKER_VERSION` is required). The port inside the container is `8080` (`/mcp` for MCP, `/health` for the health check); the compose file publishes it on `7843`.
- Mount the data folder on `/data` (a local disk: SQLite locking is not reliable over NFS or SMB) and the manuals folder
  read-only on `/manuals`. Run the container as the owner of the data folder (`user:`).
- Configuration is through environment variables: `Tinker__DataPath`, `Tinker__ManualsPath`, `Tinker__EmbeddingModelPath`,
  `Tinker__EmbeddingModel`, and the OpenTelemetry variables (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`,
  `OTEL_RESOURCE_ATTRIBUTES`). See the README.
- The embedding model (`BAAI/bge-small-en-v1.5`) is inside the image, pinned to one revision and checked by its checksum.
- The image is built for `linux/amd64` and `linux/arm64`.
- On start the server checks its data folder, manuals folder, database and model, and logs what is wrong and how to fix it.
