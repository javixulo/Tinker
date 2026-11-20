using Microsoft.Extensions.Logging;
using Tinker.Core;
using Tinker.Core.Models;
using Tinker.Core.Removal;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>UC-03 · Delete a device, UC-04 · Fail to delete a device. The real rollback is in the integration tests.</summary>
public sealed class DeviceRemoverTests
{
    private readonly FakeInventory _inventory = new();
    private readonly FakeIndex _index = new();
    private readonly FakeTransactions _transactions = new();
    private readonly ListLogger<DeviceRemover> _log = new();
    private readonly DeviceRemover _remover;

    public DeviceRemoverTests()
    {
        _remover = new DeviceRemover(_inventory, _index, _transactions, _log);
        _inventory.Seed(new Device { Brand = "Shelly", Model = "DIMMER 2", DeviceType = "Dimmer", Location = "Salon" });
        _inventory.Seed(new Device { Brand = "Shelly", Model = "RGBW2", DeviceType = "Dimmer", Location = "Habitación" });
        _inventory.Seed(new Device { Brand = "Samsung", Model = "QE55Q60", DeviceType = "TV", Location = "Salon" });
    }

    private static void AssertFails(DeletionResult result, string code)
    {
        Assert.False(result.Ok);
        Assert.Null(result.Device);
        Assert.Equal(code, result.Error!.Code);
    }

    private void AssertNothingChanged()
    {
        Assert.Empty(_inventory.Deleted);
        Assert.Empty(_index.DeletedByDevice);
        Assert.Equal(3, _inventory.Devices.Count);
    }

    [Fact]
    [Trait("Criteria", "UC-03.1")]
    public async Task Deletes_the_device_that_brand_and_model_single_out_with_its_chunks_in_one_transaction()
    {
        var result = await _remover.DeleteAsync(new Device { Brand = " SHELLY ", Model = "rgbw2" });

        Assert.True(result.Ok);
        Assert.Equal(2, result.Device!.DeviceId);
        Assert.Equal([2L], _inventory.Deleted);
        Assert.Equal([2L], _index.DeletedByDevice);
        Assert.Equal(1, _transactions.Committed);
        Assert.Equal([1L, 3L], _inventory.Devices.Select(d => d.DeviceId!.Value));
        Assert.True(_log.Has(LogLevel.Information, "Device 2 deleted"));
    }

    [Fact]
    [Trait("Criteria", "UC-03.2")]
    public async Task Deletes_the_device_that_other_properties_single_out()
    {
        // Type TV is not enough for the others, and Salon is shared with device 1: only together they single out device 3.
        var result = await _remover.DeleteAsync(new Device { DeviceType = "tv", Brand = "samsung", Location = "salon" });

        Assert.True(result.Ok);
        Assert.Equal(3, result.Device!.DeviceId);
        Assert.Equal([3L], _inventory.Deleted);
    }

    [Fact]
    [Trait("Criteria", "UC-03.1, UC-03.2")]
    public async Task Deletes_by_device_id()
    {
        var result = await _remover.DeleteAsync(new Device { DeviceId = 1 });

        Assert.True(result.Ok);
        Assert.Equal([1L], _inventory.Deleted);
    }

    [Theory]
    [Trait("Criteria", "UC-04.1")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_property_or_with_only_blank_ones_nothing_is_deleted(bool blank)
    {
        var result = await _remover.DeleteAsync(blank ? new Device { Brand = " ", Model = "", Manuals = [" "] } : null);

        AssertFails(result, ErrorCodes.InvalidFields);
        AssertNothingChanged();
        Assert.True(_log.Has(LogLevel.Warning, "Deletion rejected with"));
    }

    [Fact]
    [Trait("Criteria", "UC-04.1")]
    public async Task Without_a_property_the_only_device_is_not_deleted_either()
    {
        var single = new FakeInventory();
        single.Seed(new Device { Brand = "Shelly", Model = "X" });

        var result = await new DeviceRemover(single, _index, _transactions, _log).DeleteAsync(new Device());

        AssertFails(result, ErrorCodes.InvalidFields);
        Assert.Empty(single.Deleted);
    }

    [Fact]
    [Trait("Criteria", "UC-04.2")]
    public async Task No_device_matching_is_device_not_found_and_lists_the_properties()
    {
        var result = await _remover.DeleteAsync(new Device { Brand = "Shelly", Model = "Nothing" });

        AssertFails(result, ErrorCodes.DeviceNotFound);
        Assert.Contains("brand: Shelly", result.Error!.Message);
        Assert.Contains("model: Nothing", result.Error.Message);
        AssertNothingChanged();
    }

    [Fact]
    [Trait("Criteria", "UC-04.3")]
    public async Task Several_matching_devices_are_ambiguous_and_nothing_is_deleted()
    {
        var result = await _remover.DeleteAsync(new Device { Brand = "Shelly" });

        AssertFails(result, ErrorCodes.AmbiguousDevice);
        Assert.Equal([1L, 2L], result.Error!.Devices!.Select(d => d.DeviceId!.Value));
        AssertNothingChanged();
    }

    [Fact]
    [Trait("Criteria", "UC-04.4")]
    public async Task A_device_that_disappears_between_the_lookup_and_the_delete_is_device_not_found()
    {
        var race = new RaceInventory(_inventory);

        var result = await new DeviceRemover(race, _index, _transactions, _log).DeleteAsync(new Device { DeviceId = 1 });

        AssertFails(result, ErrorCodes.DeviceNotFound);
    }

    [Fact]
    [Trait("Criteria", "UC-04.4")]
    public async Task A_failure_in_the_index_is_a_failed_error_inside_the_transaction_and_hides_the_cause()
    {
        _index.Fails = new InvalidOperationException("index file /secret/path is corrupt");

        var result = await _remover.DeleteAsync(new Device { DeviceId = 1 });

        AssertFails(result, ErrorCodes.Failed);
        Assert.DoesNotContain("secret", result.Error!.Message);
        Assert.Equal(1, _transactions.RolledBack);
        Assert.Equal(0, _transactions.Committed);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Error is not null);
    }

    [Fact]
    [Trait("Criteria", "UC-04.4")]
    public async Task An_unreadable_inventory_is_a_failed_error_and_deletes_nothing()
    {
        _inventory.ListFails = new InvalidOperationException("cannot read");

        var result = await _remover.DeleteAsync(new Device { DeviceId = 1 });

        AssertFails(result, ErrorCodes.Failed);
        Assert.Empty(_inventory.Deleted);
    }

    /// <summary>Lists the device, but another request has deleted it by the time this one deletes.</summary>
    private sealed class RaceInventory(FakeInventory inner) : Tinker.Core.Storage.IInventory
    {
        public Task<Device?> FindByBrandAndModelAsync(string brand, string model, CancellationToken cancellationToken = default) => inner.FindByBrandAndModelAsync(brand, model, cancellationToken);

        public Task<Device?> FindByManualAsync(string manual, CancellationToken cancellationToken = default) => inner.FindByManualAsync(manual, cancellationToken);

        public Task<IReadOnlyList<Device>> ListAllAsync(CancellationToken cancellationToken = default) => inner.ListAllAsync(cancellationToken);

        public Task<Device> InsertAsync(Device device, CancellationToken cancellationToken = default) => inner.InsertAsync(device, cancellationToken);

        public Task<Device?> UpdateAsync(Device device, CancellationToken cancellationToken = default) => inner.UpdateAsync(device, cancellationToken);

        public Task<bool> DeleteAsync(long deviceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
