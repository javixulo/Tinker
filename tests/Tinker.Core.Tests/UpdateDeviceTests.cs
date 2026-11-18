using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.Core.Tests;

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
    [Trait("Criteria", "UC-10.1,3")]
    public async Task Changes_one_field_and_keeps_the_rest()
    {
        var a = await Registered("Shelly", "Dimmer 2");
        var b = await Registered("Shelly", "RGBW2");

        var result = await Update(a, new DeviceChanges { Location = "garage" });

        Assert.True(result.Ok);
        Assert.Equal("Garage", result.Device!.Location);
        Assert.Equal(TestEnvironment.Json(result.Device), TestEnvironment.Json(await Stored(a)));
        Assert.Equal(TestEnvironment.Json(a with { Location = "Garage" }), TestEnvironment.Json(result.Device));
        Assert.Equal(TestEnvironment.Json(b), TestEnvironment.Json(await Stored(b)));
        Assert.Contains(_env.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.StartsWith("Update completed", StringComparison.Ordinal));
    }

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

    [Fact]
    [Trait("Criteria", "UC-10.6")]
    public async Task A_device_does_not_collide_with_itself()
    {
        var a = await Registered("Shelly", "Dimmer 2");

        var result = await Update(a, new DeviceChanges { Brand = "SHELLY", Model = " dimmer   2" });

        Assert.True(result.Ok);
        Assert.Equal("SHELLY", result.Device!.Brand);
        Assert.Equal("DIMMER 2", result.Device.Model);
    }

    [Fact]
    [Trait("Criteria", "UC-10.7")]
    public async Task Optional_fields_can_be_emptied()
    {
        var a = await Registered("Shelly", "Dimmer 2", _env.Pdf("a.pdf", "Alpha."));

        var result = await Update(a, new DeviceChanges { Location = " ", Color = "", Urls = [], PurchaseDate = "", Manuals = [] });

        var device = result.Device!;
        Assert.Null(device.Location);
        Assert.Null(device.Color);
        Assert.Empty(device.Urls!);
        Assert.Null(device.PurchaseDate);
        Assert.Empty(device.Manuals!);
        Assert.Equal("Shelly", device.Brand);
        Assert.Equal(0, _env.CountChunks(a.DeviceId));
    }

    [Fact]
    [Trait("Criteria", "UC-10.8")]
    public async Task The_current_values_write_nothing()
    {
        var a = await Registered("Shelly", "Dimmer 2", _env.Pdf("a.pdf", "Alpha."));
        var snapshot = _env.Snapshot();

        var result = await Update(a, new DeviceChanges { Brand = "shelly", Model = "dimmer 2", Manuals = ["a.pdf"], Location = "salon" });

        Assert.True(result.Ok);
        Assert.Equal(TestEnvironment.Json(await Stored(a)), TestEnvironment.Json(result.Device!));
        Assert.Equal(snapshot, _env.Snapshot());
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

    [Fact]
    [Trait("Criteria", "UC-11.3,6,7,8")]
    public async Task Adds_manuals_to_a_device_without_any_together_with_data()
    {
        var a = await Registered("Acme", "Box");
        var manual = _env.Pdf("shelly/a.pdf", "Alpha.");

        var result = await Update(a, new DeviceChanges { Color = "red", Manuals = [manual] });

        Assert.Equal("Red", result.Device!.Color);
        Assert.Equal(["shelly/a.pdf"], result.Device.Manuals);
        Assert.All(_env.Chunks(a.DeviceId), c => Assert.Equal("shelly/a.pdf", c.Manual));
        Assert.NotEmpty(_env.Chunks(a.DeviceId));
    }

    [Fact]
    [Trait("Criteria", "UC-11.9")]
    public async Task A_manual_freed_by_one_device_can_be_added_to_another()
    {
        var a = await Registered("Acme", "Box", _env.Pdf("a.pdf", "Alpha."));
        var b = await Registered("Acme", "Other");

        Assert.True((await Update(a, new DeviceChanges { Manuals = [] })).Ok);
        Assert.True((await Update(b, new DeviceChanges { Manuals = ["a.pdf"] })).Ok);

        Assert.All(_env.Chunks(), c => Assert.Equal(b.DeviceId, c.DeviceId));
    }

    [Fact]
    [Trait("Criteria", "UC-11.10")]
    public async Task A_manual_whose_file_is_gone_does_not_block_other_changes()
    {
        var a = await Registered("Acme", "Box", _env.Pdf("a.pdf", "Alpha."));
        File.Delete(Path.Combine(_env.ManualsPath, "a.pdf"));

        Assert.True((await Update(a, new DeviceChanges { Color = "red" })).Ok);
        Assert.True((await Update(a, new DeviceChanges { Manuals = ["a.pdf"], Color = "blue" })).Ok);
        Assert.NotEmpty(_env.Chunks(a.DeviceId));

        var result = await Update(a, new DeviceChanges { Manuals = [] });

        Assert.True(result.Ok);
        Assert.Empty(result.Device!.Manuals!);
        Assert.Equal(0, _env.CountChunks(a.DeviceId));
    }

    [Fact]
    [Trait("Criteria", "UC-11.11")]
    public async Task A_manual_repeated_counts_once()
    {
        var a = await Registered("Acme", "Box");
        _env.Pdf("shelly/a.pdf", "Alpha.");

        var result = await Update(a, new DeviceChanges { Manuals = ["shelly/a.pdf", "shelly/./a.pdf"] });

        Assert.Equal(["shelly/a.pdf"], result.Device!.Manuals);
        Assert.Equal(1, _env.CountChunks(a.DeviceId));
    }

    // UC-12

    [Fact]
    [Trait("Criteria", "UC-12.1")]
    public async Task A_device_that_does_not_exist_is_device_not_found()
    {
        await Registered("Acme", "Box");

        var result = await Rejected(() => _env.Updater.UpdateAsync(99, new DeviceChanges { Color = "red" }));

        var error = Assert.Single(result.Errors);
        Assert.Equal("device_not_found", error.Code);
        Assert.Contains("find_devices", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, _env.CountDevices());
    }

    [Fact]
    [Trait("Criteria", "UC-12.2,3")]
    public async Task Invalid_input_is_invalid_fields_and_says_which_field()
    {
        var a = await Registered("Acme", "Box");

        var nothing = await Rejected(() => Update(a, new DeviceChanges()));
        var noId = await Rejected(() => _env.Updater.UpdateAsync(null, new DeviceChanges { Color = "red" }));
        var idInside = await Rejected(() => Update(a, new DeviceChanges { DeviceId = 5, Color = "red" }));
        var emptied = await Rejected(() => Update(a, new DeviceChanges { Brand = "...", Model = " ", Status = "" }));
        var status = await Rejected(() => Update(a, new DeviceChanges { Status = "sold" }));
        var date = await Rejected(() => Update(a, new DeviceChanges { PurchaseDate = "yesterday" }));

        Assert.Contains("at least one field", Assert.Single(nothing.Errors).Message, StringComparison.Ordinal);
        Assert.Contains("`device_id` is missing", Assert.Single(noId.Errors).Message, StringComparison.Ordinal);
        Assert.Contains("cannot be changed", Assert.Single(idInside.Errors).Message, StringComparison.Ordinal);
        var message = Assert.Single(emptied.Errors).Message;
        Assert.Contains("`brand` cannot be emptied", message, StringComparison.Ordinal);
        Assert.Contains("`model` cannot be emptied", message, StringComparison.Ordinal);
        Assert.Contains("`status` cannot be emptied", message, StringComparison.Ordinal);
        Assert.Contains("active, inactive, archived", Assert.Single(status.Errors).Message, StringComparison.Ordinal);
        Assert.Contains("purchase_date", Assert.Single(date.Errors).Message, StringComparison.Ordinal);
        Assert.All(new[] { nothing, noId, idInside, emptied, status, date }, r => Assert.Equal("invalid_fields", Assert.Single(r.Errors).Code));
    }

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
    [Trait("Criteria", "UC-12.6,7,9")]
    public async Task Manuals_that_are_not_allowed_or_missing_roll_back_everything()
    {
        var a = await Registered("Acme", "Box", _env.Pdf("a.pdf", "Alpha."));
        _env.Pdf("b.pdf", "Beta.");

        var missing = await Rejected(() => Update(a, new DeviceChanges { Manuals = ["a.pdf", "nope.pdf"] }));
        var outside = await Rejected(() => Update(a, new DeviceChanges { Color = "red", Manuals = ["b.pdf", "../x.pdf", "/etc/passwd", "%2e%2e/s.pdf"] }));

        Assert.Equal("manual_not_found", Assert.Single(missing.Errors).Code);
        Assert.Equal(["manual_not_allowed", "manual_not_allowed", "manual_not_allowed"], outside.Errors.Select(e => e.Code));
        Assert.Equal("White", (await Stored(a)).Color);
    }

    [Fact]
    [Trait("Criteria", "UC-12.11")]
    public async Task A_manual_that_cannot_be_read_rejects_the_whole_update()
    {
        var a = await Registered("Acme", "Box");
        File.WriteAllText(Path.Combine(_env.ManualsPath, "notes.txt"), "just text");
        var good = _env.Pdf("good.pdf", "Alpha.");

        var result = await Rejected(() => Update(a, new DeviceChanges { Color = "red", Manuals = [good, "notes.txt"] }));

        var error = Assert.Single(result.Errors);
        Assert.Equal("manual_unreadable", error.Code);
        Assert.Contains("notes.txt (it is not a PDF)", error.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", error.Message, StringComparison.Ordinal);
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
