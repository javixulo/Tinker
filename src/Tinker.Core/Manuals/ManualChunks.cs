using Tinker.Core.Storage;
using Microsoft.Extensions.AI;

namespace Tinker.Core.Manuals;

/// <summary>Turns the pages of manuals into chunks ready for the index. Shared by <c>register_device</c> and <c>update_device</c>.</summary>
public static class ManualChunks
{
    /// <summary>Chunks and embeds the text of every manual. Nothing is written. <paramref name="onNoText"/> is told which manual has no text.</summary>
    public static async Task<List<IndexedChunk>> BuildAsync(
        IEnumerable<(string Path, IReadOnlyList<string> Pages)> manuals,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        Action<string?> onNoText,
        CancellationToken cancellationToken)
    {
        var chunks = new List<IndexedChunk>();
        foreach (var (manual, pages) in manuals)
        {
            var texts = new List<(int Page, string Text)>();
            for (var page = 1; page <= pages.Count; page++)
            {
                // Chunks never cross a page.
                texts.AddRange(TextChunker.Split(pages[page - 1]).Select(text => (page, text)));
            }

            if (texts.Count == 0)
            {
                onNoText(manual);
                continue;
            }

            var vectors = await embeddings.GenerateAsync(texts.Select(t => t.Text), cancellationToken: cancellationToken);
            if (vectors.Count != texts.Count)
            {
                throw new InvalidOperationException($"The embedding generator returned {vectors.Count} vectors for {texts.Count} chunks.");
            }

            for (var i = 0; i < texts.Count; i++)
            {
                chunks.Add(new IndexedChunk(manual, texts[i].Page, i, texts[i].Text, vectors[i].Vector.ToArray()));
            }
        }

        return chunks;
    }
}
