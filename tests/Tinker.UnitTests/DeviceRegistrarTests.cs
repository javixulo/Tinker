using Microsoft.Extensions.Logging;
using Tinker.Core;
using Tinker.Core.Manuals;
using Tinker.Core.Models;
using Tinker.Core.Registration;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>
/// UC-01 and UC-02 with the manuals folder in memory (<see cref="FakeManualFiles"/>). That PDFs are really read and that links
/// are followed are in the integration tests.
/// </summary>
public sealed class DeviceRegistrarTests
{
    private readonly FakeManualFiles _files = new();
    private readonly FakeInventory _inventory = new();
    private readonly FakeIndex _index = new();
    private readonly FakeTransactions _transactions = new();
    private readonly ListLogger<DeviceRegistrar> _log = new();
    private readonly DeviceRegistrar _registrar;

    public DeviceRegistrarTests() =>
        _registrar = new DeviceRegistrar(
            _files, _inventory, _index, _transactions, _files, Embedder.Hash, _log);

    private void AssertRejectedWithNothingWritten(RegistrationResult result)
    {
        Assert.False(result.Ok);
        Assert.Null(result.Device);
        Assert.Equal(0, _inventory.Inserts);
        Assert.Empty(_index.Added);
        Assert.Equal(0, _transactions.Committed);
    }

