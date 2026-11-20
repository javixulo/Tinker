using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.IntegrationTests;

/// <summary>UC-03 · Delete a device, UC-04 · Fail to delete a device.</summary>
public sealed class DeleteDeviceTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    /// <summary>Three devices with their manuals indexed. Returns them by device_id (1 to 3). The first two share type and brand.</summary>
    private async Task<List<Device>> Inventory(TestEnvironment? env = null)
    {
        env ??= _env;
        var devices = new List<Device>();
        var rows = new (string Brand, string Model, string Type, string Location, string File)[]
        {
            ("Shelly", "Dimmer 2", "Dimmer", "Salon", "a.pdf"),
            ("Shelly", "RGBW2", "Dimmer", "Habitación", "b.pdf"),
            ("Samsung", "QE55Q60", "TV", "Salon", "c.pdf"),
        };
        foreach (var row in rows)
        {
            var result = await env.Registrar.RegisterAsync(new Device
            {
                Brand = row.Brand,
                Model = row.Model,
                DeviceType = row.Type,
                Location = row.Location,
                Manuals = [env.Pdf(row.File, "Text of " + row.File)],
                Urls = ["https://example.com/" + row.Model.Replace(' ', '-')],
            });
            Assert.True(result.Ok);
            devices.Add(result.Device!);
        }

        return devices;
    }

    private Task<DeletionResult> Delete(Device criteria) => _env.Remover.DeleteAsync(criteria);

    /// <summary>What the other devices keep: their records, their chunks and their manual files.</summary>
    private string Others(params long[] ids) =>
        string.Join('|', _env.Inventory.ListAllAsync().Result.Where(d => ids.Contains(d.DeviceId!.Value)).Select(TestEnvironment.Json))
        + "#" + string.Join('|', ids.SelectMany(id => _env.Chunks(id)).Select(c => $"{c.DeviceId}:{c.Manual}:{c.Page}:{c.Text}"));

    private static void AssertFails(DeletionResult result, string code)
    {
        Assert.False(result.Ok);
        Assert.Null(result.Device);
        Assert.Equal(code, result.Error!.Code);
    }

    // UC-03

    [Fact]
    [Trait("Criteria", "UC-03.1")]
    public async Task Deletes_the_device_that_brand_and_model_single_out()
    {
        var devices = await Inventory();
        var othersBefore = Others(1, 3);
        Assert.NotEqual(0, _env.CountChunks(2));

        var result = await Delete(new Device { Brand = " SHELLY ", Model = "rgbw2" });

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal(TestEnvironment.Json(devices[1]), TestEnvironment.Json(result.Device!));
        Assert.Equal([1L, 3L], (await _env.Inventory.ListAllAsync()).Select(d => d.DeviceId!.Value));
        Assert.Equal(0, _env.CountChunks(2));
        Assert.Equal(0, _env.CountRows("device_manuals", 2));
        Assert.Equal(0, _env.CountRows("device_urls", 2));
        Assert.True(File.Exists(Path.Combine(_env.ManualsPath, "b.pdf")), "the manual files belong to the user");
        Assert.Equal(othersBefore, Others(1, 3));
        Assert.Contains(_env.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.StartsWith("Device 2 deleted", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criteria", "UC-03.1, UC-03.2")]
    public async Task Deleting_by_device_id_works_and_the_id_is_not_reused()
    {
        await Inventory();

        var result = await Delete(new Device { DeviceId = 3 });
        var again = await _env.Registrar.RegisterAsync(new Device { Brand = "Samsung", Model = "QE55Q60", Manuals = [_env.Pdf("c.pdf", "Text of c.pdf")] });

        Assert.True(result.Ok);
        Assert.True(again.Ok, "the manual and the brand and model are free again");
        Assert.Equal(4, again.Device!.DeviceId);
    }

    // UC-04

    [Fact]
    [Trait("Criteria", "UC-04.4")]
    public async Task A_failure_while_deleting_rolls_everything_back()
    {
        using var env = new TestEnvironment(wrapIndex: index => new ThrowingDeleteIndex(index));
        var devices = await Inventory(env);
        var before = env.Snapshot();

        // The index deletes the chunks and then breaks: the device row, already deleted, must come back.
        var result = await env.Remover.DeleteAsync(new Device { Brand = "Shelly", Model = "RGBW2" });

        AssertFails(result, "failed");
        Assert.Contains("not because of the request", result.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Tell the user instead of retrying", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("index broke", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(before, env.Snapshot());
        Assert.Equal(3, env.CountDevices());
        Assert.NotEqual(0, env.CountChunks(2));
        Assert.Equal(TestEnvironment.Json(devices[1]), TestEnvironment.Json((await env.Inventory.ListAllAsync())[1]));
        Assert.Contains(env.Logs.Entries, e => e.Level == LogLevel.Error);
    }

}
