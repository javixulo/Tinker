using OpenTelemetry;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Tinker.Core;
using Tinker.Core.Embeddings;
using Tinker.Core.Finding;
using Tinker.Core.Manuals;
using Tinker.Core.Registration;
using Tinker.Core.Removal;
using Tinker.Core.Storage;
using Tinker.Mcp;
using Tinker.Mcp.Startup;
using Tinker.Core.Searching;
using Tinker.Core.Updating;
using Tinker.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// The version of the build: stamped from the git tag (-p:Version=...), or 0.0.0-dev for a build that is not a release.
// It is the one the telemetry and the MCP handshake report; the metadata after '+' (the commit) is not part of it.
var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];

// Logs go to the console on stderr (so `docker logs` works when the OTLP backend is down) and to OpenTelemetry.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var otlp = builder.Services.AddOpenTelemetry()
    // service.name and service.version here; deployment.environment.name comes from OTEL_RESOURCE_ATTRIBUTES (technical spec, section 3).
    .ConfigureResource(resource => resource.AddService(
        builder.Configuration["OTEL_SERVICE_NAME"] ?? "tinker-mcp",
        serviceVersion: version))
    // The health check runs as long as the container does: its requests are not traced (technical spec, section 3).
    .WithTracing(tracing => tracing.AddSource(Telemetry.SourceName)
        .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health")))
    .WithLogging();
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otlp.UseOtlpExporter();
}

builder.Services.Configure<TinkerOptions>(builder.Configuration.GetSection(TinkerOptions.Section));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TinkerOptions>>().Value);
builder.Services.AddSingleton(sp => new SqliteDatabase(sp.GetRequiredService<TinkerOptions>().DatabasePath));
builder.Services.AddSingleton<ITransactionRunner>(sp => sp.GetRequiredService<SqliteDatabase>());
builder.Services.AddSingleton<IInventory, SqliteInventory>();
builder.Services.AddSingleton<IManualsIndex, SqliteManualsIndex>();
builder.Services.AddSingleton<IManualTextExtractor, PdfPigTextExtractor>();
builder.Services.AddSingleton<IManualFiles>(sp => new ManualPathResolver(sp.GetRequiredService<TinkerOptions>().ManualsPath));
// The embedding model (technical spec, section 7). Its files are checked at startup, so it is created lazily.
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
{
    var options = sp.GetRequiredService<TinkerOptions>();
    return options.EmbeddingModel switch
    {
        "hash" => new HashEmbeddingGenerator(),
        _ => new BgeEmbeddingGenerator(options.EmbeddingModelPath, options.EmbeddingBatchSize, options.EmbeddingThreads),
    };
});
builder.Services.AddSingleton<DeviceRegistrar>();
builder.Services.AddSingleton<DeviceFinder>();
builder.Services.AddSingleton<DeviceRemover>();
builder.Services.AddSingleton<DeviceUpdater>();
builder.Services.AddSingleton(sp => new ManualSearcher(
    sp.GetRequiredService<IInventory>(),
    sp.GetRequiredService<IManualsIndex>(),
    sp.GetRequiredService<ILogger<ManualSearcher>>(),
    // A bge model wants a prefix in front of a query; the model-free hash generator does not.
    sp.GetRequiredService<TinkerOptions>().EmbeddingModel == "hash" ? "" : BgeEmbeddingGenerator.QueryPrefix));

// The contract uses snake_case (technical spec, section 6). Outcomes are plain results, never protocol errors.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};

builder.Services
    // The library reports 0.0.0.0 unless it is told the version; the name is the one it already reported (the assembly name).
    .AddMcpServer(options => options.ServerInfo = new Implementation { Name = "Tinker.Mcp", Version = version ?? "0.0.0-dev" })
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithRequestFilters(filters => filters.AddCallToolFilter(ToolErrorFlag.Apply))
    .WithTools<RegisterDeviceTool>(json)
    .WithTools<FindDevicesTool>(json)
    .WithTools<DeleteDeviceTool>(json)
    .WithTools<SearchManualsTool>(json)
    .WithTools<UpdateDeviceTool>(json);

var app = builder.Build();

var problems = StartupValidation.Validate(
    app.Services.GetRequiredService<TinkerOptions>(),
    app.Services.GetRequiredService<SqliteDatabase>(),
    app.Services);
if (problems.Count > 0)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    foreach (var problem in problems)
    {
#pragma warning disable CA1848, CA1873, CA1727 // Runs once at startup.
        logger.LogCritical("Startup check failed: {problem}", problem);
#pragma warning restore CA1848, CA1873
    }

    return 1;
}

#pragma warning disable CA1848
app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogInformation("Startup checks passed");
#pragma warning restore CA1848

app.UseMiddleware<RequestLogging>();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMcp("/mcp");

await app.RunAsync();
return 0;

/// <summary>Entry point, public so the contract tests can start the real server in process.</summary>
public partial class Program;
