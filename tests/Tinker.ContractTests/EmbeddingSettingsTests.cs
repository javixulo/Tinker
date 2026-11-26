using Microsoft.Extensions.DependencyInjection;
using Tinker.Core.Storage;
using Tinker.Mcp;
using Tinker.Mcp.Startup;
using Xunit;

namespace Tinker.ContractTests;

/// <summary>The settings of the embedding model the user can tune (technical spec, section 4).</summary>
public sealed class EmbeddingSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tinker-mcp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void By_default_the_batch_is_four_and_the_runtime_chooses_the_threads()
    {
        var options = new TinkerOptions();

        Assert.Equal((4, 0), (options.EmbeddingBatchSize, options.EmbeddingThreads));
    }

    // Catches: a typo in the compose variables that breaks the first registration instead of stopping the start with a reason.
    [Theory]
    [InlineData(0, 0, "Tinker__EmbeddingBatchSize")]
    [InlineData(65, 0, "Tinker__EmbeddingBatchSize")]
    [InlineData(4, -1, "Tinker__EmbeddingThreads")]
    [InlineData(4, 257, "Tinker__EmbeddingThreads")]
    public void A_value_out_of_range_stops_the_start_and_names_the_variable(int batchSize, int threads, string variable)
    {
        Directory.CreateDirectory(Path.Combine(_root, "manuals"));
        var options = new TinkerOptions
        {
            DataPath = Path.Combine(_root, "data"),
            ManualsPath = Path.Combine(_root, "manuals"),
            // The folder has no model: the settings must be reported before anything tries to load it.
            EmbeddingModelPath = Path.Combine(_root, "no-model"),
            EmbeddingBatchSize = batchSize,
            EmbeddingThreads = threads,
        };
        using var services = new ServiceCollection().BuildServiceProvider();

        var problems = StartupValidation.Validate(options, new SqliteDatabase(options.DatabasePath), services);

        Assert.Contains(problems, problem => problem.Contains(variable, StringComparison.Ordinal));
    }
}
