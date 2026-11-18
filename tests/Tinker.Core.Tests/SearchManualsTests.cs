using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.Core.Tests;

/// <summary>UC-08 · Search the manuals of a device, UC-09 · Search the manuals without a result.</summary>
public sealed class SearchManualsTests : IDisposable
{
    private const string Wifi = "To pair the dimmer with the wifi network hold the reset button for ten seconds until the led blinks.";
    private const string Filter = "Clean the washing machine filter every month and remove the lint from the drum.";
    private const string Warranty = "The warranty covers manufacturing defects for two years from the date of purchase.";

    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private async Task<long> Register(string brand, string model, TestEnvironment? env = null, params (string Path, string[] Pages)[] manuals)
    {
        env ??= _env;
        var result = await env.Registrar.RegisterAsync(new Device
        {
            Brand = brand,
            Model = model,
            Manuals = manuals.Select(m => env.Pdf(m.Path, m.Pages)).ToList(),
        });
        Assert.True(result.Ok, string.Join(" ", result.Errors.Select(e => e.Message)));
        return result.Device!.DeviceId!.Value;
    }

    private Task<SearchResult> Search(long? deviceId, string? query, int? count = null) => _env.Searcher.SearchAsync(deviceId, query, count);

    private static void AssertFails(SearchResult result, string code)
    {
        Assert.False(result.Ok);
        Assert.Empty(result.Chunks);
        Assert.Null(result.Message);
        Assert.Equal(code, result.Error!.Code);
    }

    // UC-08

    [Fact]
    [Trait("Criteria", "UC-08.1")]
    public async Task Returns_the_excerpts_that_best_match_with_where_each_one_comes_from()
    {
        var id = await Register("Shelly", "Dimmer 2", manuals:
        [
            ("shelly/guide.pdf", [Filter, Wifi]),
            ("shelly/safety.pdf", [Warranty]),
        ]);

        var result = await Search(id, "how do I pair the dimmer with the wifi network using the reset button");

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Null(result.Message);
        Assert.Equal(3, result.Chunks.Count);
        var best = result.Chunks[0];
        Assert.Contains("reset button", best.Text, StringComparison.Ordinal);
        Assert.Equal(id, best.DeviceId);
        Assert.Equal("shelly/guide.pdf", best.Manual);
        Assert.Equal(2, best.Page);
        Assert.All(result.Chunks, c => Assert.Equal(id, c.DeviceId));
        Assert.Equal(
            _env.Chunks(id).OrderBy(c => c.Manual).ThenBy(c => c.Page).Select(c => (c.Manual, c.Page)),
            result.Chunks.OrderBy(c => c.Manual).ThenBy(c => c.Page).Select(c => (c.Manual, c.Page)));
    }

    [Fact]
    [Trait("Criteria", "UC-08.1")]
    public async Task The_most_relevant_excerpt_comes_first_whatever_the_order_of_the_manual()
    {
        var id = await Register("Samsung", "WW90", manuals: [("a.pdf", [Warranty, Wifi, Filter])]);

        var byFilter = await Search(id, "washing machine filter lint drum");
        var byWarranty = await Search(id, "warranty manufacturing defects two years");

        Assert.Equal(3, byFilter.Chunks[0].Page);
        Assert.Equal(1, byWarranty.Chunks[0].Page);
    }

    [Fact]
    [Trait("Criteria", "UC-08.1")]
    public async Task The_query_goes_to_the_index_as_given_with_the_prefix_of_the_model_in_front()
    {
        var spy = new SearchSpyIndex(_env.Index);
        using var env = new TestEnvironment(wrapIndex: _ => spy, queryPrefix: "Query: ");
        var id = await Register("Shelly", "Dimmer 2", env, ("a.pdf", [Wifi]));

        var result = await env.Searcher.SearchAsync(id, "  reset button ", null);

        Assert.True(result.Ok);
        var search = Assert.Single(spy.Searches);
        Assert.Equal((id, "Query: reset button", 5), search);
    }

