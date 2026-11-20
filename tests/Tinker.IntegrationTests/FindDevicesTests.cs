using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.IntegrationTests;

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
    [Trait("Criteria", "UC-05.AC1")]
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

    // UC-06

    // UC-07

    [Theory]
    [Trait("Criteria", "UC-07.AC2")]
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

}
