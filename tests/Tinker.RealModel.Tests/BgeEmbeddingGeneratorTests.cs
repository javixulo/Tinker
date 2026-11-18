using Microsoft.Extensions.AI;
using Tinker.Core.Embeddings;
using Tinker.Core.Tests;
using Xunit;

namespace Tinker.RealModel.Tests;

/// <summary>
/// Slow tests with the real model (technical spec, section 8). They need the model files in <c>models/bge-small-en-v1.5</c>
/// at the repository root (or in <c>TINKER_TEST_MODEL_PATH</c>) and are skipped when they are not there. To run only these:
/// <c>dotnet test --filter-trait "Category=RealModel"</c>.
/// </summary>
[Trait("Category", "RealModel")]
public sealed class BgeEmbeddingGeneratorTests : IDisposable
{
    private readonly BgeEmbeddingGenerator _generator;

    public BgeEmbeddingGeneratorTests()
    {
        var path = ModelPath();
        Assert.SkipUnless(path is not null, "The bge-small-en-v1.5 files are not in models/bge-small-en-v1.5.");
        _generator = new BgeEmbeddingGenerator(path!);
    }

    public void Dispose() => _generator.Dispose();

    internal static string? ModelPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("TINKER_TEST_MODEL_PATH");
        if (fromEnvironment is not null)
        {
            return fromEnvironment;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "models", "bge-small-en-v1.5");
            if (BgeEmbeddingGenerator.RequiredFiles(candidate).All(File.Exists))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<float[]> Embed(string text) => (await _generator.GenerateAsync([text], cancellationToken: TestContext.Current.CancellationToken))[0].Vector.ToArray();

    private static float Cosine(float[] a, float[] b) => a.Zip(b, (x, y) => x * y).Sum();

    [Fact]
    public async Task Produces_normalized_vectors_of_384_dimensions()
    {
        var vector = await Embed("Press the reset button for ten seconds.");

        Assert.Equal(384, vector.Length);
        Assert.Equal(1f, MathF.Sqrt(vector.Sum(x => x * x)), 0.001f);
        var metadata = _generator.GetService<EmbeddingGeneratorMetadata>()!;
        Assert.Equal(("bge-small-en-v1.5", 384), (metadata.DefaultModelId, metadata.DefaultModelDimensions));
    }

    [Fact]
    public async Task Matches_by_meaning_not_only_by_words()
    {
        var reset = await Embed("To restore factory settings, hold the button on the side for 10 seconds until the LED blinks.");
        var warranty = await Embed("The manufacturer guarantees the product against defects for a period of two years from purchase.");
        var query = await Embed(BgeEmbeddingGenerator.QueryPrefix + "How do I reset the device?");

        Assert.True(Cosine(query, reset) > Cosine(query, warranty));
    }

    [Fact]
    public async Task Embeds_more_texts_than_one_batch_in_order_and_alone_or_together_alike()
    {
        var texts = Enumerable.Range(0, 20).Select(i => $"Chunk number {i} about wiring a dimmer to the mains.").ToList();

        var together = await _generator.GenerateAsync(texts, cancellationToken: TestContext.Current.CancellationToken);
        var alone = await Embed(texts[17]);

        Assert.Equal(20, together.Count);
        Assert.True(Cosine(together[17].Vector.ToArray(), alone) > 0.999f);
    }

    [Fact]
    public async Task A_text_longer_than_the_model_accepts_is_cut_not_rejected()
    {
        var vector = await Embed(string.Join(' ', Enumerable.Repeat("dimmer wiring", 1000)));

        Assert.Equal(384, vector.Length);
    }

    [Fact]
    public async Task Registers_a_real_manual_end_to_end_with_the_real_model()
    {
        using var env = new TestEnvironment(embeddings: _generator);
        var manual = env.Fixture("shelly/dimmer_2_guide.pdf");

        var result = await env.Register("Shelly", "Dimmer 2", manual);

        Assert.True(result.Ok);
        Assert.NotEmpty(env.Chunks(result.Device!.DeviceId));
    }
}
