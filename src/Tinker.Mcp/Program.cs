using OpenTelemetry;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
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
using Tinker.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Logs go to the console on stderr (so `docker logs` works when the OTLP backend is down) and to OpenTelemetry.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var otlp = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "tinker-mcp"))
    .WithTracing(tracing => tracing.AddSource(Telemetry.SourceName).AddAspNetCoreInstrumentation())
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
builder.Services.AddSingleton(sp => new ManualPathResolver(sp.GetRequiredService<TinkerOptions>().ManualsPath));
// The embedding model (technical spec, section 7). Its files are checked at startup, so it is created lazily.
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
{
    var options = sp.GetRequiredService<TinkerOptions>();
    return options.EmbeddingModel switch
    {
        "hash" => new HashEmbeddingGenerator(),
        _ => new BgeEmbeddingGenerator(options.EmbeddingModelPath),
    };
});
builder.Services.AddSingleton<DeviceRegistrar>();
builder.Services.AddSingleton<DeviceFinder>();
builder.Services.AddSingleton<DeviceRemover>();

// The contract uses snake_case (technical spec, section 6). Outcomes are plain results, never protocol errors.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<RegisterDeviceTool>(json)
    .WithTools<FindDevicesTool>(json)
    .WithTools<DeleteDeviceTool>(json);

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
#pragma warning disable CA1848, CA1873 // Runs once at startup.
        logger.LogCritical("Startup check failed: {Problem}", problem);
#pragma warning restore CA1848, CA1873
    }

    return 1;
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMcp("/mcp");

await app.RunAsync();
return 0;

/// <summary>Entry point, public so the contract tests can start the real server in process.</summary>
public partial class Program;
