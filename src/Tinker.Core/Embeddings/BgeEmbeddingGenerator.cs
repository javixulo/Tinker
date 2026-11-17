using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Tinker.Core.Embeddings;

/// <summary>
/// The real embedding model: BAAI/bge-small-en-v1.5 (English only, 384 dimensions) run locally with ONNX Runtime on the CPU.
/// To support manuals in other languages, replace this class (see technical spec, section 7, "Embedding model").
/// </summary>
/// <remarks>
/// Needs two files in <c>modelPath</c>: <c>model.onnx</c> and <c>vocab.txt</c>. Pooling is the CLS token, then L2 normalization,
/// as the model requires. What it embeds are passages: no prefix. A search query is embedded with <see cref="QueryPrefix"/> in
/// front (the model card recommends it for short queries looking for passages), which is the job of the code that searches.
/// </remarks>
public sealed class BgeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const string ModelId = "bge-small-en-v1.5";
    public const int Dimensions = 384;
    public const string QueryPrefix = "Represent this sentence for searching relevant passages: ";

    private static readonly string[] Files = ["model.onnx", "vocab.txt"];

    private const int MaxTokens = 512;
    private const int BatchSize = 16;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly EmbeddingGeneratorMetadata _metadata = new("tinker", null, ModelId, Dimensions);

    public BgeEmbeddingGenerator(string modelPath)
    {
        _session = new InferenceSession(Path.Combine(modelPath, "model.onnx"));
        _tokenizer = BertTokenizer.Create(Path.Combine(modelPath, "vocab.txt"), new BertOptions { LowerCaseBeforeTokenization = true });
    }

    /// <summary>The files the model needs, so a missing one can be reported before the first use.</summary>
    public static IEnumerable<string> RequiredFiles(string modelPath) =>
        Files.Select(f => Path.Combine(modelPath, f));

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var texts = values.ToList();
        var result = new GeneratedEmbeddings<Embedding<float>>();
        for (var start = 0; start < texts.Count; start += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var vector in EmbedBatch(texts.Skip(start).Take(BatchSize).ToList()))
            {
                result.Add(new Embedding<float>(vector));
            }
        }

        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata
        : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose() => _session.Dispose();

    private List<float[]> EmbedBatch(List<string> texts)
    {
        var encoded = texts.Select(Encode).ToList();
        var length = encoded.Max(ids => ids.Count);
        var shape = new[] { texts.Count, length };
        var inputIds = new DenseTensor<long>(shape);
        var attention = new DenseTensor<long>(shape);
        var tokenTypes = new DenseTensor<long>(shape);
        for (var row = 0; row < encoded.Count; row++)
        {
            for (var column = 0; column < encoded[row].Count; column++)
            {
                inputIds[row, column] = encoded[row][column];
                attention[row, column] = 1;
            }
        }

        using var outputs = _session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attention),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypes),
        ]);
        var hidden = outputs[0].AsTensor<float>();

        var vectors = new List<float[]>(texts.Count);
        for (var row = 0; row < texts.Count; row++)
        {
            var vector = new float[Dimensions];
            for (var i = 0; i < Dimensions; i++)
            {
                vector[i] = hidden[row, 0, i]; // CLS pooling
            }

            var norm = MathF.Sqrt(vector.Sum(x => x * x));
            for (var i = 0; i < Dimensions && norm > 0; i++)
            {
                vector[i] /= norm;
            }

            vectors.Add(vector);
        }

        return vectors;
    }

    /// <summary>Token ids with [CLS] first and [SEP] last, cut to the 512 the model accepts.</summary>
    private List<int> Encode(string text)
    {
        var ids = _tokenizer.EncodeToIds(text).ToList();
        if (ids.Count > MaxTokens)
        {
            var separator = ids[^1];
            ids = [.. ids.Take(MaxTokens - 1), separator];
        }

        return ids;
    }
}
