namespace Tinker.Mcp;

/// <summary>Configuration, from the <c>Tinker</c> section: <c>Tinker__DataPath</c> and <c>Tinker__ManualsPath</c> (technical spec, section 4).</summary>
public sealed class TinkerOptions
{
    public const string Section = "Tinker";

    /// <summary>Folder with the database. It must be writable and on a local disk.</summary>
    public string DataPath { get; set; } = "data";

    /// <summary>Folder with the user's manuals. It is never modified.</summary>
    public string ManualsPath { get; set; } = "manuals";

    /// <summary>
    /// The embedding model: <c>bge-small-en-v1.5</c> (default, English only) or <c>hash</c> (no model, matches by shared words;
    /// for tests and development). Changing it needs the manuals index to be rebuilt.
    /// </summary>
    public string EmbeddingModel { get; set; } = "bge-small-en-v1.5";

    /// <summary>Folder with <c>model.onnx</c> and <c>vocab.txt</c> of the embedding model.</summary>
    public string EmbeddingModelPath { get; set; } = "models/bge-small-en-v1.5";

    public string DatabasePath => Path.Combine(DataPath, "inventory.db");
}
