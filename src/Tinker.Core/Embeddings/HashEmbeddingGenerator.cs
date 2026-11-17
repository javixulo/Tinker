using System.Text;
using Microsoft.Extensions.AI;

namespace Tinker.Core.Embeddings;

/// <summary>
/// A deterministic embedding generator with no model: words are hashed into a fixed number of buckets (feature
/// hashing) and the vector is normalized. It matches by shared words, not by meaning. It is the default until the
/// embedding model is chosen (technical spec, section 9) and what the logic tests use. Any
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> can replace it.
/// </summary>
public sealed class HashEmbeddingGenerator(int dimensions = HashEmbeddingGenerator.DefaultDimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int DefaultDimensions = 256;

    private readonly EmbeddingGeneratorMetadata _metadata = new("tinker", null, $"tinker-hash-{dimensions}", dimensions);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            result.Add(new Embedding<float>(Embed(value)));
        }

        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata
        : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    private float[] Embed(string text)
    {
        var vector = new float[dimensions];
        foreach (var word in Words(text))
        {
            vector[(int)(Fnv1a(word) % (uint)dimensions)] += 1;
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    private static IEnumerable<string> Words(string text)
    {
        var word = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(char.ToLowerInvariant(c));
            }
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }
}
