using Microsoft.Extensions.Logging;
using Tinker.Core;
using Tinker.Core.Finding;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>UC-05 · Find a single device, UC-06 · Find several devices, UC-07 · Find devices without a result.</summary>
public sealed class DeviceFinderTests
{
    private readonly FakeInventory _inventory = new();
    private readonly ListLogger<DeviceFinder> _log = new();
    private readonly DeviceFinder _finder;

    public DeviceFinderTests()
    {
        _finder = new DeviceFinder(_inventory, _log);
        _inventory.Seed(new Device { Brand = "Shelly", Model = "DIMMER 2", DeviceType = "Dimmer", Location = "Habitación", Color = "White", Manuals = ["a.pdf"], Urls = ["https://x/a", "https://x/b"] });
        _inventory.Seed(new Device { Brand = "Shelly", Model = "RGBW2", DeviceType = "Dimmer", Location = "Salon", Color = "White", Manuals = ["b.pdf"] });
        _inventory.Seed(new Device { Brand = "Fibaro", Model = "DOUBLE SMART", DeviceType = "Switch", Location = "Salon", Color = "Black", Manuals = ["c.pdf"] });
    }

    private static IEnumerable<long> Ids(FindDevicesResult result) => result.Devices.Select(d => d.DeviceId!.Value);

    [Fact]
    [Trait("Criteria", "UC-05.1")]
    public async Task Finds_a_device_by_a_property_no_other_device_has()
    {
        var result = await _finder.FindAsync(new Device { Model = "rgbw2" });

        Assert.True(result.Ok);
        Assert.Equal([2L], Ids(result));
        Assert.Null(result.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-05.2")]
    public async Task Finds_a_device_by_a_combination_that_only_one_matches()
    {
        var result = await _finder.FindAsync(new Device { DeviceType = "Dimmer", Location = "Salon" });

        Assert.Equal([2L], Ids(result));
    }

    [Fact]
    [Trait("Criteria", "UC-06.1")]
    public async Task Finds_every_device_that_shares_a_value_ordered_by_device_id()
    {
        var result = await _finder.FindAsync(new Device { Location = "Salon" });

        Assert.Equal([2L, 3L], Ids(result));
    }

    [Fact]
    [Trait("Criteria", "UC-06.2")]
    public async Task Every_property_given_must_match()
    {
        var result = await _finder.FindAsync(new Device { Location = "Salon", Color = "Black", Brand = "Shelly" });

        Assert.True(result.Ok);
        Assert.Empty(result.Devices);
    }

    [Fact]
    [Trait("Criteria", "UC-06.2")]
    public async Task For_a_list_the_device_must_have_every_item_given()
    {
        Assert.Equal([1L], Ids(await _finder.FindAsync(new Device { Urls = ["https://x/a", "https://x/b"] })));
        Assert.Empty((await _finder.FindAsync(new Device { Urls = ["https://x/a", "https://x/zzz"] })).Devices);
    }

    [Theory]
    [Trait("Criteria", "UC-06.3")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lists_every_device_when_no_property_or_only_blank_ones_are_given(bool blank)
    {
        var criteria = blank ? new Device { Brand = " ", Model = "", Location = "\t", Manuals = [" "], Urls = [] } : null;

        var result = await _finder.FindAsync(criteria);

        Assert.Equal([1L, 2L, 3L], Ids(result));
    }

    [Fact]
    [Trait("Criteria", "UC-05.1, UC-06.1")]
    public async Task Compares_in_lowercase_without_accents_and_ignoring_extra_spaces()
    {
        var result = await _finder.FindAsync(new Device { Location = "  HABITACION " });

        Assert.Equal([1L], Ids(result));
    }

    [Fact]
    [Trait("Criteria", "UC-06.2")]
    public async Task The_whole_value_must_match()
    {
        Assert.Empty((await _finder.FindAsync(new Device { Location = "Sal" })).Devices);
    }

    [Fact]
    [Trait("Criteria", "UC-05.3")]
    public async Task The_brand_is_cleaned_like_the_stored_one()
    {
        _inventory.Seed(new Device { Brand = "BSH", Model = "X1" });

        var result = await _finder.FindAsync(new Device { Brand = "B.S.H." });

        Assert.Equal([4L], Ids(result));
    }

    [Fact]
    [Trait("Criteria", "UC-07.1")]
    public async Task No_match_is_ok_with_an_empty_list_and_a_message_that_lists_the_properties()
    {
        var result = await _finder.FindAsync(new Device { Brand = "Nobody", Location = "Salon" });

        Assert.True(result.Ok);
        Assert.Empty(result.Devices);
        Assert.Contains("brand: Nobody", result.Message);
        Assert.Contains("location: Salon", result.Message);
        Assert.True(_log.Has(LogLevel.Warning, "No device matched"));
    }

    [Fact]
    [Trait("Criteria", "UC-07.3")]
    public async Task With_no_registered_devices_the_message_says_to_register_one()
    {
        var empty = new DeviceFinder(new FakeInventory(), _log);

        var result = await empty.FindAsync(new Device { Brand = "Shelly" });

        Assert.True(result.Ok);
        Assert.Empty(result.Devices);
        Assert.Equal(Messages.NoDevices, result.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-07.4")]
    public async Task An_unexpected_failure_is_a_failed_error_that_hides_the_cause_and_is_logged()
    {
        _inventory.ListFails = new InvalidOperationException("disk /secret/path is on fire");

        var result = await _finder.FindAsync(new Device { Brand = "Shelly" });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.Failed, result.Error!.Code);
        Assert.DoesNotContain("secret", result.Error.Message);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Error is InvalidOperationException);
    }

    [Fact]
    public async Task A_cancelled_request_is_not_turned_into_an_error()
    {
        _inventory.ListFails = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _finder.FindAsync(new Device()));
    }
}
