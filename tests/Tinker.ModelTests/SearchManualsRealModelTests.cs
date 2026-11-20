using Tinker.Core.Embeddings;
using Tinker.Core.Models;
using Tinker.IntegrationTests;
using Xunit;

namespace Tinker.ModelTests;

/// <summary>
/// UC-08 with the real embedding model: the search is by meaning, not only by shared words, and the query prefix of the
/// model is applied. Skipped when the model files are not there (see <see cref="BgeEmbeddingGeneratorTests"/>).
/// </summary>
public sealed class SearchManualsRealModelTests : IDisposable
{
    private readonly BgeEmbeddingGenerator _generator;
    private readonly TestEnvironment _env;

    public SearchManualsRealModelTests()
    {
        var path = BgeEmbeddingGeneratorTests.ModelPath();
        Assert.SkipUnless(path is not null, "The bge-small-en-v1.5 files are not in models/bge-small-en-v1.5.");
        _generator = new BgeEmbeddingGenerator(path!);
        _env = new TestEnvironment(embeddings: _generator, queryPrefix: BgeEmbeddingGenerator.QueryPrefix);
    }

    public void Dispose()
    {
        _env.Dispose();
        _generator.Dispose();
    }

    [Theory]
    [Trait("Criteria", "UC-08.AC1")]
    [InlineData("How long does the guarantee last?", 3)]
    [InlineData("What do I do if the lamp does not turn on?", 1)]
    [InlineData("Can the device be controlled with my voice?", 2)]
    public async Task Finds_the_excerpt_that_answers_the_question_without_sharing_its_words(string query, int page)
    {
        var registered = await _env.Registrar.RegisterAsync(new Device
        {
            Brand = "Shelly",
            Model = "Dimmer 2",
            Manuals =
            [
                _env.Pdf(
                    "a.pdf",
                    "Troubleshooting. If the light bulb stays dark, check that the mains cable is connected and that the fuse has not blown.",
                    "Voice assistants. The dimmer works with Alexa and Google Home, so you can say a command out loud to change the brightness.",
                    "Warranty. The manufacturer covers defects for a period of two years from the day of purchase."),
            ],
        });
        Assert.True(registered.Ok);

        var result = await _env.Searcher.SearchAsync(registered.Device!.DeviceId, query, 3);

        Assert.True(result.Ok);
        Assert.Equal(page, result.Chunks[0].Page);
    }
}
