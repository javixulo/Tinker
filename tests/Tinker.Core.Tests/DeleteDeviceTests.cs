using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.Core.Tests;

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
        Assert.Contains(_env.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.StartsWith("Deletion completed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criteria", "UC-03.2")]
    public async Task Deletes_the_device_that_other_properties_single_out()
    {
        var devices = await Inventory();
        var othersBefore = Others(1, 2);

        // Type TV is not enough for the other two, and Salon is shared with device 1: only together they single out device 3.
        var result = await Delete(new Device { DeviceType = "tv", Brand = "samsung", Location = "salon" });

        Assert.True(result.Ok);
        Assert.Equal(TestEnvironment.Json(devices[2]), TestEnvironment.Json(result.Device!));
        Assert.Equal([1L, 2L], (await _env.Inventory.ListAllAsync()).Select(d => d.DeviceId!.Value));
        Assert.Equal(0, _env.CountChunks(3));
        Assert.True(File.Exists(Path.Combine(_env.ManualsPath, "c.pdf")));
        Assert.Equal(othersBefore, Others(1, 2));
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

    [Theory]
    [Trait("Criteria", "UC-04.1")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_property_or_with_only_blank_ones_nothing_is_deleted(bool blank)
    {
        await Inventory();
        var before = _env.Snapshot();
        var warnings = _env.Logs.Entries.Count(e => e.Level == LogLevel.Warning);

        var result = blank
            ? await Delete(new Device { Brand = "  ", Model = "", Manuals = [" "], Urls = [], Status = "\t" })
            : await _env.Remover.DeleteAsync(null);

        AssertFails(result, "invalid_fields");
        Assert.Equal(
            "No property was given. Give at least one property of the device (for example brand and model) to identify it. Nothing was deleted.",
            result.Error!.Message);
        Assert.Equal(before, _env.Snapshot());
        Assert.True(_env.Logs.Entries.Count(e => e.Level == LogLevel.Warning) > warnings);
    }

    [Fact]
    [Trait("Criteria", "UC-04.1")]
    public async Task Without_a_property_the_only_device_is_not_deleted_either()
    {
        var env = new TestEnvironment();
        using var _ = env;
        var only = await env.Registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "Dimmer 2", Manuals = [env.Pdf("a.pdf", "Text.")] });
        Assert.True(only.Ok);

        var result = await env.Remover.DeleteAsync(new Device());

        AssertFails(result, "invalid_fields");
        Assert.Equal(1, env.CountDevices());
        Assert.NotEqual(0, env.CountChunks(1));
    }

    [Fact]
    [Trait("Criteria", "UC-04.2")]
    public async Task No_device_matching_is_device_not_found_and_lists_the_properties()
    {
        await Inventory();
        var before = _env.Snapshot();
        var warnings = _env.Logs.Entries.Count(e => e.Level == LogLevel.Warning);

        // Each property exists in some device, but no device has them all; part of a value does not match either.
        var result = await Delete(new Device { Brand = "Samsung", Location = "Habitación" });
        var part = await Delete(new Device { Model = "Dimmer" });

        AssertFails(result, "device_not_found");
        Assert.Equal(
            "No registered device matches the properties (brand: Samsung, location: Habitación). Nothing was deleted. Ask the user for other details, or check the registered devices with find_devices.",
            result.Error!.Message);
        Assert.Null(result.Error.Devices);
        AssertFails(part, "device_not_found");
        Assert.Equal(before, _env.Snapshot());
        Assert.True(_env.Logs.Entries.Count(e => e.Level == LogLevel.Warning) >= warnings + 2);
    }

    [Fact]
    [Trait("Criteria", "UC-04.3")]
    public async Task Several_matching_devices_are_ambiguous_and_nothing_is_deleted()
    {
        var devices = await Inventory();
        var before = _env.Snapshot();

        var result = await Delete(new Device { Brand = "Shelly", DeviceType = "Dimmer" });

        AssertFails(result, "ambiguous_device");
        Assert.Equal(
            "2 registered devices match the properties (brand: Shelly, device_type: Dimmer). Nothing was deleted. Ask the user which one it is, or give more properties.",
            result.Error!.Message);
        Assert.Equal(TestEnvironment.Json(devices[0]) + "|" + TestEnvironment.Json(devices[1]), string.Join('|', result.Error.Devices!.Select(TestEnvironment.Json)));
        Assert.Equal(before, _env.Snapshot());
        Assert.Contains(_env.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("ambiguous_device", StringComparison.Ordinal));
    }

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

    [Fact]
    [Trait("Criteria", "UC-04.4")]
    public async Task An_unreadable_inventory_is_a_failed_error()
    {
        using var env = new TestEnvironment(wrapInventory: inventory => new UnreadableInventory(inventory));

        var result = await env.Remover.DeleteAsync(new Device { Brand = "Shelly" });

        AssertFails(result, "failed");
        Assert.Contains(env.Logs.Entries, e => e.Level == LogLevel.Error);
    }
}
