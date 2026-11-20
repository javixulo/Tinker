using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>
/// Trace context (technical spec, section 3): a client that sends <c>traceparent</c> gets its trace continued by the
/// server, and a client that sends none still works, in a trace of its own. Spans are read from the real server in process.
/// </summary>
public sealed class TraceContextContractTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinker-mcp-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentBag<Activity> _spans = [];
    private ActivityListener _listener = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _http = null!;

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Path.Combine(_root, "manuals"));
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Tinker",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tinker:DataPath", Path.Combine(_root, "data"));
            builder.UseSetting("Tinker:ManualsPath", Path.Combine(_root, "manuals"));
            builder.UseSetting("Tinker:EmbeddingModel", "hash");
        });
        _http = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Dispose();
        _http.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task The_spans_of_a_call_continue_the_trace_the_client_sent()
    {
        var traceId = ActivityTraceId.CreateRandom();
        _http.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        await FindDevices();

        Assert.Contains(_spans, span => span.OperationName == "find_devices" && span.TraceId == traceId);
    }

    [Fact]
    public async Task Without_traceparent_the_call_works_and_starts_its_own_trace()
    {
        // The listener is global and other contract tests call tools in parallel, so the span is found by a query only this test sends.
        var query = "trace-test-" + Guid.NewGuid().ToString("N");

        var result = await Call("search_manuals", new Dictionary<string, object?> { ["device_id"] = 999, ["query"] = query });

        Assert.NotNull(result.StructuredContent);
        var span = Assert.Single(_spans, s => s.OperationName == "search_manuals" && (string?)s.GetTagItem("search.query") == query);
        Assert.NotEqual(default, span.TraceId);
    }

    private Task<ModelContextProtocol.Protocol.CallToolResult> FindDevices() =>
        Call("find_devices", new Dictionary<string, object?> { ["device"] = new { } });

    private async Task<ModelContextProtocol.Protocol.CallToolResult> Call(string tool, Dictionary<string, object?> arguments)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(_http.BaseAddress!, "/mcp") }, _http);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        return await client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);
    }
}
