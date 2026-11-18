using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>Contract tests: the real server in process, spoken to with the SDK's MCP client over HTTP.</summary>
public sealed class UpdateDeviceContractTests : IAsyncLifetime
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
    public async Task Exposes_update_device_with_a_snake_case_schema_marked_idempotent()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools, t => t.Name == "update_device");
        var schema = tool.JsonSchema.GetRawText();
        Assert.Contains("\"device_id\"", schema);
        Assert.Contains("\"changes\"", schema);
        Assert.Contains("warranty_end_date", schema);
        Assert.DoesNotContain("deviceType", schema);
        Assert.Equal(true, tool.ProtocolTool.Annotations?.IdempotentHint);
        Assert.NotNull(tool.ReturnJsonSchema);
    }

    [Fact]
    public async Task A_field_that_is_absent_is_kept_and_one_that_is_empty_is_emptied()
    {
        await Call("register_device", new { device = new { brand = "Shelly", model = "Dimmer 2", location = "Salon", color = "White", urls = new[] { "https://a.example" } } });

        var kept = await Call("update_device", new { device_id = 1, changes = new { color = "Black" } });
        var device = kept.StructuredContent!.Value.GetProperty("device");
        Assert.Equal("Salon", device.GetProperty("location").GetString());
        Assert.Equal("Black", device.GetProperty("color").GetString());
        Assert.Equal("https://a.example", device.GetProperty("urls")[0].GetString());

        var emptied = await Call("update_device", new { device_id = 1, changes = new { location = "", urls = Array.Empty<string>() } });
        device = emptied.StructuredContent!.Value.GetProperty("device");
        Assert.Equal(JsonValueKind.Null, device.GetProperty("location").ValueKind);
        Assert.Equal(0, device.GetProperty("urls").GetArrayLength());
        Assert.Equal("Black", device.GetProperty("color").GetString());
    }

    [Fact]
    public async Task A_rejection_is_a_normal_result_and_a_missing_argument_is_tolerated()
    {
        var notFound = await Call("update_device", new { device_id = 9, changes = new { color = "Red" } });
        var missing = await Call("update_device", new { });

        Assert.NotEqual(true, notFound.IsError);
        Assert.Equal("device_not_found", notFound.StructuredContent!.Value.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.NotEqual(true, missing.IsError);
        Assert.Equal("invalid_fields", missing.StructuredContent!.Value.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    private async Task<CallToolResult> Call(string tool, object arguments)
    {
        var json = JsonSerializer.SerializeToElement(arguments);
        var args = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        return await _client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
    }
}
