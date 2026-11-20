using Microsoft.Extensions.Logging;
using Tinker.Core;
using Tinker.Core.Models;
using Tinker.Core.Searching;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>UC-08 · Search the manuals of a device, UC-09 · Searches that give no excerpts. The real search by device is in the integration tests.</summary>
public sealed class ManualSearcherTests
{
    private readonly FakeInventory _inventory = new();
    private readonly FakeIndex _index = new();
    private readonly ListLogger<ManualSearcher> _log = new();

    public ManualSearcherTests()
    {
        _inventory.Seed(new Device { Brand = "Shelly", Model = "DIMMER 2", Manuals = ["a.pdf"] });
        _inventory.Seed(new Device { Brand = "Shelly", Model = "NO MANUALS" });
        _index.Results = [new Chunk("Reset it", 1, "a.pdf", 3), new Chunk("Wire it", 1, "a.pdf", 1)];
    }

    private ManualSearcher Searcher(string prefix = "") => new(_inventory, _index, _log, prefix);

    [Fact]
    [Trait("Criteria", "UC-08.AC1")]
    public async Task Returns_the_excerpts_in_the_order_the_index_gives_them()
    {
        var result = await Searcher().SearchAsync(1, "how to reset", null);

        Assert.True(result.Ok);
        Assert.Equal(["Reset it", "Wire it"], result.Chunks.Select(c => c.Text));
        Assert.Null(result.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-08.AC1")]
    public async Task The_query_goes_to_the_index_trimmed_with_the_prefix_of_the_model_in_front()
    {
        await Searcher("Represent this: ").SearchAsync(1, "  how to reset  ", 3);

        Assert.Equal([(1L, "Represent this: how to reset", 3)], _index.Searches);
    }

    [Fact]
    [Trait("Criteria", "UC-08.AC2")]
    public async Task The_search_is_always_for_the_device_given()
    {
        await Searcher().SearchAsync(1, "reset", null);

        Assert.All(_index.Searches, s => Assert.Equal(1L, s.DeviceId));
    }

    [Theory]
    [Trait("Criteria", "UC-08.AC3")]
    [InlineData(null, 5)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    public async Task The_number_of_results_is_passed_to_the_index_and_the_default_is_five(int? asked, int passed)
    {
        await Searcher().SearchAsync(1, "reset", asked);

        Assert.Equal(passed, Assert.Single(_index.Searches).Count);
    }

    [Theory]
    [Trait("Criteria", "UC-09.AC2")]
    [InlineData(null, "reset", null, "device_id")]
    [InlineData(1L, "  ", null, "query")]
    [InlineData(1L, null, null, "query")]
    [InlineData(1L, "reset", 0, "n_results")]
    [InlineData(1L, "reset", -2, "n_results")]
    public async Task Invalid_arguments_are_invalid_fields_that_say_which_one_and_nothing_is_looked_up(long? id, string? query, int? n, string field)
    {
        var result = await Searcher().SearchAsync(id, query, n);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.InvalidFields, result.Error!.Code);
        Assert.Contains($"`{field}`", result.Error.Message);
        Assert.Empty(_index.Searches);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC2")]
    public async Task Every_invalid_argument_is_reported_at_once()
    {
        var result = await Searcher().SearchAsync(null, " ", 0);

        Assert.Contains("`device_id`", result.Error!.Message);
        Assert.Contains("`query`", result.Error.Message);
        Assert.Contains("`n_results`", result.Error.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC1")]
    public async Task A_device_that_does_not_exist_is_device_not_found()
    {
        var result = await Searcher().SearchAsync(99, "reset", null);

        Assert.Equal(ErrorCodes.DeviceNotFound, result.Error!.Code);
        Assert.Empty(result.Chunks);
        Assert.Empty(_index.Searches);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC1")]
    public async Task A_device_deleted_while_searching_is_device_not_found_and_not_no_text()
    {
        _index.Results = [];
        _index.OnSearch = () => _inventory.DeleteAsync(1).GetAwaiter().GetResult();

        var result = await Searcher().SearchAsync(1, "reset", null);

        Assert.Equal(ErrorCodes.DeviceNotFound, result.Error!.Code);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC4")]
    public async Task A_device_without_manuals_gives_an_empty_list_and_says_so_without_asking_the_index()
    {
        var result = await Searcher().SearchAsync(2, "reset", null);

        Assert.True(result.Ok);
        Assert.Empty(result.Chunks);
        Assert.Equal(Messages.NoManuals, result.Message);
        Assert.Empty(_index.Searches);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC3")]
    public async Task Manuals_without_text_give_an_empty_list_and_a_message_for_the_client()
    {
        _index.Results = [];

        var result = await Searcher().SearchAsync(1, "reset", null);

        Assert.True(result.Ok);
        Assert.Empty(result.Chunks);
        Assert.Equal(Messages.NoExtractableText, result.Message);
        Assert.True(_log.Has(LogLevel.Warning, "Search found no chunks for device 1"));
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC4")]
    public async Task A_failure_in_the_index_is_a_failed_error_that_hides_the_cause()
    {
        _index.Fails = new InvalidOperationException("index /secret/path broken");

        var result = await Searcher().SearchAsync(1, "reset", null);

        Assert.Equal(ErrorCodes.Failed, result.Error!.Code);
        Assert.DoesNotContain("secret", result.Error.Message);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Error is not null);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC4")]
    public async Task An_unreadable_inventory_is_a_failed_error()
    {
        _inventory.ListFails = new InvalidOperationException("cannot read");

        var result = await Searcher().SearchAsync(1, "reset", null);

        Assert.Equal(ErrorCodes.Failed, result.Error!.Code);
    }
}
