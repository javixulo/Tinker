using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>Contract tests: the real server in process, spoken to with the SDK's MCP client over HTTP.</summary>
public sealed class RegisterDeviceContractTests : IAsyncLifetime
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
    public async Task Exposes_register_device_with_a_snake_case_schema()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(tools, t => t.Name == "register_device");
        var schema = tool.JsonSchema.GetRawText();
        Assert.Contains("\"device\"", schema);
        Assert.Contains("device_type", schema);
        Assert.Contains("warranty_end_date", schema);
        Assert.DoesNotContain("deviceType", schema);
        Assert.NotNull(tool.ReturnJsonSchema);
    }

    [Fact]
    public async Task Registers_a_device_and_returns_structured_snake_case_content()
    {
        var result = await Call(new
        {
            device = new { brand = "Shelly", model = "Dimmer 2", manuals = new[] { "shelly/dimmer_2_guide.pdf" }, device_type = "Dimmer", purchase_date = "2025-03-01" },
        });

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        Assert.True(content.GetProperty("ok").GetBoolean());
        var device = content.GetProperty("device");
        Assert.Equal(1, device.GetProperty("device_id").GetInt64());
        Assert.Equal("Dimmer", device.GetProperty("device_type").GetString());
        Assert.Equal("2025-03-01", device.GetProperty("purchase_date").GetString());
        Assert.Equal("shelly/dimmer_2_guide.pdf", device.GetProperty("manuals")[0].GetString());
    }

    [Fact]
    public async Task A_rejection_is_a_normal_result_not_a_protocol_error()
    {
        var result = await Call(new { device = new { brand = "Shelly", model = " ", manuals = new[] { "../secret.pdf" } } });

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        Assert.False(content.GetProperty("ok").GetBoolean());
        var codes = content.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();
        Assert.Equal(["invalid_fields", "manual_not_allowed"], codes);
    }

    [Fact]
    public async Task A_missing_device_is_tolerated_as_invalid_fields()
    {
        var result = await Call(new { });

        Assert.NotEqual(true, result.IsError);
        var errors = result.StructuredContent!.Value.GetProperty("errors");
        Assert.Equal("invalid_fields", errors[0].GetProperty("code").GetString());
    }

    private async Task<CallToolResult> Call(object arguments)
    {
        var json = JsonSerializer.SerializeToElement(arguments);
        var args = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        return await _client.CallToolAsync("register_device", args, cancellationToken: TestContext.Current.CancellationToken);
    }
}
