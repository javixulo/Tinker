using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.Core.Tests;

/// <summary>UC-05 · Find a single device, UC-06 · Find several devices, UC-07 · Find devices without a result.</summary>
public sealed class FindDevicesTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    /// <summary>Four devices, with properties shared in different ways. Returns them by device_id (1 to 4).</summary>
    private async Task<List<Device>> Inventory()
    {
        var devices = new List<Device>();
        var rows = new (string Brand, string Model, string Type, string Location, string Color, string File)[]
        {
            ("Shelly", "Dimmer 2", "Dimmer", "Habitación", "White", "a.pdf"),
            ("Shelly", "RGBW2", "Dimmer", "Salon", "White", "b.pdf"),
            ("Fibaro", "Double Smart", "Switch", "Salon", "Black", "c.pdf"),
            ("Samsung", "QE55Q60", "TV", "Salon", "Black", "d.pdf"),
        };
        foreach (var row in rows)
        {
            var result = await _env.Registrar.RegisterAsync(new Device
            {
                Brand = row.Brand,
                Model = row.Model,
                DeviceType = row.Type,
                Location = row.Location,
                Color = row.Color,
                Manuals = [_env.Pdf(row.File, "Text of " + row.File)],
                Urls = ["https://example.com/" + row.Model.Replace(' ', '-')],
                PurchaseDate = new DateOnly(2025, 3, 1),
                Status = "active",
            });
            Assert.True(result.Ok);
            devices.Add(result.Device!);
        }

        return devices;
    }

    private static string Json(IEnumerable<Device> devices) => string.Join('|', devices.Select(TestEnvironment.Json));

    private Task<FindDevicesResult> Find(Device criteria) => _env.Finder.FindAsync(criteria);

    private static void AssertFoundOnly(FindDevicesResult result, params Device[] expected)
    {
        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Null(result.Message);
        Assert.Equal(Json(expected), Json(result.Devices));
    }

    // UC-05

    [Fact]
    [Trait("Criteria", "UC-05.1")]
    public async Task Finds_a_device_by_a_property_no_other_device_has()
    {
        var devices = await Inventory();

        var byModel = await Find(new Device { Model = "QE55Q60" });
        var byId = await Find(new Device { DeviceId = devices[1].DeviceId });
        var byManual = await Find(new Device { Manuals = ["c.pdf"] });
        var byUrl = await Find(new Device { Urls = ["https://example.com/Dimmer-2"] });

        AssertFoundOnly(byModel, devices[3]);
        AssertFoundOnly(byId, devices[1]);
        AssertFoundOnly(byManual, devices[2]);
        AssertFoundOnly(byUrl, devices[0]);
        Assert.NotNull(byModel.Devices[0].DeviceId);
        Assert.Equal(["d.pdf"], byModel.Devices[0].Manuals);
    }

    [Fact]
    [Trait("Criteria", "UC-05.2")]
    public async Task Finds_a_device_by_a_combination_that_only_one_matches()
    {
        var devices = await Inventory();

        // Brand Shelly is shared by two devices, type Dimmer too; the model tells them apart.
        var result = await Find(new Device { Brand = "Shelly", DeviceType = "Dimmer", Model = "RGBW2" });
        // Location Salon is shared by three, color Black by two; together they leave two, and the type leaves one.
        var other = await Find(new Device { Location = "Salon", Color = "Black", DeviceType = "TV" });

        AssertFoundOnly(result, devices[1]);
        AssertFoundOnly(other, devices[3]);
    }

    // UC-06

    [Fact]
    [Trait("Criteria", "UC-06.1")]
    public async Task Finds_every_device_that_shares_a_value_ordered_by_device_id()
    {
        var devices = await Inventory();

        var result = await Find(new Device { Location = "Salon" });

        AssertFoundOnly(result, devices[1], devices[2], devices[3]);
        Assert.Equal([2L, 3L, 4L], result.Devices.Select(d => d.DeviceId!.Value));
    }

    [Fact]
    [Trait("Criteria", "UC-06.2")]
    public async Task Every_property_given_must_match()
    {
        var devices = await Inventory();

        var result = await Find(new Device { Location = "Salon", Color = "White" });
        var none = await Find(new Device { Location = "Salon", Color = "White", Brand = "Samsung" });
        var dates = await Find(new Device { PurchaseDate = new DateOnly(2025, 3, 1), Brand = "Shelly", Status = "active" });

        AssertFoundOnly(result, devices[1]);
        Assert.Empty(none.Devices);
        AssertFoundOnly(dates, devices[0], devices[1]);
    }

    [Fact]
    [Trait("Criteria", "UC-06.3")]
    public async Task Lists_every_device_when_no_property_or_only_blank_ones_are_given()
    {
        var devices = await Inventory();

        var nothing = await _env.Finder.FindAsync(null);
        var empty = await Find(new Device());
        var blank = await Find(new Device { Brand = "  ", Model = "", Manuals = [" "], Urls = [], Status = "\t" });

        AssertFoundOnly(nothing, devices.ToArray());
        AssertFoundOnly(empty, devices.ToArray());
        AssertFoundOnly(blank, devices.ToArray());
    }

    [Fact]
    [Trait("Criteria", "UC-05.1, UC-06.1")]
    public async Task Compares_in_lowercase_without_accents_and_ignoring_extra_spaces()
    {
        var devices = await Inventory();

        var lower = await Find(new Device { Brand = "SHELLY", Location = "habitacion" });
        var accented = await Find(new Device { Location = "  HABITACIÓN " });
        var spaces = await Find(new Device { Model = " dimmer   2 " });
        var listed = await Find(new Device { Manuals = ["A.PDF"] });

        AssertFoundOnly(lower, devices[0]);
        AssertFoundOnly(accented, devices[0]);
        AssertFoundOnly(spaces, devices[0]);
        AssertFoundOnly(listed, devices[0]);
    }

    [Fact]
    [Trait("Criteria", "UC-06.2")]
    public async Task For_a_list_the_device_must_have_every_item_given()
    {
        var devices = await Inventory();
        var several = await _env.Registrar.RegisterAsync(new Device
        {
            Brand = "Acme",
            Model = "Two manuals",
            Manuals = [_env.Pdf("e.pdf", "Alpha."), _env.Pdf("f.pdf", "Beta.")],
        });
        Assert.True(several.Ok);

        var both = await Find(new Device { Manuals = ["f.pdf", "e.pdf"] });
        var oneOfThem = await Find(new Device { Manuals = ["e.pdf"] });
        var mixed = await Find(new Device { Manuals = ["e.pdf", "a.pdf"] });

        AssertFoundOnly(both, several.Device!);
        AssertFoundOnly(oneOfThem, several.Device!);
        Assert.Empty(mixed.Devices);
        Assert.NotEmpty(devices);
    }

    [Fact]
    [Trait("Criteria", "UC-05.3")]
    public async Task The_brand_is_cleaned_like_the_stored_one()
    {
        var bsh = await _env.Registrar.RegisterAsync(new Device { Brand = "B.S.H.", Model = "X1" });
        var hue = await _env.Registrar.RegisterAsync(new Device { Brand = "Philips Hué", Model = "Go" });

        AssertFoundOnly(await Find(new Device { Brand = "B.S.H." }), bsh.Device!);
        AssertFoundOnly(await Find(new Device { Brand = "Philips Hué" }), hue.Device!);
        AssertFoundOnly(await Find(new Device { Brand = "philips hue" }), hue.Device!);
        // Nothing left once cleaned: the brand counts as not given, so every device matches.
        Assert.Equal(2, (await Find(new Device { Brand = "..." })).Devices.Count);
    }

    // UC-07

    [Fact]
    [Trait("Criteria", "UC-07.1")]
    public async Task No_match_is_ok_with_an_empty_list_and_a_message_that_lists_the_properties()
    {
        await Inventory();
        var warnings = _env.Logs.Entries.Count(e => e.Level == LogLevel.Warning);

        var result = await Find(new Device { Brand = "Samsung", Location = "Garage" });

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Empty(result.Devices);
        Assert.Equal(
            "No registered device matches (brand: Samsung, location: Garage). Ask the user for other details, or register the device.",
            result.Message);
        Assert.True(_env.Logs.Entries.Count(e => e.Level == LogLevel.Warning) > warnings, "a lookup that finds nothing is a warning");
    }

    [Theory]
    [Trait("Criteria", "UC-07.2")]
    [InlineData("Dimm")]
    [InlineData("imer 2")]
    [InlineData("Dimmer")]
    public async Task Part_of_a_value_does_not_match(string part)
    {
        await Inventory();

        var result = await Find(new Device { Model = part });

        Assert.True(result.Ok);
        Assert.Empty(result.Devices);
        Assert.Contains("No registered device matches (model: " + part + ")", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-07.3")]
    public async Task With_no_registered_devices_the_message_says_to_register_one()
    {
        var withoutProperties = await Find(new Device());
        var withProperties = await Find(new Device { Brand = "Shelly" });

        foreach (var result in new[] { withoutProperties, withProperties })
        {
            Assert.True(result.Ok);
            Assert.Null(result.Error);
            Assert.Empty(result.Devices);
            Assert.Equal("There are no registered devices. Register one with register_device.", result.Message);
        }
    }

    [Fact]
    [Trait("Criteria", "UC-07.4")]
    public async Task An_unexpected_failure_is_a_failed_error_and_is_logged()
    {
        using var env = new TestEnvironment(wrapInventory: inventory => new UnreadableInventory(inventory));

        var result = await env.Finder.FindAsync(new Device { Brand = "Shelly" });

        Assert.False(result.Ok);
        Assert.Empty(result.Devices);
        Assert.Null(result.Message);
        var error = result.Error!;
        Assert.Equal("failed", error.Code);
        Assert.Contains("not because of the request", error.Message, StringComparison.Ordinal);
        Assert.Contains("Tell the user instead of retrying", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("unreadable", error.Message, StringComparison.Ordinal);
        Assert.Contains(env.Logs.Entries, e => e.Level == LogLevel.Error);
    }
}
