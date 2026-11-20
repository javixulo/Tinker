using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Xunit;

namespace Tinker.IntegrationTests;

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
    [Trait("Criteria", "UC-08.1")]
    public async Task A_search_does_not_change_anything()
    {
        var id = await Register("Shelly", "Dimmer 2", manuals: [("a.pdf", [Wifi])]);
        var before = _env.Snapshot();

        await Search(id, "wifi");

        Assert.Equal(before, _env.Snapshot());
    }

    // UC-09

    [Fact]
    [Trait("Criteria", "UC-09.1")]
    public async Task A_deleted_device_is_device_not_found_and_its_content_is_not_returned()
    {
        var id = await Register("Shelly", "Dimmer 2", manuals: [("a.pdf", [Wifi])]);
        await _env.Remover.DeleteAsync(new Device { DeviceId = id });

        var result = await Search(id, "wifi");

        AssertFails(result, "device_not_found");
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
