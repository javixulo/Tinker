using Microsoft.Extensions.Logging;
using Tinker.Core;
using Tinker.Core.Manuals;
using Tinker.Core.Models;
using Tinker.Core.Updating;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>
/// UC-10 to UC-12 with the manuals folder in memory (<see cref="FakeManualFiles"/>). That PDFs are really read is in the
/// integration tests.
/// </summary>
public sealed class DeviceUpdaterTests
{
    private readonly FakeManualFiles _files = new();
    private readonly FakeInventory _inventory = new();
    private readonly FakeIndex _index = new();
    private readonly FakeTransactions _transactions = new();
    private readonly ListLogger<DeviceUpdater> _log = new();
    private readonly DeviceUpdater _updater;

    public DeviceUpdaterTests()
    {
        _updater = new DeviceUpdater(
            _files, _inventory, _index, _transactions, _files, Embedder.Hash, _log);
        _files.WithFile("a.pdf", "Alpha.").WithFile("b.pdf", "Beta.").WithFile("c.pdf", "Gamma.");
        _inventory.Seed(new Device
        {
            Brand = "Shelly", Model = "DIMMER 2", DeviceType = "Dimmer", Color = "White", Location = "Salon", Invoice = "INV-1",
            Manuals = ["a.pdf", "b.pdf"], Urls = ["https://x/a"], PurchaseDate = new DateOnly(2025, 1, 2), Status = "active",
        });
        _inventory.Seed(new Device { Brand = "Fibaro", Model = "DOUBLE SMART", Manuals = ["c.pdf"], Status = "active" });
    }

    private Device Stored(long id) => _inventory.Devices.Single(d => d.DeviceId == id);

