using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.IntegrationTests;

/// <summary>UC-02 · Reject an invalid registration. A rejection changes nothing and is logged as a warning.</summary>
public sealed class RejectRegistrationTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private async Task<RegistrationResult> Rejected(Func<Task<RegistrationResult>> register)
    {
        var before = _env.Snapshot();
        var warnings = _env.Logs.Entries.Count(e => e.Level == LogLevel.Warning);

        var result = await register();

        Assert.False(result.Ok);
        Assert.Null(result.Device);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(before, _env.Snapshot());
        Assert.True(_env.Logs.Entries.Count(e => e.Level == LogLevel.Warning) > warnings, "a warning with the reason is logged");
        return result;
    }

    private async Task<Device> Registered(string brand, string model, string manual)
    {
        var result = await _env.Register(brand, model, manual);
        Assert.True(result.Ok);
        return result.Device!;
    }

    [Fact]
    [Trait("Criteria", "UC-02.1")]
    public async Task Rejects_a_device_already_registered_even_with_another_manual()
    {
        var existing = await Registered("Samsung", "QE55Q60", _env.Pdf("a.pdf", "Alpha."));
        var other = _env.Pdf("b.pdf", "Beta.");

        var result = await Rejected(() => _env.Register("Samsung", "QE55Q60", other));

        var error = Assert.Single(result.Errors);
        Assert.Equal("device_exists", error.Code);
        Assert.Equal(TestEnvironment.Json(existing), TestEnvironment.Json(error.Device!));
        Assert.Contains("Samsung QE55Q60", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.2")]
    public async Task Rejects_the_same_device_written_differently()
    {
        var existing = await Registered("Samsung", "QE55Q60", _env.Pdf("a.pdf", "Alpha."));
        var other = _env.Pdf("b.pdf", "Beta.");

        var result = await Rejected(() => _env.Register("samsung", " qe55q60 ", other));

        var error = Assert.Single(result.Errors);
        Assert.Equal("device_exists", error.Code);
        Assert.Equal(TestEnvironment.Json(existing), TestEnvironment.Json(error.Device!));
    }

    [Fact]
    [Trait("Criteria", "UC-02.2")]
    public async Task Spaces_between_words_do_not_make_a_different_device()
    {
        await Registered("Fibaro", "Double  Smart", _env.Pdf("a.pdf", "Alpha."));

        var result = await Rejected(() => _env.Register("FIBARO", "double smart", _env.Pdf("b.pdf", "Beta.")));

        Assert.Equal("device_exists", Assert.Single(result.Errors).Code);
    }

    [Theory]
    [Trait("Criteria", "UC-02.1,2")]
    [InlineData("Philips", "Hué", "Philips", "hue")]
    [InlineData("Philips", "Hue", "PHILIPS", "HUÉ")]
    [InlineData("Philips Hué", "Bridge", "philips  hue", " BRIDGE ")]
    [InlineData("Müller", "Küche 2", "muller", "kuche 2")]
    public async Task Accents_do_not_make_a_different_device(string brand, string model, string otherBrand, string otherModel)
    {
        var existing = await Registered(brand, model, _env.Pdf("a.pdf", "Alpha."));
        var other = _env.Pdf("b.pdf", "Beta.");

        var result = await Rejected(() => _env.Register(otherBrand, otherModel, other));

        var error = Assert.Single(result.Errors);
        Assert.Equal("device_exists", error.Code);
        Assert.Equal(TestEnvironment.Json(existing), TestEnvironment.Json(error.Device!));
    }

    [Fact]
    [Trait("Criteria", "UC-02.1,2")]
    public async Task Accents_are_not_the_only_thing_ignored_other_characters_still_count()
    {
        await Registered("Philips", "Hue-2", _env.Pdf("a.pdf", "Alpha."));

        var result = await _env.Register("Philips", "Hue 2", _env.Pdf("b.pdf", "Beta."));

        Assert.True(result.Ok);
    }

    [Fact]
    [Trait("Criteria", "UC-02.3")]
    public async Task Registering_twice_is_rejected_the_second_time()
    {
        var manual = _env.Pdf("a.pdf", "Alpha.");
        await Registered("Samsung", "QE55Q60", manual);

        var result = await Rejected(() => _env.Register("Samsung", "QE55Q60", manual));

        Assert.Contains(result.Errors, e => e.Code == "device_exists");
        Assert.Equal(1, _env.CountDevices());
    }

    [Theory]
    [Trait("Criteria", "UC-02.4")]
    [InlineData("", "Model", true, "brand")]
    [InlineData("Brand", "   ", true, "model")]
    [InlineData(null, null, false, "brand, model")]
    public async Task Rejects_missing_required_fields_and_lists_them(string? brand, string? model, bool withManual, string expected)
    {
        var manuals = withManual ? new[] { _env.Pdf("a.pdf", "Alpha.") } : [];

        var result = await Rejected(() => _env.Registrar.RegisterAsync(new Device { Brand = brand, Model = model, Manuals = manuals }));

        var error = Assert.Single(result.Errors);
        Assert.Equal("invalid_fields", error.Code);
        Assert.Contains($"required fields: {expected}.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.4")]
    public async Task A_brand_with_no_valid_character_left_is_rejected()
    {
        var result = await Rejected(() => _env.Registrar.RegisterAsync(new Device { Brand = "...", Model = "M" }));

        var error = Assert.Single(result.Errors);
        Assert.Equal("invalid_fields", error.Code);
        Assert.Contains("no valid characters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.12")]
    public async Task Manuals_that_cannot_be_read_are_listed_with_their_reason_and_nothing_is_saved()
    {
        var readable = _env.Pdf("ok.pdf", "Alpha.");
        File.WriteAllText(Path.Combine(_env.ManualsPath, "notes.txt"), "just text");
        File.WriteAllText(Path.Combine(_env.ManualsPath, "broken.pdf"), "%PDF-1.4\nthis is not a real body");

        var result = await Rejected(() => _env.Register("Acme", "Box", readable, "notes.txt", "broken.pdf"));

        var error = Assert.Single(result.Errors);
        Assert.Equal("manual_unreadable", error.Code);
        Assert.Contains("notes.txt (it is not a PDF)", error.Message, StringComparison.Ordinal);
        Assert.Contains("broken.pdf (it is damaged)", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ok.pdf", error.Message, StringComparison.Ordinal);
        Assert.Contains("neither was the device", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _env.CountChunks());
    }

    [Fact]
    [Trait("Criteria", "UC-02.4")]
    public async Task Rejects_no_device_at_all()
    {
        var result = await Rejected(() => _env.Registrar.RegisterAsync(null));

        Assert.Equal("invalid_fields", Assert.Single(result.Errors).Code);
    }

    [Fact]
    [Trait("Criteria", "UC-02.4")]
    public async Task Rejects_an_unknown_status()
    {
        var manual = _env.Pdf("a.pdf", "Alpha.");

        var result = await Rejected(() => _env.Registrar.RegisterAsync(new Device { Brand = "B", Model = "M", Manuals = [manual], Status = "broken" }));

        var error = Assert.Single(result.Errors);
        Assert.Equal("invalid_fields", error.Code);
        Assert.Contains("active, inactive, archived", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.5")]
    public async Task Rejects_manuals_that_do_not_exist_and_lists_all_of_them()
    {
        _env.Pdf("exists.pdf", "Alpha.");

        var result = await Rejected(() => _env.Register("Shelly", "Dimmer 2", "shelly/missing.pdf", "exists.pdf", "other-missing.pdf"));

        var error = Assert.Single(result.Errors);
        Assert.Equal("manual_not_found", error.Code);
        Assert.Contains("shelly/missing.pdf", error.Message, StringComparison.Ordinal);
        Assert.Contains("other-missing.pdf", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("exists.pdf", error.Message.Replace("missing.pdf", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.6")]
    public async Task Rejects_a_manual_that_belongs_to_another_device()
    {
        var owner = await Registered("Acme", "One", _env.Pdf("shared.pdf", "Shared."));
        var own = _env.Pdf("own.pdf", "Own.");

        var alone = await Rejected(() => _env.Register("Acme", "Two", "shared.pdf"));
        var withOthers = await Rejected(() => _env.Register("Acme", "Three", own, "shared.pdf"));

        foreach (var result in new[] { alone, withOthers })
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal("manual_in_use", error.Code);
            Assert.Contains("shared.pdf", error.Message, StringComparison.Ordinal);
            Assert.Equal(TestEnvironment.Json(owner), TestEnvironment.Json(error.Device!));
        }
    }

    [Fact]
    [Trait("Criteria", "UC-02.6")]
    public async Task The_same_file_written_two_ways_is_the_same_manual()
    {
        await Registered("Acme", "One", _env.Pdf("shelly/a.pdf", "Alpha."));

        var result = await Rejected(() => _env.Register("Acme", "Two", "shelly/./a.pdf"));

        Assert.Equal("manual_in_use", Assert.Single(result.Errors).Code);
    }

    [Fact]
    [Trait("Criteria", "UC-02.7")]
    public async Task Rejects_paths_that_point_outside_the_manuals_folder()
    {
        var outside = Path.Combine(_env.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.pdf"), "not read");
        File.CreateSymbolicLink(Path.Combine(_env.ManualsPath, "link.pdf"), Path.Combine(outside, "secret.pdf"));

        foreach (var manual in new[] { Path.Combine(outside, "secret.pdf"), "/etc/passwd", "link.pdf", "~/Documents/guide.pdf", "%2e%2e/secret.pdf", "C:\\secret.pdf", "\\secret.pdf" })
        {
            var result = await Rejected(() => _env.Register("Acme", "Box", manual));

            var error = Assert.Single(result.Errors);
            Assert.Equal("manual_not_allowed", error.Code);
            Assert.Contains("outside the manuals folder", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criteria", "UC-02.7")]
    public async Task Links_are_followed_in_every_component_of_the_path()
    {
        var outside = Path.Combine(_env.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.pdf"), "not read");
        File.CreateSymbolicLink(Path.Combine(_env.ManualsPath, "linkdir"), outside);
        File.CreateSymbolicLink(Path.Combine(_env.ManualsPath, "broken.pdf"), Path.Combine(_env.Root, "nowhere", "gone.pdf"));
        File.CreateSymbolicLink(Path.Combine(_env.ManualsPath, "chain-a"), "chain-b");
        File.CreateSymbolicLink(Path.Combine(_env.ManualsPath, "chain-b"), outside);

        foreach (var manual in new[] { "linkdir/secret.pdf", "broken.pdf", "chain-a/secret.pdf" })
        {
            var result = await Rejected(() => _env.Register("Acme", "Box", manual));

            Assert.Equal("manual_not_allowed", Assert.Single(result.Errors).Code);
        }
    }

    [Fact]
    [Trait("Criteria", "UC-02.7")]
    public async Task A_link_that_stays_inside_the_folder_is_allowed_and_stored_by_its_real_path()
    {
        _env.Pdf("shelly/guide.pdf", "Alpha.");
        File.CreateSymbolicLink(Path.Combine(_env.ManualsPath, "alias.pdf"), "shelly/guide.pdf");

        var result = await _env.Register("Acme", "Box", "alias.pdf");

        Assert.True(result.Ok);
        Assert.Equal(["shelly/guide.pdf"], result.Device!.Manuals);
    }

    [Fact]
    [Trait("Criteria", "UC-02.8")]
    public async Task Rejects_paths_with_dot_dot_even_when_they_would_end_up_inside()
    {
        _env.Pdf("shelly/dimmer_2_guide.pdf", "Alpha.");
        File.WriteAllText(Path.Combine(_env.Root, "secret.pdf"), "not read");

        foreach (var manual in new[] { "../secret.pdf", "shelly/../../secret.pdf", "shelly/../shelly/dimmer_2_guide.pdf", "shelly\\..\\x.pdf" })
        {
            var result = await Rejected(() => _env.Register("Acme", "Box", manual));

            var error = Assert.Single(result.Errors);
            Assert.Equal("manual_not_allowed", error.Code);
            Assert.Contains("cannot contain `..`", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criteria", "UC-02.9")]
    public async Task Does_not_reveal_whether_a_file_outside_the_folder_exists()
    {
        var outside = Path.Combine(_env.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "exists.pdf"), "secret content");
        var existing = Path.Combine(outside, "exists.pdf");
        var missing = Path.Combine(outside, "missing.pdf");

        var one = await Rejected(() => _env.Register("Acme", "Box", existing));
        var two = await Rejected(() => _env.Register("Acme", "Box", missing));

        var a = Assert.Single(one.Errors);
        var b = Assert.Single(two.Errors);
        Assert.Equal(a.Code, b.Code);
        Assert.Equal(a.Message.Replace(existing, "<path>", StringComparison.Ordinal), b.Message.Replace(missing, "<path>", StringComparison.Ordinal));
        Assert.DoesNotContain("secret content", a.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_env.ManualsPath, a.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.10")]
    public async Task One_invalid_manual_rolls_back_everything()
    {
        var good = _env.Pdf("good.pdf", "Alpha.");

        var result = await Rejected(() => _env.Register("Acme", "Box", good, "../secret.pdf"));

        var error = Assert.Single(result.Errors);
        Assert.Equal("manual_not_allowed", error.Code);
        Assert.Contains("../secret.pdf", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _env.CountDevices());
        Assert.Equal(0, _env.CountChunks());
    }

    [Fact]
    [Trait("Criteria", "UC-02.4,5,6")]
    public async Task Tells_every_reason_at_once()
    {
        var owner = await Registered("Acme", "One", _env.Pdf("shared.pdf", "Shared."));

        var result = await Rejected(() => _env.Register("Acme", "One", "shared.pdf", "missing.pdf", "../x.pdf", "/etc/passwd"));

        Assert.Equal(
            ["manual_not_allowed", "manual_not_allowed", "manual_not_found", "device_exists", "manual_in_use"],
            result.Errors.Select(e => e.Code));
        Assert.All(result.Errors.Where(e => e.Code is "device_exists" or "manual_in_use"), e => Assert.Equal(owner.DeviceId, e.Device!.DeviceId));
    }

    [Fact]
    [Trait("Criteria", "UC-02.11")]
    public async Task A_failure_after_the_device_is_written_rolls_back_everything()
    {
        using var env = new TestEnvironment(wrapIndex: index => new ThrowingIndex(index));
        var before = env.Snapshot();

        var result = await env.Register("Acme", "Box", env.Pdf("a.pdf", "Alpha."));

        AssertFailed(result);
        Assert.Equal(before, env.Snapshot());
        Assert.Equal(0, env.CountDevices());
        Assert.Equal(0, env.CountChunks());
        Assert.Contains(env.Logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    [Trait("Criteria", "UC-02.11")]
    public async Task A_manual_that_cannot_be_read_fails_and_leaves_nothing()
    {
        using var env = new TestEnvironment(extractor: new ThrowingExtractor());
        var before = env.Snapshot();

        var result = await env.Register("Acme", "Box", env.Pdf("a.pdf", "Alpha."));

        AssertFailed(result);
        Assert.Equal(before, env.Snapshot());
    }

    [Fact]
    [Trait("Criteria", "UC-02.11")]
    public async Task A_database_that_cannot_be_opened_fails()
    {
        using var env = new TestEnvironment(brokenDatabasePath: Path.Combine(Path.GetTempPath(), "tinker-no-such-folder-" + Guid.NewGuid().ToString("N"), "inventory.db"));

        var result = await env.Register("Acme", "Box", env.Pdf("a.pdf", "Alpha."));

        AssertFailed(result);
    }

    [Fact]
    [Trait("Criteria", "UC-02.1,6")]
    public async Task When_two_registrations_race_the_database_decides_and_the_loser_is_rolled_back()
    {
        RacyInventory racy = null!;
        using var env = new TestEnvironment(wrapInventory: inventory => racy = new RacyInventory(inventory));
        var winner = await env.Register("Acme", "Box", env.Pdf("a.pdf", "Alpha."));
        Assert.True(winner.Ok);
        var snapshot = env.Snapshot();
        var other = env.Pdf("b.pdf", "Beta.");

        // The checks pass for the loser, the UNIQUE constraints refuse.
        racy.Blind = true;
        var sameDevice = await env.Register("Acme", "Box", other);
        racy.Blind = true;
        var sameManual = await env.Register("Acme", "Other", "a.pdf");

        Assert.Equal("device_exists", Assert.Single(sameDevice.Errors).Code);
        Assert.Equal(winner.Device!.DeviceId, sameDevice.Errors[0].Device!.DeviceId);
        Assert.Equal("manual_in_use", Assert.Single(sameManual.Errors).Code);
        Assert.Equal(winner.Device.DeviceId, sameManual.Errors[0].Device!.DeviceId);
        Assert.Equal(snapshot, env.Snapshot());
    }

    private static DeviceError AssertFailed(RegistrationResult result)
    {
        Assert.False(result.Ok);
        Assert.Null(result.Device);
        var error = Assert.Single(result.Errors);
        Assert.Equal("failed", error.Code);
        Assert.Contains("not because of the request", error.Message, StringComparison.Ordinal);
        Assert.Contains("Tell the user instead of retrying", error.Message, StringComparison.Ordinal);
        return error;
    }
}
