using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Tinker.SystemTests;

/// <summary>
/// Complete flows through the Docker image, with the real embedding model, as an MCP client would use it. Each test
/// starts its own container. What the lower levels already prove (every rule and every message) is not repeated here:
/// these tests check what only the whole image can show.
/// </summary>
public sealed class SystemTests
{
    private const string Manual = "shelly/dimmer_2_guide.pdf";

    [Fact]
    public async Task A_device_goes_through_its_whole_life_with_its_manual_found_by_meaning()
    {
        await using var tinker = await TinkerContainer.StartAsync();

        var registered = await Call(tinker, "register_device", new { device = new { brand = "Shelly", model = "Dimmer 2", location = "Habitación", manuals = new[] { Manual } } });
        Assert.True(registered.GetProperty("ok").GetBoolean(), registered.ToString());
        var id = registered.GetProperty("device").GetProperty("device_id").GetInt64();

        var found = await Call(tinker, "find_devices", new { device = new { brand = "shelly", location = "habitacion" } });
        Assert.Equal(id, Assert.Single(found.GetProperty("devices").EnumerateArray()).GetProperty("device_id").GetInt64());

        var searched = await Call(tinker, "search_manuals", new { device_id = id, query = "How do I install and wire the dimmer?", n_results = 3 });
        Assert.True(searched.GetProperty("ok").GetBoolean(), searched.ToString());
        var first = searched.GetProperty("chunks")[0];
        Assert.Equal(Manual, first.GetProperty("manual").GetString());
        Assert.True(first.GetProperty("page").GetInt32() >= 1);
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("text").GetString()));

        var updated = await Call(tinker, "update_device", new { device_id = id, changes = new { color = "Black" } });
        Assert.True(updated.GetProperty("ok").GetBoolean(), updated.ToString());
        Assert.Equal("Black", updated.GetProperty("device").GetProperty("color").GetString());

        var deleted = await Call(tinker, "delete_device", new { device = new { brand = "Shelly", model = "Dimmer 2" } });
        Assert.True(deleted.GetProperty("ok").GetBoolean(), deleted.ToString());

        var gone = await Call(tinker, "search_manuals", new { device_id = id, query = "wiring" });
        Assert.False(gone.GetProperty("ok").GetBoolean());
        Assert.Equal("device_not_found", gone.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_inventory_and_the_search_index_survive_a_restart_of_the_container()
    {
        await using var tinker = await TinkerContainer.StartAsync();
        var registered = await Call(tinker, "register_device", new { device = new { brand = "Shelly", model = "Dimmer 2", manuals = new[] { Manual } } });
        Assert.True(registered.GetProperty("ok").GetBoolean(), registered.ToString());
        var id = registered.GetProperty("device").GetProperty("device_id").GetInt64();

        await tinker.RestartAsync();

        var found = await Call(tinker, "find_devices", new { device = new { brand = "Shelly" } });
        Assert.Equal(id, Assert.Single(found.GetProperty("devices").EnumerateArray()).GetProperty("device_id").GetInt64());
        var searched = await Call(tinker, "search_manuals", new { device_id = id, query = "How do I install and wire the dimmer?" });
        Assert.True(searched.GetProperty("ok").GetBoolean(), searched.ToString());
        Assert.True(searched.GetProperty("chunks").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task A_data_folder_that_cannot_be_written_stops_the_container_and_says_how_to_fix_it()
    {
        var (exitCode, output) = await TinkerContainer.RunWithUnwritableDataAsync();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("/data", output, StringComparison.Ordinal);
        Assert.Contains("not writable", output, StringComparison.Ordinal);
        Assert.Contains("Fix its owner or permissions", output, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> Call(TinkerContainer tinker, string tool, object arguments)
    {
        var json = JsonSerializer.SerializeToElement(arguments);
        var args = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        CallToolResult result = await tinker.Client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value;
    }
}
