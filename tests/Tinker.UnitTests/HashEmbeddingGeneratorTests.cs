using Tinker.Core.Embeddings;
using Xunit;

namespace Tinker.UnitTests;

/// <summary>The model-free generator the integration and contract tests rely on: it must be predictable.</summary>
public sealed class HashEmbeddingGeneratorTests
{
    private static async Task<float[]> Embed(HashEmbeddingGenerator generator, string text)
    {
        var result = await generator.GenerateAsync([text], cancellationToken: TestContext.Current.CancellationToken);
        return result.Single().Vector.ToArray();
    }

    private static float Dot(float[] a, float[] b) => a.Zip(b, (x, y) => x * y).Sum();

    [Fact]
    public async Task The_same_text_gives_the_same_vector_with_the_configured_dimensions()
    {
        var generator = new HashEmbeddingGenerator(64);

        var first = await Embed(generator, "Factory reset");
        var second = await Embed(generator, "Factory reset");

        Assert.Equal(64, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Vectors_are_normalized_and_case_and_punctuation_do_not_matter()
    {
        var generator = new HashEmbeddingGenerator();

        var a = await Embed(generator, "Factory, RESET!");
        var b = await Embed(generator, "factory reset");

        Assert.Equal(1f, Dot(a, a), 0.0001f);
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Texts_that_share_words_are_closer_than_texts_that_share_none()
    {
        var generator = new HashEmbeddingGenerator();
        var query = await Embed(generator, "reset the device to factory settings");

        var related = await Embed(generator, "factory settings reset");
        var unrelated = await Embed(generator, "wifi antenna placement guidelines");

        Assert.True(Dot(query, related) > Dot(query, unrelated));
    }

    [Fact]
    public async Task A_text_with_no_words_gives_a_zero_vector()
    {
        var vector = await Embed(new HashEmbeddingGenerator(), "  !!! ");

        Assert.All(vector, x => Assert.Equal(0f, x));
    }
}