    [Fact]
    [Trait("Criteria", "UC-01.1")]
    public async Task Registers_with_brand_and_model_only_and_writes_inventory_and_index_in_one_transaction()
    {
        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "Dimmer 2" });

        Assert.True(result.Ok);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Device!.DeviceId);
        Assert.Equal(1, _transactions.Committed);
        Assert.Equal(1L, Assert.Single(_index.Added).DeviceId);
        Assert.True(_log.Has(LogLevel.Information, "Registration requested"));
        Assert.True(_log.Has(LogLevel.Information, "Registration completed"));
    }

    [Fact]
    [Trait("Criteria", "UC-01.1")]
    public async Task A_device_id_given_by_the_client_is_ignored()
    {
        _inventory.Seed(new Device { Brand = "Other", Model = "X" });

        var result = await _registrar.RegisterAsync(new Device { DeviceId = 77, Brand = "Shelly", Model = "Dimmer 2" });

        Assert.Equal(2, result.Device!.DeviceId);
    }

    [Fact]
    [Trait("Criteria", "UC-01.8")]
    public async Task Values_are_normalized()
    {
        var result = await _registrar.RegisterAsync(new Device
        {
            Brand = "philips hué",
            Model = " lct  015 ",
            DeviceType = "washing   machine",
            Color = "dark red",
            Location = " living room ",
            Invoice = "  INV-1 ",
            Urls = [" https://x/a ", "https://x/a", " "],
        });

        var device = result.Device!;
        Assert.Equal("Philips Hue", device.Brand);
        Assert.Equal("LCT 015", device.Model);
        Assert.Equal("Washing machine", device.DeviceType);
        Assert.Equal("Dark red", device.Color);
        Assert.Equal("Living room", device.Location);
        Assert.Equal("INV-1", device.Invoice);
        Assert.Equal(["https://x/a"], device.Urls);
    }

    [Fact]
    [Trait("Criteria", "UC-01.2")]
    public async Task Blank_optional_fields_are_stored_as_not_given()
    {
        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X", DeviceType = " ", Color = "", Location = "\t", Invoice = " " });

        var device = result.Device!;
        Assert.Null(device.DeviceType);
        Assert.Null(device.Color);
        Assert.Null(device.Location);
        Assert.Null(device.Invoice);
    }

    [Theory]
    [Trait("Criteria", "UC-01.9")]
    [InlineData(null, "active")]
    [InlineData("  ", "active")]
    [InlineData(" archived ", "archived")]
    public async Task Status_defaults_to_active(string? given, string expected)
    {
        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X", Status = given });

        Assert.Equal(expected, result.Device!.Status);
    }

    [Theory]
    [Trait("Criteria", "UC-02.4")]
    [InlineData(null, "X", "brand")]
    [InlineData("  ", "X", "brand")]
    [InlineData("Shelly", null, "model")]
    [InlineData("Shelly", " ", "model")]
    public async Task A_missing_brand_or_model_is_invalid_fields_that_names_it(string? brand, string? model, string field)
    {
        var result = await _registrar.RegisterAsync(new Device { Brand = brand, Model = model });

        AssertRejectedWithNothingWritten(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.InvalidFields, error.Code);
        Assert.Contains(field, error.Message);
        Assert.True(_log.Has(LogLevel.Warning, "Registration rejected [invalid_fields]"));
    }

    [Fact]
    [Trait("Criteria", "UC-02.4")]
    public async Task Rejects_no_device_at_all()
    {
        var result = await _registrar.RegisterAsync(null);

        AssertRejectedWithNothingWritten(result);
        Assert.Contains("brand, model", Assert.Single(result.Errors).Message);
    }

    [Fact]
    [Trait("Criteria", "UC-02.4")]
    public async Task A_brand_with_no_valid_character_left_is_rejected()
    {
        var result = await _registrar.RegisterAsync(new Device { Brand = "***", Model = "X" });

        AssertRejectedWithNothingWritten(result);
        Assert.Equal(Messages.BrandWithoutValidCharacters, Assert.Single(result.Errors).Message);
    }

    [Fact]
    [Trait("Criteria", "UC-02.4")]
    public async Task Rejects_an_unknown_status()
    {
        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X", Status = "sold" });

        AssertRejectedWithNothingWritten(result);
        Assert.Contains("'sold'", Assert.Single(result.Errors).Message);
    }

    [Theory]
    [Trait("Criteria", "UC-02.1")]
    [InlineData("Shelly", "dimmer 2")]
    [InlineData("  SHELLY ", " Dimmer   2 ")]
    [InlineData("shellý", "DIMMER 2")]
    [Trait("Criteria", "UC-02.2")]
    public async Task Rejects_a_device_already_registered_even_written_differently_and_returns_it(string brand, string model)
    {
        var existing = _inventory.Seed(new Device { Brand = "Shelly", Model = "DIMMER 2" });

        var result = await _registrar.RegisterAsync(new Device { Brand = brand, Model = model });

        AssertRejectedWithNothingWritten(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.DeviceExists, error.Code);
        Assert.Equal(existing.DeviceId, error.Device!.DeviceId);
    }

    [Fact]
    [Trait("Criteria", "UC-02.2")]
    public async Task Other_characters_still_make_a_different_device()
    {
        _inventory.Seed(new Device { Brand = "Shelly", Model = "DIMMER2" });

        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "DIMMER-2" });

        Assert.True(result.Ok);
    }

    [Theory]
    [Trait("Criteria", "UC-02.7")]
    [InlineData("/etc/passwd", "outside the manuals folder")]
    [InlineData("~/Documents/guide.pdf", "outside the manuals folder")]
    [InlineData("%2e%2e/secret.pdf", "outside the manuals folder")]
    [InlineData("a/../b.pdf", "`..`")]
    public async Task Rejects_paths_that_point_outside_the_manuals_folder(string manual, string reason)
    {
        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X", Manuals = [manual] });

        AssertRejectedWithNothingWritten(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.ManualNotAllowed, error.Code);
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-02.4,5,6")]
    public async Task Tells_every_reason_at_once()
    {
        _inventory.Seed(new Device { Brand = "Shelly", Model = "X" });

        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X", Status = "sold", Manuals = ["/etc/passwd", "../x.pdf"] });

        AssertRejectedWithNothingWritten(result);
        Assert.Equal(
            [ErrorCodes.InvalidFields, ErrorCodes.ManualNotAllowed, ErrorCodes.ManualNotAllowed, ErrorCodes.DeviceExists],
            result.Errors.Select(e => e.Code));
    }

    [Fact]
    [Trait("Criteria", "UC-02.1,6")]
    public async Task When_two_registrations_race_the_database_decides_and_the_loser_gets_device_exists()
    {
        _inventory.Blind = true;
        _inventory.RaceWinner = () => new Device { Brand = "Shelly", Model = "X" };

        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X" });

        AssertRejectedWithNothingWritten(result);
        Assert.Equal(ErrorCodes.DeviceExists, Assert.Single(result.Errors).Code);
        Assert.Equal(1, _transactions.RolledBack);
    }

    [Fact]
    [Trait("Criteria", "UC-02.11")]
    public async Task A_failure_while_indexing_rolls_back_and_is_a_failed_error_that_hides_the_cause()
    {
        _index.Fails = new InvalidOperationException("index /secret/path broken");

        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X" });

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.Failed, error.Code);
        Assert.DoesNotContain("secret", error.Message);
        Assert.Equal(1, _transactions.RolledBack);
        Assert.Equal(0, _transactions.Committed);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Error is not null);
    }

    [Fact]
    [Trait("Criteria", "UC-02.11")]
    public async Task A_database_that_cannot_be_read_is_a_failed_error()
    {
        _inventory.InsertFails = new InvalidOperationException("cannot open");

        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X" });

        Assert.Equal(ErrorCodes.Failed, Assert.Single(result.Errors).Code);
    }

    [Fact]
    [Trait("Criteria", "UC-01.3, UC-01.5, UC-01.6")]
    public async Task Indexes_every_manual_as_chunks_with_device_manual_and_page_in_the_same_transaction()
    {
        _files.WithFile("a.pdf", "Press the reset button for ten seconds.", "The warranty lasts two years.").WithFile("sub/b.pdf", "Beta manual text.");

        var result = await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Box", Manuals = ["a.pdf", "sub/b.pdf"] });

        Assert.True(result.Ok);
        Assert.Equal(["a.pdf", "sub/b.pdf"], result.Device!.Manuals);
        var (id, chunks) = Assert.Single(_index.Added);
        Assert.Equal(result.Device.DeviceId, id);
        Assert.Equal([("a.pdf", 1), ("a.pdf", 2), ("sub/b.pdf", 1)], chunks.Select(c => (c.Manual, c.Page)));
        Assert.Equal(1, _transactions.Committed);
    }

    [Fact]
    [Trait("Criteria", "UC-01.5")]
    public async Task A_page_with_text_is_split_in_chunks_that_never_cross_the_maximum()
    {
        _files.WithFile("long.pdf", string.Join(' ', Enumerable.Range(0, 400).Select(i => "word" + i)));

        await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Box", Manuals = ["long.pdf"] });

        var chunks = Assert.Single(_index.Added).Chunks;
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(1, c.Page));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= TextChunker.DefaultMax));
    }

    [Fact]
    [Trait("Criteria", "UC-01.10")]
    public async Task A_manual_repeated_is_stored_and_read_once()
    {
        _files.WithFile("shelly/a.pdf", "Alpha.");

        var result = await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Box", Manuals = ["shelly/a.pdf", "shelly/./a.pdf", " shelly/a.pdf "] });

        Assert.Equal(["shelly/a.pdf"], result.Device!.Manuals);
        Assert.Equal(["shelly/a.pdf"], _files.Read);
        Assert.Single(Assert.Single(_index.Added).Chunks);
    }

    [Fact]
    [Trait("Criteria", "UC-01.5")]
    public async Task A_manual_with_no_text_registers_without_error_produces_no_chunks_and_is_logged()
    {
        _files.WithFile("scanned.pdf", "", "  ");

        var result = await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Box", Manuals = ["scanned.pdf"] });

        Assert.True(result.Ok);
        Assert.Empty(Assert.Single(_index.Added).Chunks);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("scanned.pdf", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criteria", "UC-02.5")]
    public async Task Rejects_manuals_that_do_not_exist_and_lists_all_of_them()
    {
        _files.WithFile("exists.pdf", "Alpha.");

        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "Dimmer 2", Manuals = ["shelly/missing.pdf", "exists.pdf", "other-missing.pdf"] });

        AssertRejectedWithNothingWritten(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.ManualNotFound, error.Code);
        Assert.Contains("shelly/missing.pdf, other-missing.pdf", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("exists.pdf,", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.6")]
    public async Task Rejects_a_manual_that_belongs_to_another_device_and_returns_that_device()
    {
        _files.WithFile("shared.pdf", "Shared.").WithFile("own.pdf", "Own.");
        var owner = _inventory.Seed(new Device { Brand = "Acme", Model = "One", Manuals = ["shared.pdf"] });

        var alone = await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Two", Manuals = ["shared.pdf"] });
        var withOthers = await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Three", Manuals = ["own.pdf", "./shared.pdf"] });

        foreach (var result in new[] { alone, withOthers })
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal(ErrorCodes.ManualInUse, error.Code);
            Assert.Contains("shared.pdf", error.Message, StringComparison.Ordinal);
            Assert.Equal(owner.DeviceId, error.Device!.DeviceId);
        }

        Assert.Equal(0, _inventory.Inserts);
    }

    [Fact]
    [Trait("Criteria", "UC-02.12")]
    public async Task Manuals_that_cannot_be_read_are_listed_with_their_reason_and_nothing_is_saved()
    {
        _files.WithFile("ok.pdf", "Alpha.")
            .WithUnreadableFile("notes.txt", ManualUnreadableException.NotAPdf)
            .WithUnreadableFile("broken.pdf", ManualUnreadableException.Damaged);

        var result = await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Box", Manuals = ["ok.pdf", "notes.txt", "broken.pdf"] });

        AssertRejectedWithNothingWritten(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.ManualUnreadable, error.Code);
        Assert.Contains("notes.txt (it is not a PDF)", error.Message, StringComparison.Ordinal);
        Assert.Contains("broken.pdf (it is damaged)", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ok.pdf", error.Message, StringComparison.Ordinal);
        Assert.Contains("neither was the device", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.10")]
    public async Task One_invalid_manual_rejects_everything()
    {
        _files.WithFile("good.pdf", "Alpha.");

        var result = await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Box", Manuals = ["good.pdf", "../secret.pdf"] });

        AssertRejectedWithNothingWritten(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.ManualNotAllowed, error.Code);
        Assert.Contains("../secret.pdf", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.11")]
    public async Task A_manual_that_fails_to_be_read_is_a_failed_error_and_leaves_nothing()
    {
        _files.WithBrokenReader("a.pdf");

        var result = await _registrar.RegisterAsync(new Device { Brand = "Acme", Model = "Box", Manuals = ["a.pdf"] });

        AssertRejectedWithNothingWritten(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.Failed, error.Code);
        Assert.DoesNotContain("fire", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.4,5,6")]
    public async Task Tells_every_reason_at_once_including_the_files()
    {
        _files.WithFile("shared.pdf", "x");
        _inventory.Seed(new Device { Brand = "Shelly", Model = "X", Manuals = ["shared.pdf"] });

        var result = await _registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "X", Status = "sold", Manuals = ["missing.pdf", "shared.pdf"] });

        AssertRejectedWithNothingWritten(result);
        Assert.Equal(
            [ErrorCodes.InvalidFields, ErrorCodes.ManualNotFound, ErrorCodes.DeviceExists, ErrorCodes.ManualInUse],
            result.Errors.Select(e => e.Code));
    }
}
