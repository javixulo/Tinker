using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>Contract tests of <c>delete_device</c>: the real server in process, spoken to with the SDK's MCP client.</summary>
public sealed class DeleteDeviceContractTests : IAsyncLifetime
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
    public async Task Exposes_delete_device_with_a_snake_case_schema()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools, t => t.Name == "delete_device");
        var schema = tool.JsonSchema.GetRawText();
        Assert.Contains("\"device\"", schema);
        Assert.Contains("device_type", schema);
        Assert.DoesNotContain("deviceType", schema);
        Assert.NotNull(tool.ReturnJsonSchema);
        Assert.Contains("device_id", tool.ReturnJsonSchema!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deletes_a_device_and_every_outcome_is_a_normal_result()
    {
        await Register(new { device = new { brand = "Shelly", model = "Dimmer 2", manuals = new[] { "shelly/dimmer_2_guide.pdf" } } });

        var blank = await Delete(new { });
        Assert.NotEqual(true, blank.IsError);
        Assert.False(blank.StructuredContent!.Value.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_fields", blank.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());

        var missing = await Delete(new { device = new { brand = "Samsung" } });
        Assert.NotEqual(true, missing.IsError);
        Assert.Equal("device_not_found", missing.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());

        var deleted = await Delete(new { device = new { brand = "shelly", model = "DIMMER 2" } });
        Assert.NotEqual(true, deleted.IsError);
        var content = deleted.StructuredContent!.Value;
        Assert.True(content.GetProperty("ok").GetBoolean());
        Assert.Equal(1, content.GetProperty("device").GetProperty("device_id").GetInt64());
        Assert.Equal("DIMMER 2", content.GetProperty("device").GetProperty("model").GetString());

        var listed = await Call("find_devices", new { });
        Assert.Equal(0, listed.StructuredContent!.Value.GetProperty("devices").GetArrayLength());
    }

    private Task<CallToolResult> Delete(object arguments) => Call("delete_device", arguments);

    private Task<CallToolResult> Register(object arguments) => Call("register_device", arguments);

    private async Task<CallToolResult> Call(string tool, object arguments)
    {
        var json = JsonSerializer.SerializeToElement(arguments);
        var args = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        return await _client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
    }
}
