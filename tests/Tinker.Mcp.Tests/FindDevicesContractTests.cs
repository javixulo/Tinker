using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Tinker.Mcp.Tests;

/// <summary>Contract tests of <c>find_devices</c>: the real server in process, spoken to with the SDK's MCP client.</summary>
public sealed class FindDevicesContractTests : IAsyncLifetime
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
    public async Task Exposes_find_devices_with_a_snake_case_schema()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools, t => t.Name == "find_devices");
        var schema = tool.JsonSchema.GetRawText();
        Assert.Contains("\"device\"", schema);
        Assert.Contains("device_type", schema);
        Assert.DoesNotContain("deviceType", schema);
        Assert.NotNull(tool.ReturnJsonSchema);
        Assert.Contains("device_id", tool.ReturnJsonSchema!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finds_a_registered_device_and_an_empty_result_is_a_normal_result()
    {
        var empty = await Find(new { device = new { brand = "Shelly" } });
        Assert.NotEqual(true, empty.IsError);
        Assert.True(empty.StructuredContent!.Value.GetProperty("ok").GetBoolean());
        Assert.Equal(0, empty.StructuredContent!.Value.GetProperty("devices").GetArrayLength());
        Assert.Contains("no registered devices", empty.StructuredContent!.Value.GetProperty("message").GetString(), StringComparison.Ordinal);

        await Register(new { device = new { brand = "Shelly", model = "Dimmer 2", location = "Habitación", manuals = new[] { "shelly/dimmer_2_guide.pdf" } } });

        var found = await Find(new { device = new { brand = "shelly", location = "habitacion" } });
        Assert.NotEqual(true, found.IsError);
        var content = found.StructuredContent!.Value;
        Assert.True(content.GetProperty("ok").GetBoolean());
        var device = Assert.Single(content.GetProperty("devices").EnumerateArray());
        Assert.Equal(1, device.GetProperty("device_id").GetInt64());
        Assert.Equal("Habitación", device.GetProperty("location").GetString());

        var nothing = await Find(new { device = new { brand = "Samsung" } });
        Assert.Contains("No registered device matches (brand: Samsung)", nothing.StructuredContent!.Value.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_device_lists_everything()
    {
        await Register(new { device = new { brand = "Shelly", model = "Dimmer 2", manuals = new[] { "shelly/dimmer_2_guide.pdf" } } });

        var result = await Find(new { });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, result.StructuredContent!.Value.GetProperty("devices").GetArrayLength());
    }

    private Task<CallToolResult> Find(object arguments) => Call("find_devices", arguments);

    private Task<CallToolResult> Register(object arguments) => Call("register_device", arguments);

    private async Task<CallToolResult> Call(string tool, object arguments)
    {
        var json = JsonSerializer.SerializeToElement(arguments);
        var args = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        return await _client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
    }
}
