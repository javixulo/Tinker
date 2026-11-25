# Changelog

All notable changes to Tinker are written here, newest version first. What counts as a change for a user (the public surface)
is defined in [`docs/technical_spec.md`](docs/technical_spec.md), section 10.

**How a release uses this file.** The release workflow copies the entry of the version into the GitHub release, finding the
heading `## [x.y.z] - date` (it must start exactly with `## [x.y.z]`). Before creating a tag, rename `Unreleased` to the
version and the date, add a new empty `Unreleased` above it, and commit. The workflow fails before publishing anything if the
entry is missing.

## [Unreleased]

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
