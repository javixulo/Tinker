using Tinker.Core.Manuals;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.Core.Tests;

/// <summary>UC-01 · Register a device. Each test names the acceptance criteria it covers.</summary>
public sealed class RegisterDeviceTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    [Trait("Criteria", "UC-01.1")]
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
    [Trait("Criteria", "UC-01.8")]
    public async Task Values_are_normalized()
    {
        var manual = _env.Fixture("shelly/dimmer_2_guide.pdf");

        var result = await _env.Registrar.RegisterAsync(new Device
        {
            Brand = "philips hué",
            Model = "qe55q60-a/b",
            Location = "salón",
            Color = "  light   grey ",
            Invoice = " inv-1 ",
            Manuals = [manual],
        });

        var device = result.Device!;
        Assert.Equal("Philips Hue", device.Brand);
        Assert.Equal("QE55Q60-A/B", device.Model);
        Assert.Equal("Salón", device.Location);
        Assert.Equal("Light grey", device.Color);
        Assert.Equal([manual], device.Manuals);
    }

    [Theory]
    [Trait("Criteria", "UC-01.8")]
    [InlineData("LG", "LG")]
    [InlineData("philips hué", "Philips Hue")]
    [InlineData("bang & olufsen", "Bang & Olufsen")]
    [InlineData("B.S.H.", "BSH")]
    [InlineData("  SAMSUNG   electronics ", "Samsung Electronics")]
    public void Brand_normalization(string given, string expected) => Assert.Equal(expected, DeviceNormalizer.Brand(given));

    [Fact]
    [Trait("Criteria", "UC-01.9")]
    public async Task Status_defaults_to_active()
    {
        var archived = await _env.Registrar.RegisterAsync(new Device { Brand = "A", Model = "1", Status = "archived" });
        var plain = await _env.Registrar.RegisterAsync(new Device { Brand = "A", Model = "2" });

        Assert.Equal("archived", archived.Device!.Status);
        Assert.Equal("active", plain.Device!.Status);
    }

    [Fact]
    [Trait("Criteria", "UC-01.10")]
    public async Task A_manual_repeated_is_stored_and_indexed_once()
    {
        var manual = _env.Pdf("shelly/a.pdf", "Alpha.");

        var result = await _env.Register("Acme", "Box", manual, "shelly/./a.pdf");

        Assert.Equal(["shelly/a.pdf"], result.Device!.Manuals);
        Assert.Equal(1, _env.CountChunks());
    }

    [Fact]
    [Trait("Criteria", "UC-01.1")]
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
    [Trait("Criteria", "UC-01.2")]
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
    [Trait("Criteria", "UC-01.2")]
    public async Task Blank_optional_fields_are_stored_as_not_given()
    {
        var manual = _env.Fixture("shelly/dimmer_2_guide.pdf");

        var result = await _env.Registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "Dimmer 2", Manuals = [manual], Color = "  ", Urls = [" "] });

        Assert.True(result.Ok);
        Assert.Null(result.Device!.Color);
        Assert.Empty(result.Device.Urls!);
    }

    [Fact]
    [Trait("Criteria", "UC-01.3")]
    public async Task Registers_with_several_manuals()
    {
        var first = _env.Fixture("shelly/dimmer_2_guide.pdf");
        var second = _env.Fixture("shelly/rgbw2_app_guide.pdf");

        var result = await _env.Register("Shelly", "Dimmer 2", first, second);

        Assert.True(result.Ok);
        Assert.Equal([first, second], result.Device!.Manuals);
    }

    [Fact]
    [Trait("Criteria", "UC-01.4")]
    public async Task Keeps_the_relative_path_of_a_manual_in_a_subfolder()
    {
        _env.Fixture("shelly/dimmer_2_guide.pdf");

        var result = await _env.Register("Shelly", "Dimmer 2", "shelly/dimmer_2_guide.pdf");

        Assert.Equal(["shelly/dimmer_2_guide.pdf"], result.Device!.Manuals);
    }

    [Fact]
    [Trait("Criteria", "UC-01.5")]
    public async Task Indexes_the_manual_as_chunks_with_device_manual_and_page()
    {
        var manual = _env.Pdf("guide.pdf", "Press the reset button for ten seconds.", "The warranty lasts two years.");

        var result = await _env.Register("Acme", "Box", manual);

        var id = result.Device!.DeviceId!.Value;
        var chunks = _env.Chunks();
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal((id, "guide.pdf"), (c.DeviceId, c.Manual)));
        Assert.Contains(chunks, c => c.Page == 1 && c.Text.Contains("reset button", StringComparison.Ordinal));
        Assert.Contains(chunks, c => c.Page == 2 && c.Text.Contains("warranty", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criteria", "UC-01.5")]
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
    [Trait("Criteria", "UC-01.5")]
    public async Task A_page_with_text_is_split_in_chunks_of_about_500_characters()
    {
        var text = string.Join(' ', Enumerable.Range(0, 400).Select(i => "word" + i));
        var manual = _env.Pdf("long.pdf", text);

        await _env.Register("Acme", "Box", manual);

        var chunks = _env.Chunks();
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(1, c.Page));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= TextChunker.DefaultMax));
    }

    [Fact]
    [Trait("Criteria", "UC-01.6")]
    public async Task Indexes_the_content_of_every_manual_with_the_device_id()
    {
        var first = _env.Pdf("a.pdf", "Alpha manual text.");
        var second = _env.Pdf("b.pdf", "Beta manual text.");

        var result = await _env.Register("Acme", "Box", first, second);

        var id = result.Device!.DeviceId!.Value;
        var chunks = _env.Chunks();
        Assert.All(chunks, c => Assert.Equal(id, c.DeviceId));
        Assert.Equal(["a.pdf", "b.pdf"], chunks.Select(c => c.Manual).Distinct().Order());
    }

    [Fact]
    [Trait("Criteria", "UC-01.7")]
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
    [Trait("Criteria", "UC-01.1")]
    public async Task A_device_id_given_by_the_client_is_ignored()
    {
        var manual = _env.Pdf("guide.pdf", "Text.");

        var result = await _env.Registrar.RegisterAsync(new Device { DeviceId = 99, Brand = "Acme", Model = "Box", Manuals = [manual] });

        Assert.Equal(1, result.Device!.DeviceId);
    }

    [Fact]
    [Trait("Criteria", "UC-01.1")]
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

    [Fact]
    [Trait("Criteria", "UC-01.5")]
    public async Task A_manual_with_no_text_registers_without_error_and_produces_no_chunks()
    {
        var manual = _env.Pdf("scanned.pdf", "");

        var result = await _env.Register("Acme", "Box", manual);

        Assert.True(result.Ok);
        Assert.Equal(0, _env.CountChunks());
        Assert.Contains(_env.Logs.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("scanned.pdf", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criteria", "UC-01.1")]
    public async Task Logs_the_registration_requested_and_completed()
    {
        var manual = _env.Pdf("guide.pdf", "Text.");

        await _env.Register("Acme", "Box", manual);

        var info = _env.Logs.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information).Select(e => e.Message).ToList();
        Assert.Contains(info, m => m.StartsWith("Registration requested", StringComparison.Ordinal));
        Assert.Contains(info, m => m.StartsWith("Registration completed", StringComparison.Ordinal));
    }
}
