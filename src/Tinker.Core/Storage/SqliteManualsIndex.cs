using System.Runtime.InteropServices;
using Microsoft.Extensions.AI;
using Tinker.Core.Models;

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

    public Task DeleteByManualAsync(long deviceId, string manual, CancellationToken cancellationToken = default) =>
        database.UseAsync<object?>(async (connection, transaction) =>
        {
            using var command = SqliteInventory.Command(connection, transaction, "DELETE FROM chunks WHERE device_id = $id AND manual = $manual");
            command.Parameters.AddWithValue("$id", deviceId);
            command.Parameters.AddWithValue("$manual", manual);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return null;
        });

    public async Task<IReadOnlyList<Chunk>> SearchAsync(long deviceId, string queryText, int count, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("search_index");
        // Embed first, outside any database access: the cosine similarity is computed in C# over the chunks of this device only.
        var generated = await embeddings.GenerateAsync([queryText], cancellationToken: cancellationToken);
        var query = generated[0].Vector.ToArray();
        var queryNorm = Norm(query);

        return await database.UseAsync<IReadOnlyList<Chunk>>(async (connection, transaction) =>
        {
            var scored = new List<(double Score, Chunk Chunk)>();
            using var command = SqliteInventory.Command(connection, transaction,
                "SELECT text, manual, page, embedding FROM chunks WHERE device_id = $id ORDER BY rowid");
            command.Parameters.AddWithValue("$id", deviceId);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var bytes = (byte[])reader["embedding"];
                if (bytes.Length != query.Length * sizeof(float))
                {
                    throw new InvalidOperationException("The manuals index has vectors of another size than the embedding model. The index must be rebuilt.");
                }

                var vector = new float[query.Length];
                Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
                scored.Add((Cosine(query, queryNorm, vector), new Chunk(reader.GetString(0), deviceId, reader.GetString(1), reader.GetInt32(2))));
            }

            // OrderByDescending is stable: equal scores keep the order of the manual, so the answer is deterministic.
            return scored.OrderByDescending(s => s.Score).Take(count).Select(s => s.Chunk).ToList();
        });
    }

    private static double Norm(float[] vector) => Math.Sqrt(vector.Sum(x => (double)x * x));

    private static double Cosine(float[] query, double queryNorm, float[] vector)
    {
        double dot = 0;
        for (var i = 0; i < query.Length; i++)
        {
            dot += (double)query[i] * vector[i];
        }

        var norm = queryNorm * Norm(vector);
        return norm == 0 ? 0 : dot / norm;
    }

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
