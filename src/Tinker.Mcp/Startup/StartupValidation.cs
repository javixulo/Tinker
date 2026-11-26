using Microsoft.Extensions.AI;
using Tinker.Core.Embeddings;
using Tinker.Core.Storage;

namespace Tinker.Mcp.Startup;

/// <summary>
/// Checks on start that everything the server needs is in place (technical spec, section 4). Every problem is
/// reported with what is wrong and how to fix it.
/// </summary>
public static class StartupValidation
{
    public static IReadOnlyList<string> Validate(TinkerOptions options, SqliteDatabase database, IServiceProvider services)
    {
        var problems = new List<string>();
        var dataOk = CheckDataFolder(options.DataPath, problems);

        CheckManualsFolder(options.ManualsPath, problems);

        if (dataOk)
        {
            try
            {
                database.Initialize();
            }
            catch (Exception error)
            {
                problems.Add($"The database '{Path.GetFullPath(options.DatabasePath)}' cannot be opened: {error.Message}");
            }
        }

        if (options.EmbeddingBatchSize is < 1 or > 64)
        {
            problems.Add($"Tinker__EmbeddingBatchSize is {options.EmbeddingBatchSize}. Set it to a number from 1 to 64, or remove it to use {BgeEmbeddingGenerator.DefaultBatchSize}.");
        }

        if (options.EmbeddingThreads is < 0 or > 256)
        {
            problems.Add($"Tinker__EmbeddingThreads is {options.EmbeddingThreads}. Set it to a number from 1 to 256, or to 0 to let ONNX Runtime choose.");
        }

        if (problems.Count == 0)
        {
            CheckEmbeddingModel(options, services, problems);
        }

        return problems;
    }

    private static void CheckEmbeddingModel(TinkerOptions options, IServiceProvider services, List<string> problems)
    {
        if (options.EmbeddingModel is not ("hash" or BgeEmbeddingGenerator.ModelId))
        {
            problems.Add($"Unknown embedding model '{options.EmbeddingModel}'. Set Tinker__EmbeddingModel to '{BgeEmbeddingGenerator.ModelId}' or 'hash'.");
            return;
        }

        if (options.EmbeddingModel == BgeEmbeddingGenerator.ModelId)
        {
            var missing = BgeEmbeddingGenerator.RequiredFiles(options.EmbeddingModelPath).Where(f => !File.Exists(f)).ToList();
            if (missing.Count > 0)
            {
                problems.Add(
                    $"The embedding model files are missing: {string.Join(", ", missing.Select(Path.GetFullPath))}. " +
                    "Download model.onnx (from the onnx folder) and vocab.txt of BAAI/bge-small-en-v1.5 into that folder, or set Tinker__EmbeddingModelPath.");
                return;
            }
        }

        // Vectors of two models cannot be compared: an index built with another model must be rebuilt.
        var index = (SqliteManualsIndex)services.GetRequiredService<IManualsIndex>();
        var stored = index.StoredModelAsync().GetAwaiter().GetResult();
        if (stored is not null && stored != index.ExpectedModel)
        {
            problems.Add(
                $"The manuals index was built with the embedding model '{stored}', but '{index.ExpectedModel}' is configured. " +
                "Go back to the previous model, or delete the data folder and register the devices again.");
        }
    }

    private static void CheckManualsFolder(string path, List<string> problems)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
        {
            problems.Add($"The manuals folder '{full}' does not exist. Create it or set Tinker__ManualsPath.");
            return;
        }

        try
        {
            // Listing it needs read permission.
            _ = Directory.EnumerateFileSystemEntries(full).FirstOrDefault();
            // Looking anything up inside needs enter (execute) permission. The name does not exist, so this also works in an
            // empty folder: with the permission the answer is "not found", without it, "access denied".
            try
            {
                _ = File.GetAttributes(Path.Combine(full, $".enter-test-{Guid.NewGuid():N}"));
            }
            catch (FileNotFoundException)
            {
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            problems.Add(
                $"The manuals folder '{full}' cannot be read by the running user: {error.Message} " +
                "Give that user, or one of its groups, read and enter (r-x) permission on it. In a container only the groups set " +
                "in compose count (user and group_add), not the ones the user has on the host.");
        }
    }

    private static bool CheckDataFolder(string path, List<string> problems)
    {
        var full = Path.GetFullPath(path);
        try
        {
            Directory.CreateDirectory(full);
            // An actual write test: reading the permissions is not enough.
            var probe = Path.Combine(full, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception error)
        {
            problems.Add($"The data folder '{full}' is not writable by the running user: {error.Message} Fix its owner or permissions, or set Tinker__DataPath.");
            return false;
        }
    }
}