    [Fact]
    [Trait("Criteria", "UC-08.2")]
    public async Task Only_the_excerpts_of_that_device_come_back_even_if_another_device_matches_better()
    {
        var wanted = await Register("Shelly", "Dimmer 2", manuals: [("a.pdf", [Filter, Warranty])]);
        var other = await Register("Shelly", "RGBW2", manuals: [("b.pdf", [Wifi])]);

        // The text of the other device is the query itself: it would be first if the search did not filter.
        var result = await Search(wanted, Wifi);
        var otherResult = await Search(other, Wifi);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Chunks.Count);
        Assert.All(result.Chunks, c =>
        {
            Assert.Equal(wanted, c.DeviceId);
            Assert.Equal("a.pdf", c.Manual);
        });
        Assert.DoesNotContain(result.Chunks, c => c.Text.Contains("reset button", StringComparison.Ordinal));
        Assert.Equal(other, Assert.Single(otherResult.Chunks).DeviceId);
    }

    [Theory]
    [Trait("Criteria", "UC-08.3")]
    [InlineData(null, 5)]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    [InlineData(8, 7)]
    public async Task Returns_at_most_the_number_asked_for_or_the_default(int? asked, int expected)
    {
        var pages = Enumerable.Range(1, 7).Select(i => $"Section {i} describes the part number {i} of the heater and its settings.").ToArray();
        var id = await Register("Fibaro", "Double Smart", manuals: [("a.pdf", pages)]);
        Assert.Equal(7, _env.CountChunks(id));

        var result = await Search(id, "heater settings", asked);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Chunks.Count);
    }

    [Fact]
    [Trait("Criteria", "UC-08.3")]
    public async Task The_number_of_results_is_passed_to_the_index_and_the_default_is_five()
    {
        var spy = new SearchSpyIndex(_env.Index);
        using var env = new TestEnvironment(wrapIndex: _ => spy);
        var id = await Register("Shelly", "Dimmer 2", env, ("a.pdf", [Wifi]));

        await env.Searcher.SearchAsync(id, "reset", 3);
        await env.Searcher.SearchAsync(id, "reset", null);

        Assert.Equal([3, 5], spy.Searches.Select(s => s.Count));
    }

    [Fact]
    [Trait("Criteria", "UC-08.1")]
    public async Task A_search_does_not_change_anything_and_is_logged()
    {
        var id = await Register("Shelly", "Dimmer 2", manuals: [("a.pdf", [Wifi])]);
        var before = _env.Snapshot();

        await Search(id, "wifi");

        Assert.Equal(before, _env.Snapshot());
        Assert.Contains(_env.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.StartsWith("Search manuals", StringComparison.Ordinal));
        Assert.DoesNotContain(_env.Logs.Entries, e => e.Message.Contains("wifi", StringComparison.Ordinal) && e.Message.StartsWith("Search manuals", StringComparison.Ordinal));
    }

    // UC-09

    [Theory]
    [Trait("Criteria", "UC-09.1")]
    [InlineData(99L)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task A_device_that_does_not_exist_is_device_not_found(long id)
    {
        await Register("Shelly", "Dimmer 2", manuals: [("a.pdf", [Wifi])]);
        var warnings = _env.Logs.Entries.Count(e => e.Level == LogLevel.Warning);

        var result = await Search(id, "wifi");

        AssertFails(result, "device_not_found");
        Assert.Equal($"The device id {id} does not exist. Use find_devices to get a valid device_id.", result.Error!.Message);
        Assert.True(_env.Logs.Entries.Count(e => e.Level == LogLevel.Warning) > warnings);
    }

    [Fact]
    [Trait("Criteria", "UC-09.1")]
    public async Task A_deleted_device_is_device_not_found_and_its_content_is_not_returned()
    {
        var id = await Register("Shelly", "Dimmer 2", manuals: [("a.pdf", [Wifi])]);
        await _env.Remover.DeleteAsync(new Device { DeviceId = id });

        var result = await Search(id, "wifi");

        AssertFails(result, "device_not_found");
    }

    [Theory]
    [Trait("Criteria", "UC-09.2")]
    [InlineData(null, null, "`query` is blank: it must be the text to look for in the manuals.")]
    [InlineData("", null, "`query` is blank: it must be the text to look for in the manuals.")]
    [InlineData("  \t ", 3, "`query` is blank: it must be the text to look for in the manuals.")]
    [InlineData("wifi", 0, "`n_results` must be 1 or more: it is the number of excerpts to return.")]
    [InlineData("wifi", -4, "`n_results` must be 1 or more: it is the number of excerpts to return.")]
    [InlineData(" ", 0, "`query` is blank: it must be the text to look for in the manuals. `n_results` must be 1 or more: it is the number of excerpts to return.")]
    public async Task Invalid_arguments_are_invalid_fields_and_say_which_one(string? query, int? count, string message)
    {
        var id = await Register("Shelly", "Dimmer 2", manuals: [("a.pdf", [Wifi])]);
        var warnings = _env.Logs.Entries.Count(e => e.Level == LogLevel.Warning);

        var result = await Search(id, query, count);

        AssertFails(result, "invalid_fields");
        Assert.Equal(message, result.Error!.Message);
        Assert.True(_env.Logs.Entries.Count(e => e.Level == LogLevel.Warning) > warnings);
    }

    [Fact]
    [Trait("Criteria", "UC-09.2")]
    public async Task A_missing_device_id_is_invalid_fields()
    {
        await Register("Shelly", "Dimmer 2", manuals: [("a.pdf", [Wifi])]);

        var result = await Search(null, "wifi");

        AssertFails(result, "invalid_fields");
        Assert.Equal("`device_id` is missing: it is the id of the device whose manuals are searched. Get it with find_devices.", result.Error!.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-09.2")]
    public async Task Invalid_arguments_are_reported_before_the_device_is_looked_up()
    {
        var result = await Search(99, "  ");

        AssertFails(result, "invalid_fields");
    }

    [Fact]
    [Trait("Criteria", "UC-09.4")]
    public async Task A_device_without_manuals_gives_an_empty_list_and_says_so()
    {
        var registered = await _env.Registrar.RegisterAsync(new Device { Brand = "Shelly", Model = "Dimmer 2" });

        var result = await Search(registered.Device!.DeviceId, "wifi");

        Assert.True(result.Ok);
        Assert.Empty(result.Chunks);
        Assert.Equal(
            "This device has no manuals, so nothing can be answered. Tell the user, who can add manuals with update_device.",
            result.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-09.3")]
    public async Task Manuals_without_text_give_an_empty_list_and_a_message_for_the_client()
    {
        // A page with no text, like a scanned manual: it registers without error and produces no chunks.
        var id = await Register("Shelly", "Dimmer 2", manuals: [("scan.pdf", [""])]);
        Assert.Equal(0, _env.CountChunks(id));
        var warnings = _env.Logs.Entries.Count(e => e.Level == LogLevel.Warning);

        var result = await Search(id, "wifi");

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Empty(result.Chunks);
        Assert.Equal(
            "The manuals of this device have no extractable text (for example, they are scanned), so nothing can be answered from them. Tell the user.",
            result.Message);
        Assert.True(_env.Logs.Entries.Count(e => e.Level == LogLevel.Warning) > warnings);
    }

    [Fact]
    [Trait("Criteria", "UC-09.3")]
    public async Task A_device_without_text_does_not_get_the_content_of_another()
    {
        var scanned = await Register("Shelly", "Dimmer 2", manuals: [("scan.pdf", [""])]);
        await Register("Shelly", "RGBW2", manuals: [("b.pdf", [Wifi])]);

        var result = await Search(scanned, Wifi);

        Assert.True(result.Ok);
        Assert.Empty(result.Chunks);
        Assert.NotNull(result.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-09.4")]
    public async Task A_failure_in_the_index_is_a_failed_error_that_hides_the_cause()
    {
        using var env = new TestEnvironment(wrapIndex: index => new SearchSpyIndex(index, fail: true));
        var id = await Register("Shelly", "Dimmer 2", env, ("a.pdf", [Wifi]));

        var result = await env.Searcher.SearchAsync(id, "wifi", null);

        AssertFails(result, "failed");
        Assert.StartsWith("Searching the manuals failed because of an internal problem, not because of the request.", result.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("Tell the user instead of retrying", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("index broke", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(env.Logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    [Trait("Criteria", "UC-09.4")]
    public async Task An_unreadable_inventory_is_a_failed_error()
    {
        using var env = new TestEnvironment(wrapInventory: inventory => new UnreadableInventory(inventory));

        var result = await env.Searcher.SearchAsync(1, "wifi", null);

        AssertFails(result, "failed");
        Assert.Contains(env.Logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    [Trait("Criteria", "UC-09.4")]
    public async Task A_failure_of_the_embedding_model_is_a_failed_error()
    {
        // The index embeds the query: a generator that works to register and breaks when searching.
        var generator = new FlakyEmbeddings();
        using var env = new TestEnvironment(embeddings: generator);
        var id = await Register("Shelly", "Dimmer 2", env, ("a.pdf", [Wifi]));
        generator.Broken = true;

        var result = await env.Searcher.SearchAsync(id, "wifi", null);

        AssertFails(result, "failed");
        Assert.Contains(env.Logs.Entries, e => e.Level == LogLevel.Error);
    }
}