    private void AssertRejectedAndNothingWritten(UpdateResult result, params string[] codes)
    {
        Assert.False(result.Ok);
        Assert.Null(result.Device);
        Assert.Equal(codes, result.Errors.Select(e => e.Code));
        Assert.Equal(0, _inventory.Updates);
        Assert.Empty(_index.Added);
        Assert.Empty(_index.DeletedByManual);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC1,3")]
    public async Task Changes_one_field_and_keeps_the_rest()
    {
        var before = Stored(1);

        var result = await _updater.UpdateAsync(1, new DeviceChanges { Location = "kitchen" });

        Assert.True(result.Ok);
        Assert.Equal("Kitchen", result.Device!.Location);
        Assert.Equal(before with { Location = "Kitchen" }, result.Device);
        Assert.Equal(1, _transactions.Committed);
        Assert.True(_log.Has(LogLevel.Information, "Device 1 updated"));
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC2")]
    public async Task Changes_several_fields_and_a_list_replaces_the_previous_one()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges
        {
            Color = "dark  grey",
            Urls = ["https://y/1", " https://y/2 ", "https://y/1", " "],
            PurchaseDate = " 2024-12-31 ",
            Status = " archived ",
            Invoice = "  INV-9 ",
        });

        var device = result.Device!;
        Assert.Equal("Dark grey", device.Color);
        Assert.Equal(["https://y/1", "https://y/2"], device.Urls);
        Assert.Equal(new DateOnly(2024, 12, 31), device.PurchaseDate);
        Assert.Equal("archived", device.Status);
        Assert.Equal("INV-9", device.Invoice);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC4,5")]
    public async Task Changing_brand_and_model_normalizes_them_and_keeps_the_id_and_the_manuals()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Brand = "philips hué", Model = " lct  015 " });

        var device = result.Device!;
        Assert.Equal(1, device.DeviceId);
        Assert.Equal("Philips Hue", device.Brand);
        Assert.Equal("LCT 015", device.Model);
        Assert.Equal(["a.pdf", "b.pdf"], device.Manuals);
        Assert.Empty(_index.DeletedByManual);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC6")]
    public async Task A_device_does_not_collide_with_itself()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Brand = " shelly ", Model = "dimmer   2", Color = "Black" });

        Assert.True(result.Ok);
        Assert.Equal("Shelly", result.Device!.Brand);
        Assert.Equal("DIMMER 2", result.Device.Model);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC7")]
    public async Task Optional_fields_can_be_emptied_and_absent_ones_are_kept()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Color = "  ", Invoice = "", Urls = [], PurchaseDate = "" });

        var device = result.Device!;
        Assert.Null(device.Color);
        Assert.Null(device.Invoice);
        Assert.Empty(device.Urls!);
        Assert.Null(device.PurchaseDate);
        Assert.Equal("Dimmer", device.DeviceType);
        Assert.Equal("Salon", device.Location);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC8")]
    public async Task The_current_values_write_nothing()
    {
        var before = Stored(1);

        var result = await _updater.UpdateAsync(1, new DeviceChanges { Color = "white", Model = "dimmer 2", Manuals = ["a.pdf", "b.pdf"] });

        Assert.True(result.Ok);
        Assert.Equal(before, result.Device);
        Assert.Equal(0, _inventory.Updates);
        Assert.Equal(0, _transactions.Committed);
        Assert.Empty(_index.Added);
        Assert.True(_log.Has(LogLevel.Information, "Device 1 update changed nothing"));
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC1,2,4")]
    public async Task Removing_a_manual_deletes_its_chunks_only_and_keeps_the_others_without_reading_them()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Manuals = ["a.pdf"] });

        Assert.True(result.Ok);
        Assert.Equal(["a.pdf"], result.Device!.Manuals);
        Assert.Equal([(1L, "b.pdf")], _index.DeletedByManual);
        Assert.Empty(Assert.Single(_index.Added).Chunks);
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC5")]
    public async Task An_empty_list_removes_every_manual()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Manuals = [] });

        Assert.Empty(result.Device!.Manuals!);
        Assert.Equal(["a.pdf", "b.pdf"], _index.DeletedByManual.Select(d => d.Manual).Order());
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC10")]
    public async Task A_manual_the_device_already_has_is_not_checked_again_so_a_missing_file_does_not_block_other_changes()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Manuals = ["a.pdf", "b.pdf"], Color = "Red" });

        Assert.True(result.Ok);
        Assert.Equal("Red", result.Device!.Color);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC1")]
    public async Task A_device_that_does_not_exist_is_device_not_found()
    {
        var result = await _updater.UpdateAsync(99, new DeviceChanges { Color = "Red" });

        AssertRejectedAndNothingWritten(result, ErrorCodes.DeviceNotFound);
        Assert.Contains("99", result.Errors[0].Message);
    }

    [Theory]
    [Trait("Criteria", "UC-12.AC2,3")]
    [InlineData(null, "Color", "`device_id` is missing")]
    [InlineData(1L, null, "Nothing to change")]
    [InlineData(1L, "DeviceId", "`device_id` cannot be changed")]
    [InlineData(1L, "Brand", "`brand` cannot be emptied")]
    [InlineData(1L, "Model", "`model` cannot be emptied")]
    [InlineData(1L, "Status", "Invalid status")]
    [InlineData(1L, "EmptyStatus", "`status` cannot be emptied")]
    [InlineData(1L, "PurchaseDate", "`purchase_date` must be a date")]
    [InlineData(1L, "WarrantyEndDate", "`warranty_end_date` must be a date")]
    public async Task Invalid_input_is_invalid_fields_and_says_which_field(long? id, string? field, string expected)
    {
        var changes = field switch
        {
            null => new DeviceChanges(),
            "Color" => new DeviceChanges { Color = "Red" },
            "DeviceId" => new DeviceChanges { DeviceId = 5, Color = "Red" },
            "Brand" => new DeviceChanges { Brand = "***" },
            "Model" => new DeviceChanges { Model = "  " },
            "Status" => new DeviceChanges { Status = "sold" },
            "EmptyStatus" => new DeviceChanges { Status = " " },
            "PurchaseDate" => new DeviceChanges { PurchaseDate = "01/02/2025" },
            _ => new DeviceChanges { WarrantyEndDate = "2025-13-40" },
        };

        var result = await _updater.UpdateAsync(id, changes);

        AssertRejectedAndNothingWritten(result, ErrorCodes.InvalidFields);
        Assert.Contains(expected, result.Errors[0].Message);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC3")]
    public async Task Every_invalid_field_is_reported_at_once_and_the_device_is_not_looked_up()
    {
        var result = await _updater.UpdateAsync(null, new DeviceChanges { Status = "sold", PurchaseDate = "x" });

        var error = Assert.Single(result.Errors);
        Assert.Contains("`device_id`", error.Message);
        Assert.Contains("Invalid status", error.Message);
        Assert.Contains("`purchase_date`", error.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC1")]
    public async Task No_changes_at_all_are_rejected_as_nothing_to_change()
    {
        var result = await _updater.UpdateAsync(1, null);

        AssertRejectedAndNothingWritten(result, ErrorCodes.InvalidFields);
        Assert.Contains(Messages.NothingToChange, result.Errors[0].Message);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC4")]
    public async Task A_brand_and_model_of_another_device_is_device_exists_and_returns_that_device()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Brand = "fibaro", Model = "double  smart" });

        AssertRejectedAndNothingWritten(result, ErrorCodes.DeviceExists);
        Assert.Equal(2, result.Errors[0].Device!.DeviceId);
    }

    [Theory]
    [Trait("Criteria", "UC-12.AC6,7,9")]
    [InlineData("/etc/passwd", "outside the manuals folder")]
    [InlineData("a/../b.pdf", "`..`")]
    public async Task A_manual_that_is_not_allowed_rejects_the_whole_update(string manual, string reason)
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Color = "Red", Manuals = ["a.pdf", manual] });

        AssertRejectedAndNothingWritten(result, ErrorCodes.ManualNotAllowed);
        Assert.Contains(reason, result.Errors[0].Message);
        Assert.Equal("White", Stored(1).Color);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC4,5,8")]
    public async Task Every_reason_comes_at_once()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Brand = "Fibaro", Model = "Double Smart", Manuals = ["/etc/passwd"] });

        AssertRejectedAndNothingWritten(result, ErrorCodes.ManualNotAllowed, ErrorCodes.DeviceExists);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC4")]
    public async Task When_two_updates_race_the_database_decides_and_the_loser_gets_device_exists()
    {
        var racer = new FakeInventory();
        racer.Seed(new Device { Brand = "Shelly", Model = "DIMMER 2", Manuals = [] });
        racer.Blind = true;
        racer.RaceWinner = () => new Device { Brand = "Philips", Model = "HUE", Manuals = [] };
        var updater = new DeviceUpdater(
            _files, racer, _index, _transactions, _files, Embedder.Hash, _log);

        var result = await updater.UpdateAsync(1, new DeviceChanges { Brand = "Philips", Model = "Hue" });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.DeviceExists, Assert.Single(result.Errors).Code);
        Assert.Equal(1, _transactions.RolledBack);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC1")]
    public async Task A_device_deleted_between_the_lookup_and_the_write_is_device_not_found()
    {
        var racer = new FakeInventory();
        racer.Seed(new Device { Brand = "Shelly", Model = "X", Manuals = [] });
        var vanishing = new VanishingInventory(racer);
        var updater = new DeviceUpdater(
            _files, vanishing, _index, _transactions, _files, Embedder.Hash, _log);

        var result = await updater.UpdateAsync(1, new DeviceChanges { Color = "Red" });

        Assert.Equal(ErrorCodes.DeviceNotFound, Assert.Single(result.Errors).Code);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC10")]
    public async Task A_failure_while_writing_rolls_back_and_is_a_failed_error_that_hides_the_cause()
    {
        _inventory.UpdateFails = new InvalidOperationException("cannot write /secret/path");

        var result = await _updater.UpdateAsync(1, new DeviceChanges { Color = "Red" });

        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.Failed, error.Code);
        Assert.DoesNotContain("secret", error.Message);
        Assert.Equal(1, _transactions.RolledBack);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Error is not null);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC10")]
    public async Task A_failure_in_the_index_rolls_back_the_whole_update()
    {
        _index.Fails = new InvalidOperationException("index broken");

        var result = await _updater.UpdateAsync(1, new DeviceChanges { Manuals = ["a.pdf"] });

        Assert.Equal(ErrorCodes.Failed, Assert.Single(result.Errors).Code);
        Assert.Equal(1, _transactions.RolledBack);
        Assert.Equal(0, _transactions.Committed);
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC3,6,7,8")]
    public async Task Adds_manuals_together_with_data_reading_only_the_new_ones()
    {
        _files.WithFile("shelly/new.pdf", "Press reset.", "Second page.");

        var result = await _updater.UpdateAsync(1, new DeviceChanges { Color = "red", Manuals = ["a.pdf", "b.pdf", "shelly/new.pdf"] });

        Assert.True(result.Ok);
        Assert.Equal("Red", result.Device!.Color);
        Assert.Equal(["a.pdf", "b.pdf", "shelly/new.pdf"], result.Device.Manuals);
        Assert.Equal(["shelly/new.pdf"], _files.Read);
        Assert.Empty(_index.DeletedByManual);
        var (id, chunks) = Assert.Single(_index.Added);
        Assert.Equal(1L, id);
        Assert.Equal([("shelly/new.pdf", 1), ("shelly/new.pdf", 2)], chunks.Select(c => (c.Manual, c.Page)));
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC1,2,4")]
    public async Task Adds_and_removes_manuals_in_one_update_and_leaves_the_others_as_they_were()
    {
        _files.WithFile("new.pdf", "Fresh.");

        var result = await _updater.UpdateAsync(1, new DeviceChanges { Manuals = ["b.pdf", "new.pdf"] });

        Assert.Equal(["b.pdf", "new.pdf"], result.Device!.Manuals);
        Assert.Equal([(1L, "a.pdf")], _index.DeletedByManual);
        Assert.Equal(["new.pdf"], Assert.Single(_index.Added).Chunks.Select(c => c.Manual).Distinct());
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC9")]
    public async Task A_manual_freed_by_one_device_can_be_added_to_another()
    {
        await _updater.UpdateAsync(1, new DeviceChanges { Manuals = ["b.pdf"] });

        var result = await _updater.UpdateAsync(2, new DeviceChanges { Manuals = ["c.pdf", "a.pdf"] });

        Assert.True(result.Ok);
        Assert.Equal(["c.pdf", "a.pdf"], result.Device!.Manuals);
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC10")]
    public async Task A_manual_whose_file_is_gone_does_not_block_other_changes_and_can_still_be_removed()
    {
        _files.Delete("a.pdf");

        var changed = await _updater.UpdateAsync(1, new DeviceChanges { Color = "red" });
        var kept = await _updater.UpdateAsync(1, new DeviceChanges { Manuals = ["a.pdf", "b.pdf"], Color = "blue" });
        var removed = await _updater.UpdateAsync(1, new DeviceChanges { Manuals = [] });

        Assert.True(changed.Ok);
        Assert.True(kept.Ok);
        Assert.True(removed.Ok);
        Assert.Empty(removed.Device!.Manuals!);
        Assert.Empty(_files.Read);
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC11")]
    public async Task A_manual_repeated_counts_once()
    {
        _files.WithFile("shelly/a.pdf", "Alpha.");

        var result = await _updater.UpdateAsync(2, new DeviceChanges { Manuals = ["shelly/a.pdf", "shelly/./a.pdf"] });

        Assert.Equal(["shelly/a.pdf"], result.Device!.Manuals);
        Assert.Equal(["shelly/a.pdf"], _files.Read);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC6,7,9")]
    public async Task A_missing_manual_rejects_the_whole_update()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Color = "red", Manuals = ["a.pdf", "nope.pdf"] });

        AssertRejectedAndNothingWritten(result, ErrorCodes.ManualNotFound);
        Assert.Contains("nope.pdf", result.Errors[0].Message, StringComparison.Ordinal);
        Assert.Equal("White", Stored(1).Color);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC6,7,9")]
    public async Task Every_manual_that_is_not_allowed_is_reported()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Color = "red", Manuals = ["b.pdf", "../x.pdf", "/etc/passwd", "%2e%2e/s.pdf"] });

        AssertRejectedAndNothingWritten(result, ErrorCodes.ManualNotAllowed, ErrorCodes.ManualNotAllowed, ErrorCodes.ManualNotAllowed);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC8")]
    public async Task A_manual_that_belongs_to_another_device_is_manual_in_use_and_returns_that_device()
    {
        var result = await _updater.UpdateAsync(1, new DeviceChanges { Manuals = ["a.pdf", "c.pdf"] });

        AssertRejectedAndNothingWritten(result, ErrorCodes.ManualInUse);
        Assert.Equal(2, result.Errors[0].Device!.DeviceId);
    }

    [Fact]
    [Trait("Criteria", "UC-12.AC11")]
    public async Task A_manual_that_cannot_be_read_rejects_the_whole_update()
    {
        _files.WithFile("good.pdf", "Alpha.").WithUnreadableFile("notes.txt", ManualUnreadableException.NotAPdf);

        var result = await _updater.UpdateAsync(1, new DeviceChanges { Color = "red", Manuals = ["good.pdf", "notes.txt"] });

        AssertRejectedAndNothingWritten(result, ErrorCodes.ManualUnreadable);
        Assert.Contains("notes.txt (it is not a PDF)", result.Errors[0].Message, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", result.Errors[0].Message, StringComparison.Ordinal);
    }

    /// <summary>Lists the device, but another request has deleted it by the time this one writes.</summary>
    private sealed class VanishingInventory(FakeInventory inner) : Tinker.Core.Storage.IInventory
    {
        public Task<Device?> FindByBrandAndModelAsync(string brand, string model, CancellationToken cancellationToken = default) => inner.FindByBrandAndModelAsync(brand, model, cancellationToken);

        public Task<Device?> FindByManualAsync(string manual, CancellationToken cancellationToken = default) => inner.FindByManualAsync(manual, cancellationToken);

        public Task<IReadOnlyList<Device>> ListAllAsync(CancellationToken cancellationToken = default) => inner.ListAllAsync(cancellationToken);

        public Task<Device> InsertAsync(Device device, CancellationToken cancellationToken = default) => inner.InsertAsync(device, cancellationToken);

        public Task<Device?> UpdateAsync(Device device, CancellationToken cancellationToken = default) => Task.FromResult<Device?>(null);

        public Task<bool> DeleteAsync(long deviceId, CancellationToken cancellationToken = default) => inner.DeleteAsync(deviceId, cancellationToken);
    }
}