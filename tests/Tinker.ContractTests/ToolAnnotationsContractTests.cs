using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>
/// Contract tests of what a client sees of the whole tool list: annotations, titles and the text copy of the
/// structured result and its shape (technical spec, section 6).
/// </summary>
public sealed class ToolAnnotationsContractTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinker-mcp-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> _factory = null!;
    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Path.Combine(_root, "manuals"));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tinker:DataPath", Path.Combine(_root, "data"));
            builder.UseSetting("Tinker:ManualsPath", Path.Combine(_root, "manuals"));
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

    // Catches: the handshake reporting the library's default version instead of the one the build stamped from the tag.
    [Fact]
    public void The_handshake_reports_the_version_of_the_build()
    {
        var stamped = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

        var info = _client.ServerInfo;
        Assert.NotNull(info);
        Assert.Equal(stamped, info.Version);
        Assert.Equal("Tinker.Mcp", info.Name);
    }

    // Catches: a tool added or renamed without saying so (the tool list is the contract).
    [Fact]
    public async Task Lists_the_five_tools_in_a_fixed_order()
    {
        var names = (await Tools()).Select(t => t.Name).ToList();

        Assert.Equal(["register_device", "find_devices", "delete_device", "search_manuals", "update_device"], names);
    }

    // Catches: a tool that reaches outside systems or has no title: the defaults of MCP say open world and no title.
    [Fact]
    public async Task Every_tool_is_closed_world_and_has_a_title()
    {
        foreach (var tool in await Tools())
        {
            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.False(annotations!.OpenWorldHint, tool.Name);
            Assert.False(string.IsNullOrWhiteSpace(annotations.Title), tool.Name);
        }
    }

    // Catches: a reader marked as a writer or the other way round (clients decide on confirmations from these hints).
    [Theory]
    [InlineData("find_devices", true, null, null)]
    [InlineData("search_manuals", true, null, null)]
    [InlineData("register_device", false, false, false)]
    [InlineData("update_device", false, true, true)]
    [InlineData("delete_device", false, true, false)]
    public async Task Each_tool_says_whether_it_reads_deletes_or_repeats_safely(string name, bool readOnly, bool? destructive, bool? idempotent)
    {
        var annotations = Assert.Single(await Tools(), t => t.Name == name).ProtocolTool.Annotations!;

        // The SDK leaves readOnlyHint out when it is false, and MCP's default for it is false.
        Assert.Equal(readOnly, annotations.ReadOnlyHint ?? false);
        if (!readOnly)
        {
            Assert.Equal(destructive, annotations.DestructiveHint);
            Assert.Equal(idempotent, annotations.IdempotentHint);
        }
    }

    // Catches: clients that read only text get nothing: the same JSON must travel as text too.
    [Fact]
    public async Task The_structured_result_is_also_sent_as_text()
    {
        var result = await _client.CallToolAsync("find_devices", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        using var parsed = JsonDocument.Parse(text);
        Assert.Equal(result.StructuredContent!.Value.GetRawText(), parsed.RootElement.GetRawText());
    }

    // Catches: an `ok` flag coming back: isError already says whether the call worked, so a second flag can disagree with it.
    [Fact]
    public async Task No_result_carries_an_ok_flag_and_a_rejection_is_told_by_isError_and_its_errors()
    {
        foreach (var tool in await Tools())
        {
            Assert.DoesNotContain("\"ok\"", tool.ReturnJsonSchema?.GetRawText() ?? "", StringComparison.Ordinal);
        }

        var success = await _client.CallToolAsync("find_devices", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);
        var rejection = await _client.CallToolAsync("register_device", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, success.IsError);
        Assert.False(success.StructuredContent!.Value.TryGetProperty("ok", out _));
        Assert.True(rejection.IsError);
        Assert.False(rejection.StructuredContent!.Value.TryGetProperty("ok", out _));
        Assert.Equal("invalid_fields", rejection.StructuredContent!.Value.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    private async Task<IList<McpClientTool>> Tools() =>
        await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
}
