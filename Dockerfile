# syntax=docker/dockerfile:1
# The build, publish and model stages always run on the machine that builds (the compiled output is the same for every architecture: portable
# assemblies and data files), so a multi-platform build does not compile under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Tinker.Core/Tinker.Core.csproj src/Tinker.Core/
COPY src/Tinker.Mcp/Tinker.Mcp.csproj src/Tinker.Mcp/
RUN dotnet restore src/Tinker.Mcp
COPY src/ src/
# The version comes from the git tag (the release workflow passes it); the build context has no .git folder. A build that
# is not a release is 0.0.0-dev. It is declared here, after the restore, so changing it does not invalidate the restore layer.
ARG VERSION=0.0.0-dev
RUN dotnet publish src/Tinker.Mcp -c Release --no-restore -p:Version=${VERSION} -o /app

# ONNX Runtime and SQLite come with their native libraries for every platform (about 300 MB). Each image keeps only the
# ones of the architecture it runs on. This stage still runs on the machine that builds, and only this step differs per
# target, so the compilation above is done once for all of them. TARGETARCH needs BuildKit (the default builder).
FROM build AS publish
ARG TARGETARCH
RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "Unsupported architecture '$TARGETARCH' (amd64 or arm64; the build needs BuildKit)." >&2; exit 1 ;; \
    esac \
 && find /app/runtimes -mindepth 1 -maxdepth 1 ! -name "$rid" -exec rm -rf {} + \
 && test -f "/app/runtimes/$rid/native/libonnxruntime.so"

# The embedding model (technical spec, section 7). Pinned to one revision of the Hugging Face repository and checked
# against its SHA-256, so the image never gets a different model without a build failure. It is its own stage and comes
# before the code, so the cache keeps it between builds.
FROM --platform=$BUILDPLATFORM alpine:3.24 AS model
ARG MODEL_REVISION=5c38ec7c405ec4b44b94cc5a9bb96e735b38267a
ARG MODEL_ONNX_SHA256=828e1496d7fabb79cfa4dcd84fa38625c0d3d21da474a00f08db0f559940cf35
ARG MODEL_VOCAB_SHA256=07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3
RUN apk add --no-cache curl
WORKDIR /model
RUN base="https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/${MODEL_REVISION}" \
 && curl -fsSL --retry 3 -o model.onnx "$base/onnx/model.onnx" \
 && curl -fsSL --retry 3 -o vocab.txt "$base/vocab.txt" \
 && echo "${MODEL_ONNX_SHA256}  model.onnx" | sha256sum -c - \
 && echo "${MODEL_VOCAB_SHA256}  vocab.txt" | sha256sum -c -

FROM mcr.microsoft.com/dotnet/aspnet:11.0
ARG VERSION=0.0.0-dev
LABEL org.opencontainers.image.title="Tinker" \
      org.opencontainers.image.description="MCP server: inventory of home devices and search in their manuals" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${VERSION}"
# curl is only for the HEALTHCHECK: the runtime image has no HTTP client.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=model /model /models/bge-small-en-v1.5
COPY --from=publish /app .

# /data and /manuals are mounted (see docker-compose.yml); nothing persistent lives in the image. The model is in the
# image; mounting a folder on /models/bge-small-en-v1.5 replaces it.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Tinker__DataPath=/data \
    Tinker__ManualsPath=/manuals \
    Tinker__EmbeddingModelPath=/models/bge-small-en-v1.5

EXPOSE 8080
# Every 5 seconds while it starts (--start-interval needs Docker Engine 25 or later), then every 5 minutes.
HEALTHCHECK --interval=5m --timeout=5s --start-period=60s --start-interval=5s --retries=3 \
  CMD curl -fs http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Tinker.Mcp.dll"]
