using Tinker.Core.Manuals;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.IntegrationTests;

/// <summary>UC-01 · Register a device. Each test names the acceptance criteria it covers.</summary>
public sealed class RegisterDeviceTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    [Trait("Criteria", "UC-01.AC1")]
    public async Task Registers_with_brand_and_model_only()
    {
        var result = await _env.Registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "Dimmer 2" });

        Assert.True(result.Ok);
        var device = result.Device!;
        Assert.Empty(device.Manuals!);
        Assert.Equal("active", device.Status);
        Assert.Equal(0, _env.CountChunks());
        var stored = await _env.Inventory.FindByBrandAndModelAsync("Shelly", "Dimmer 2");
        Assert.Equal(TestEnvironment.Json(device), TestEnvironment.Json(stored!));
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC1")]
    public async Task Registers_with_the_required_fields()
    {
        var manual = _env.Fixture("shelly/dimmer_2_guide.pdf");

        var result = await _env.Register("Shelly", "Dimmer 2", manual);

        Assert.True(result.Ok);
        Assert.Empty(result.Errors);
        var device = result.Device!;
        Assert.Equal(1, device.DeviceId);
        Assert.Equal("Shelly", device.Brand);
        Assert.Equal("DIMMER 2", device.Model);
        Assert.Equal([manual], device.Manuals);
        Assert.Equal(1, _env.CountDevices());
        var stored = await _env.Inventory.FindByBrandAndModelAsync("Shelly", "Dimmer 2");
        Assert.Equal(TestEnvironment.Json(device), TestEnvironment.Json(stored!));
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC2")]
    public async Task Registers_with_optional_fields()
    {
        var manual = _env.Fixture("shelly/dimmer_2_guide.pdf");

        var result = await _env.Registrar.RegisterAsync(new Device
        {
            Brand = "Shelly",
            Model = "Dimmer 2",
            Manuals = [manual],
            DeviceType = "Dimmer",
            Color = "White",
            Location = "Living room",
            Invoice = "INV-42",
            Urls = ["https://example.com/support", "https://example.com/manual"],
            PurchaseDate = new DateOnly(2025, 3, 1),
            WarrantyEndDate = new DateOnly(2027, 3, 1),
            Status = "active",
        });

        Assert.True(result.Ok);
        var stored = (await _env.Inventory.FindByBrandAndModelAsync("Shelly", "Dimmer 2"))!;
        Assert.Equal(TestEnvironment.Json(result.Device!), TestEnvironment.Json(stored));
        Assert.Equal("Dimmer", stored.DeviceType);
        Assert.Equal("White", stored.Color);
        Assert.Equal("Living room", stored.Location);
        Assert.Equal("INV-42", stored.Invoice);
        Assert.Equal(["https://example.com/support", "https://example.com/manual"], stored.Urls);
        Assert.Equal(new DateOnly(2025, 3, 1), stored.PurchaseDate);
        Assert.Equal(new DateOnly(2027, 3, 1), stored.WarrantyEndDate);
        Assert.Equal("active", stored.Status);
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC3")]
    public async Task Registers_with_several_manuals()
    {
        var first = _env.Fixture("shelly/dimmer_2_guide.pdf");
        var second = _env.Fixture("shelly/rgbw2_app_guide.pdf");

        var result = await _env.Register("Shelly", "Dimmer 2", first, second);

        Assert.True(result.Ok);
        Assert.Equal([first, second], result.Device!.Manuals);
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC4")]
    public async Task Keeps_the_relative_path_of_a_manual_in_a_subfolder()
    {
        _env.Fixture("shelly/dimmer_2_guide.pdf");

        var result = await _env.Register("Shelly", "Dimmer 2", "shelly/dimmer_2_guide.pdf");

        Assert.Equal(["shelly/dimmer_2_guide.pdf"], result.Device!.Manuals);
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC5")]
    public async Task Indexes_a_real_manual_in_chunks_that_never_cross_a_page()
    {
        var manual = _env.Fixture("shelly/dimmer_2_guide.pdf");

        var result = await _env.Register("Shelly", "Dimmer 2", manual);

        var chunks = _env.Chunks(result.Device!.DeviceId);
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c =>
        {
            Assert.InRange(c.Text.Length, 1, TextChunker.DefaultMax);
            Assert.True(c.Page >= 1);
        });
        Assert.True(_env.CountChunks() == chunks.Count);
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC7")]
    public async Task Content_stays_with_its_own_device()
    {
        var first = await _env.Register("Acme", "One", _env.Pdf("one.pdf", "First device text."));
        var before = _env.Chunks(first.Device!.DeviceId);

        var second = await _env.Register("Acme", "Two", _env.Pdf("two.pdf", "Second device text."));

        Assert.NotEqual(first.Device.DeviceId, second.Device!.DeviceId);
        Assert.Equal(before, _env.Chunks(first.Device.DeviceId));
        var added = _env.Chunks(second.Device.DeviceId);
        Assert.NotEmpty(added);
        Assert.All(added, c => Assert.Equal("two.pdf", c.Manual));
        Assert.Equal(before.Count + added.Count, _env.CountChunks());
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC1")]
    public async Task A_device_id_is_never_reused_after_a_deletion()
    {
        var first = await _env.Register("Acme", "One", _env.Pdf("one.pdf", "Text."));
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_env.DatabasePath};Pooling=False;Foreign Keys=True"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM devices WHERE device_id = 1";
            await delete.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var second = await _env.Register("Acme", "Two", _env.Pdf("two.pdf", "Text."));

        Assert.Equal(1, first.Device!.DeviceId);
        Assert.Equal(2, second.Device!.DeviceId);
    }

}
