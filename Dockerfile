# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Tinker.Core/Tinker.Core.csproj src/Tinker.Core/
COPY src/Tinker.Mcp/Tinker.Mcp.csproj src/Tinker.Mcp/
RUN dotnet restore src/Tinker.Mcp
COPY src/ src/
RUN dotnet publish src/Tinker.Mcp -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl is only for the HEALTHCHECK: the runtime image has no HTTP client.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

# Folders the container expects to be mounted (see docker-compose.yml). Nothing persistent lives in the image.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Tinker__DataPath=/data \
    Tinker__ManualsPath=/manuals \
    Tinker__EmbeddingModelPath=/models/bge-small-en-v1.5

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fs http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Tinker.Mcp.dll"]
