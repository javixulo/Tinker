using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>Contract tests of <c>search_manuals</c>: the real server in process, spoken to with the SDK's MCP client.</summary>
public sealed class SearchManualsContractTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinker-mcp-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> _factory = null!;
    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        var manuals = Path.Combine(_root, "manuals");
        Directory.CreateDirectory(Path.Combine(manuals, "shelly"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "manuals", "shelly", "dimmer_2_guide.pdf"), Path.Combine(manuals, "shelly", "dimmer_2_guide.pdf"));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tinker:DataPath", Path.Combine(_root, "data"));
            builder.UseSetting("Tinker:ManualsPath", manuals);
            builder.UseSetting("Tinker:EmbeddingModel", "hash");
        });
        var http = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http);
        _client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Exposes_search_manuals_with_a_snake_case_schema_and_is_read_only()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools, t => t.Name == "search_manuals");
        var schema = tool.JsonSchema.GetRawText();
        Assert.Contains("device_id", schema, StringComparison.Ordinal);
        Assert.Contains("n_results", schema, StringComparison.Ordinal);
        Assert.Contains("query", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("deviceId", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("nResults", schema, StringComparison.Ordinal);
        Assert.Equal(true, tool.ProtocolTool.Annotations?.ReadOnlyHint);
        var returned = tool.ReturnJsonSchema!.Value.GetRawText();
        Assert.Contains("chunks", returned, StringComparison.Ordinal);
        Assert.Contains("device_id", returned, StringComparison.Ordinal);
        Assert.Contains("page", returned, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Searches_the_manuals_of_a_registered_device()
    {
        var registered = await Register(new { device = new { brand = "Shelly", model = "Dimmer 2", manuals = new[] { "shelly/dimmer_2_guide.pdf" } } });
        Assert.True(registered.StructuredContent!.Value.GetProperty("ok").GetBoolean());

        var found = await Search(new { device_id = 1, query = "how do I install and wire the dimmer", n_results = 2 });

        Assert.NotEqual(true, found.IsError);
        var content = found.StructuredContent!.Value;
        Assert.True(content.GetProperty("ok").GetBoolean());
        var chunks = content.GetProperty("chunks");
        Assert.InRange(chunks.GetArrayLength(), 1, 2);
        var first = chunks[0];
        Assert.Equal(1, first.GetProperty("device_id").GetInt64());
        Assert.Equal("shelly/dimmer_2_guide.pdf", first.GetProperty("manual").GetString());
        Assert.True(first.GetProperty("page").GetInt32() >= 1);
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("text").GetString()));
        Assert.Equal(JsonValueKind.Null, content.TryGetProperty("message", out var message) ? message.ValueKind : JsonValueKind.Null);
    }

    [Fact]
    public async Task Every_outcome_is_a_normal_result_and_a_missing_argument_is_not_a_binding_error()
    {
        await Register(new { device = new { brand = "Shelly", model = "Dimmer 2", manuals = new[] { "shelly/dimmer_2_guide.pdf" } } });

        var noQuery = await Search(new { device_id = 1 });
        Assert.NotEqual(true, noQuery.IsError);
        Assert.False(noQuery.StructuredContent!.Value.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_fields", noQuery.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());

        var noDevice = await Search(new { query = "wifi" });
        Assert.NotEqual(true, noDevice.IsError);
        Assert.Equal("invalid_fields", noDevice.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());

        var badCount = await Search(new { device_id = 1, query = "wifi", n_results = 0 });
        Assert.NotEqual(true, badCount.IsError);
        Assert.Equal("invalid_fields", badCount.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());

        var unknown = await Search(new { device_id = 42, query = "wifi" });
        Assert.NotEqual(true, unknown.IsError);
        var error = unknown.StructuredContent!.Value.GetProperty("error");
        Assert.Equal("device_not_found", error.GetProperty("code").GetString());
        Assert.Contains("find_devices", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private Task<CallToolResult> Search(object arguments) => Call("search_manuals", arguments);

    private Task<CallToolResult> Register(object arguments) => Call("register_device", arguments);

    private async Task<CallToolResult> Call(string tool, object arguments)
    {
        var json = JsonSerializer.SerializeToElement(arguments);
        var args = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        return await _client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
    }
}
