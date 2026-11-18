using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.IntegrationTests;

/// <summary>UC-10 · Update the data, UC-11 · Change the manuals, UC-12 · Reject an invalid update.</summary>
public sealed class UpdateDeviceTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private async Task<Device> Registered(string brand, string model, params string[] manuals)
    {
        var result = await _env.Registrar.RegisterAsync(new Device
        {
            Brand = brand,
            Model = model,
            Manuals = manuals,
            Location = "Salon",
            Color = "White",
            Urls = ["https://example.com/a"],
            PurchaseDate = new DateOnly(2025, 3, 1),
        });
        Assert.True(result.Ok);
        return result.Device!;
    }

    private Task<UpdateResult> Update(Device device, DeviceChanges changes) => _env.Updater.UpdateAsync(device.DeviceId, changes);

    private async Task<Device> Stored(Device device) => (await _env.Inventory.ListAllAsync()).Single(d => d.DeviceId == device.DeviceId);

    private async Task<UpdateResult> Rejected(Func<Task<UpdateResult>> update)
    {
        var before = _env.Snapshot();
        var warnings = _env.Logs.Entries.Count(e => e.Level == LogLevel.Warning);

        var result = await update();

        Assert.False(result.Ok);
        Assert.Null(result.Device);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(before, _env.Snapshot());
        Assert.True(_env.Logs.Entries.Count(e => e.Level == LogLevel.Warning) > warnings);
        return result;
    }

    // UC-10

    [Fact]
    [Trait("Criteria", "UC-10.2")]
    public async Task Changes_several_fields_and_a_list_replaces_the_previous_one()
    {
        var a = await Registered("Shelly", "Dimmer 2");

        var result = await Update(a, new DeviceChanges { Status = "archived", WarrantyEndDate = "2027-03-01", Urls = ["https://example.com/b"] });

        Assert.Equal("archived", result.Device!.Status);
        Assert.Equal(new DateOnly(2027, 3, 1), result.Device.WarrantyEndDate);
        Assert.Equal(["https://example.com/b"], result.Device.Urls);
    }

    [Fact]
    [Trait("Criteria", "UC-10.4,5")]
    public async Task Changing_the_model_normalizes_it_and_keeps_the_id_and_the_chunks()
    {
        var a = await Registered("Shelly", "Dimmer 2", _env.Pdf("a.pdf", "Alpha."));
        var chunks = _env.Chunks(a.DeviceId);

        var result = await Update(a, new DeviceChanges { Brand = "philips", Model = " hue-2/b ", Location = "trastero" });

        Assert.Equal("Philips", result.Device!.Brand);
        Assert.Equal("HUE-2/B", result.Device.Model);
        Assert.Equal("Trastero", result.Device.Location);
        Assert.Equal(a.DeviceId, result.Device.DeviceId);
        Assert.Equal(chunks, _env.Chunks(a.DeviceId));
        Assert.Single((await _env.Finder.FindAsync(new Device { Model = "hue-2/b" })).Devices);
        Assert.Empty((await _env.Finder.FindAsync(new Device { Model = "Dimmer 2" })).Devices);
    }

    // UC-11

    [Fact]
    [Trait("Criteria", "UC-11.1,2,4")]
    public async Task Adds_and_removes_manuals_and_leaves_the_others_as_they_were()
    {
        var a = await Registered("Acme", "Box", _env.Pdf("a.pdf", "Alpha."), _env.Pdf("b.pdf", "Beta."));
        _env.Pdf("c.pdf", "Gamma.");
        var kept = _env.Chunks(a.DeviceId).Where(c => c.Manual == "a.pdf").ToList();

        var result = await Update(a, new DeviceChanges { Manuals = ["a.pdf", "c.pdf"] });

        Assert.True(result.Ok);
        Assert.Equal(["a.pdf", "c.pdf"], result.Device!.Manuals);
        var chunks = _env.Chunks(a.DeviceId);
        Assert.Equal(kept, chunks.Where(c => c.Manual == "a.pdf").ToList());
        Assert.Contains(chunks, c => c.Manual == "c.pdf");
        Assert.DoesNotContain(chunks, c => c.Manual == "b.pdf");
        Assert.True(File.Exists(Path.Combine(_env.ManualsPath, "b.pdf")));
    }

    // UC-12

    [Fact]
    [Trait("Criteria", "UC-12.4,5,8")]
    public async Task Collisions_return_the_other_device_and_every_reason_comes_at_once()
    {
        var a = await Registered("Acme", "Box", _env.Pdf("a.pdf", "Alpha."));
        var b = await Registered("Other", "Thing");

        var brandOnly = await Rejected(() => Update(b, new DeviceChanges { Brand = "acme", Model = "BOX" }));
        var manual = await Rejected(() => Update(b, new DeviceChanges { Manuals = ["a.pdf"] }));
        var all = await Rejected(() => Update(b, new DeviceChanges { Brand = "acme", Model = "box", Manuals = ["a.pdf", "missing.pdf"] }));

        Assert.Equal("device_exists", Assert.Single(brandOnly.Errors).Code);
        Assert.Equal(a.DeviceId, brandOnly.Errors[0].Device!.DeviceId);
        Assert.Equal("manual_in_use", Assert.Single(manual.Errors).Code);
        Assert.Equal(a.DeviceId, manual.Errors[0].Device!.DeviceId);
        Assert.Equal(["manual_not_found", "device_exists", "manual_in_use"], all.Errors.Select(e => e.Code));
        Assert.Equal(["a.pdf"], (await Stored(a)).Manuals);
    }

    [Fact]
    [Trait("Criteria", "UC-12.10")]
    public async Task A_failure_while_indexing_rolls_everything_back()
    {
        using var env = new TestEnvironment(wrapIndex: index => new ThrowingIndex(index));
        var seed = await env.Inventory.InsertAsync(new Device { Brand = "Acme", Model = "BOX", Status = "active", Manuals = [], Urls = [] });
        env.Pdf("a.pdf", "Alpha.");
        var before = env.Snapshot();

        var result = await env.Updater.UpdateAsync(seed.DeviceId, new DeviceChanges { Color = "red", Manuals = ["a.pdf"] });

        var error = Assert.Single(result.Errors);
        Assert.Equal("failed", error.Code);
        Assert.Contains("Nothing was changed", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, env.Snapshot());
        Assert.Contains(env.Logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    [Trait("Criteria", "UC-12.4")]
    public async Task When_two_updates_race_the_database_decides()
    {
        RacyInventory racy = null!;
        using var env = new TestEnvironment(wrapInventory: inventory => racy = new RacyInventory(inventory));
        var a = (await env.Registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Box" })).Device!;
        var b = (await env.Registrar.RegisterAsync(new Device { Brand = "Other", Model = "Thing" })).Device!;
        var snapshot = env.Snapshot();

        racy.Blind = true;
        var result = await env.Updater.UpdateAsync(b.DeviceId, new DeviceChanges { Brand = "Acme", Model = "Box" });

        var error = Assert.Single(result.Errors);
        Assert.Equal("device_exists", error.Code);
        Assert.Equal(a.DeviceId, error.Device!.DeviceId);
        Assert.Equal(snapshot, env.Snapshot());
    }
}
