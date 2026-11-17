using System.Runtime.InteropServices;
using Microsoft.Extensions.AI;

namespace Tinker.Core.Storage;

/// <summary>
/// The initial manuals index (technical spec, section 7): chunks in the same SQLite file, vectors as a binary column.
/// Nothing but this class knows how a chunk is stored.
/// </summary>
public sealed class SqliteManualsIndex(SqliteDatabase database, IEmbeddingGenerator<string, Embedding<float>> embeddings) : IManualsIndex
{
    /// <summary>The model and dimensions the configured generator produces, as the index records them.</summary>
    public string ExpectedModel =>
        $"{embeddings.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId ?? "unknown"}/{embeddings.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelDimensions}";

    /// <summary>The model that built the index, or null while it is empty.</summary>
    public Task<string?> StoredModelAsync(CancellationToken cancellationToken = default) =>
        database.UseAsync(async (connection, transaction) =>
        {
            using var command = SqliteInventory.Command(connection, transaction, "SELECT value FROM index_meta WHERE key = 'embedding_model'");
            return (string?)await command.ExecuteScalarAsync(cancellationToken);
        });

    public Task AddAsync(long deviceId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default) =>
        database.UseAsync<object?>(async (connection, transaction) =>
        {
            if (chunks.Count == 0)
            {
                return null;
            }

            await CheckModelAsync(connection, transaction, chunks[0].Embedding.Length, cancellationToken);
            foreach (var chunk in chunks)
            {
                using var command = SqliteInventory.Command(connection, transaction,
                    """
                    INSERT OR REPLACE INTO chunks (chunk_id, device_id, manual, page, text, embedding)
                    VALUES ($id, $device_id, $manual, $page, $text, $embedding)
                    """);
                command.Parameters.AddWithValue("$id", $"{deviceId}:{chunk.Manual}:{chunk.Number}");
                command.Parameters.AddWithValue("$device_id", deviceId);
                command.Parameters.AddWithValue("$manual", chunk.Manual);
                command.Parameters.AddWithValue("$page", chunk.Page);
                command.Parameters.AddWithValue("$text", chunk.Text);
                command.Parameters.AddWithValue("$embedding", MemoryMarshal.AsBytes<float>(chunk.Embedding).ToArray());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return null;
        });

    public Task DeleteByDeviceAsync(long deviceId, CancellationToken cancellationToken = default) =>
        database.UseAsync<object?>(async (connection, transaction) =>
        {
            using var command = SqliteInventory.Command(connection, transaction, "DELETE FROM chunks WHERE device_id = $id");
            command.Parameters.AddWithValue("$id", deviceId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return null;
        });

    /// <summary>Vectors of two models cannot be compared: the index records the model that built it.</summary>
    private async Task CheckModelAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, int dimensions, CancellationToken cancellationToken)
    {
        var model = $"{embeddings.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId ?? "unknown"}/{dimensions}";
        using var insert = SqliteInventory.Command(connection, transaction, "INSERT OR IGNORE INTO index_meta (key, value) VALUES ('embedding_model', $model)");
        insert.Parameters.AddWithValue("$model", model);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        using var read = SqliteInventory.Command(connection, transaction, "SELECT value FROM index_meta WHERE key = 'embedding_model'");
        var stored = (string)(await read.ExecuteScalarAsync(cancellationToken))!;
        if (stored != model)
        {
            throw new InvalidOperationException($"The manuals index was built with the embedding model '{stored}', not '{model}'. The index must be rebuilt.");
        }
    }
}
