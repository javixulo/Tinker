using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>
/// The health check runs for as long as the container does, so it must leave nothing in the shared backend: no span and no
/// log line for a successful request (technical spec, section 3).
/// </summary>
public sealed class HealthNoiseContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinker-mcp-" + Guid.NewGuid().ToString("N"));
    private readonly Recorder _recorder = new();
    private readonly WebApplicationFactory<Program> _factory;

    public HealthNoiseContractTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Path.Combine(_root, "manuals"));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tinker:DataPath", Path.Combine(_root, "data"));
            builder.UseSetting("Tinker:ManualsPath", Path.Combine(_root, "manuals"));
            builder.UseSetting("Tinker:EmbeddingModel", "hash");
            // Debug is hidden by default: the test asks for it to see the level of the health check line.
            builder.UseSetting("Logging:LogLevel:Tinker.Mcp.RequestLogging", "Debug");
            builder.ConfigureTestServices(services =>
            {
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(_recorder));
                services.AddSingleton<ILoggerProvider>(_recorder.Logs);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    // Catches: the health check filling the log and the traces, which hides what matters.
    [Fact]
    public async Task A_successful_health_check_leaves_no_span_and_no_log()
    {
        var client = _factory.CreateClient();
        _recorder.Logs.Entries.Clear();

        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        // A request that must be traced, so the test fails if the recorder sees nothing at all.
        using var list = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = list };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        (await client.SendAsync(request, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        // The client gets the response before the server ends the span and writes its line: wait for both, or the test is a race.
        Assert.True(
            SpinWait.SpinUntil(
                () => _recorder.Paths.Contains("/mcp")
                    && _recorder.Logs.Entries.Any(entry => entry.Text.Contains("POST /mcp", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(10)),
            "The span and the log line of POST /mcp never arrived.");
        _factory.Services.GetRequiredService<TracerProvider>().ForceFlush();

        Assert.Contains("/mcp", _recorder.Paths);
        Assert.DoesNotContain("/health", _recorder.Paths);
        Assert.DoesNotContain(_recorder.Logs.Entries, entry => entry.Text.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        // One line of our own per request: the good ones stay at Information, the health check goes down to Debug.
        var requests = _recorder.Logs.Entries.Where(entry => entry.Text.StartsWith("Tinker.Mcp.RequestLogging:", StringComparison.Ordinal)).ToList();
        var health = Assert.Single(requests, entry => entry.Text.Contains("GET /health", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Debug, health.Level);
        var mcp = Assert.Single(requests, entry => entry.Text.Contains("POST /mcp", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, mcp.Level);
        Assert.Contains("200", mcp.Text, StringComparison.Ordinal);
    }

    private sealed class Recorder : BaseProcessor<Activity>
    {
        private readonly ConcurrentQueue<string> _paths = new();

        public CapturingLoggerProvider Logs { get; } = new();

        public IReadOnlyCollection<string> Paths => _paths.ToArray();

        public override void OnEnd(Activity data)
        {
            if (data.GetTagItem("url.path") is string path)
            {
                _paths.Enqueue(path);
            }
        }
    }

    /// <summary>Records the level and "category: message" of what passes the log levels.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Text)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<(LogLevel Level, string Text)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, $"{category}: {formatter(state, exception)}"));
        }
    }
}
