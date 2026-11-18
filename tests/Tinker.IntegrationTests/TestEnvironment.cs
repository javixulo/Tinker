using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tinker.Core.Embeddings;
using Tinker.Core.Finding;
using Tinker.Core.Manuals;
using Tinker.Core.Models;
using Tinker.Core.Searching;
using Tinker.Core.Registration;
using Tinker.Core.Removal;
using Tinker.Core.Storage;
using Tinker.Core.Updating;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Tinker.IntegrationTests;

/// <summary>A temporary manuals folder, a database and a registrar wired with the real pieces. Failures are injected by wrapping a port.</summary>
public sealed class TestEnvironment : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "tinker-tests-" + Guid.NewGuid().ToString("N"));
    public string ManualsPath => Path.Combine(Root, "manuals");
    public string DatabasePath => Path.Combine(Root, "data", "inventory.db");
    public SqliteDatabase Database { get; }
    public IInventory Inventory { get; }
    public IManualsIndex Index { get; }
    public ListLoggerProvider Logs { get; } = new();

    public TestEnvironment(
        Func<IInventory, IInventory>? wrapInventory = null,
        Func<IManualsIndex, IManualsIndex>? wrapIndex = null,
        IManualTextExtractor? extractor = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddings = null,
        string? brokenDatabasePath = null,
        string queryPrefix = "")
    {
        Directory.CreateDirectory(ManualsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Database = new SqliteDatabase(brokenDatabasePath ?? DatabasePath);
        if (brokenDatabasePath is null)
        {
            Database.Initialize();
        }

        var generator = embeddings ?? new HashEmbeddingGenerator();
        Inventory = new SqliteInventory(Database);
        Index = new SqliteManualsIndex(Database, generator);
        var inventory = wrapInventory?.Invoke(Inventory) ?? Inventory;
        Finder = new DeviceFinder(inventory, LoggerFactory.Create(b => b.AddProvider(Logs)).CreateLogger<DeviceFinder>());
        var wrappedIndex = wrapIndex?.Invoke(Index) ?? Index;
        Remover = new DeviceRemover(inventory, wrappedIndex, Database, LoggerFactory.Create(b => b.AddProvider(Logs)).CreateLogger<DeviceRemover>());
        Searcher = new ManualSearcher(inventory, wrappedIndex, LoggerFactory.Create(b => b.AddProvider(Logs)).CreateLogger<ManualSearcher>(), queryPrefix);
        Registrar = new DeviceRegistrar(
            new ManualPathResolver(ManualsPath),
            inventory,
            wrappedIndex,
            Database,
            extractor ?? new PdfPigTextExtractor(),
            generator,
            LoggerFactory.Create(b => b.AddProvider(Logs)).CreateLogger<DeviceRegistrar>());
        Updater = new DeviceUpdater(
            new ManualPathResolver(ManualsPath),
            inventory,
            wrappedIndex,
            Database,
            extractor ?? new PdfPigTextExtractor(),
            generator,
            LoggerFactory.Create(b => b.AddProvider(Logs)).CreateLogger<DeviceUpdater>());
    }

    public DeviceRegistrar Registrar { get; }

    public DeviceUpdater Updater { get; }

    public DeviceFinder Finder { get; }

    public DeviceRemover Remover { get; }

    public ManualSearcher Searcher { get; }

    /// <summary>Devices hold lists, so records compare by reference: compare their JSON instead.</summary>
    public static string Json(Device device) => System.Text.Json.JsonSerializer.Serialize(device);

    /// <summary>Copies a public manual of <c>tests/fixtures/manuals</c> into the manuals folder, at the same relative path.</summary>
    public string Fixture(string relativePath, string? storeAs = null)
    {
        var target = Path.Combine(ManualsPath, storeAs ?? relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "manuals", relativePath), target);
        return storeAs ?? relativePath;
    }

    /// <summary>A small generated PDF with one text per page; an empty text gives a page with no text.</summary>
    public string Pdf(string relativePath, params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            if (text.Length > 0)
            {
                page.AddText(text, 12, new PdfPoint(25, 700), font);
            }
        }

        var target = Path.Combine(ManualsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, builder.Build());
        return relativePath;
    }

    public Task<RegistrationResult> Register(string brand, string model, params string[] manuals) =>
        Registrar.RegisterAsync(new Device { Brand = brand, Model = model, Manuals = manuals });

    public long CountDevices() => Scalar<long>("SELECT COUNT(*) FROM devices");

    public long CountRows(string table, long deviceId) => Scalar<long>($"SELECT COUNT(*) FROM {table} WHERE device_id = {deviceId}");

    public long CountChunks(long? deviceId = null) =>
        deviceId is null ? Scalar<long>("SELECT COUNT(*) FROM chunks") : Scalar<long>($"SELECT COUNT(*) FROM chunks WHERE device_id = {deviceId}");

    public List<(long DeviceId, string Manual, int Page, string Text)> Chunks(long? deviceId = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT device_id, manual, page, text FROM chunks" + (deviceId is null ? "" : $" WHERE device_id = {deviceId}") + " ORDER BY chunk_id";
        using var reader = command.ExecuteReader();
        var rows = new List<(long, string, int, string)>();
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        }

        return rows;
    }

    /// <summary>What is stored, to prove that a rejection left everything as it was.</summary>
    public string Snapshot() =>
        string.Join('|', Scalar<long>("SELECT COUNT(*) FROM devices"), Scalar<long>("SELECT COUNT(*) FROM device_manuals"),
            Scalar<long>("SELECT COUNT(*) FROM device_urls"), Scalar<long>("SELECT COUNT(*) FROM chunks"),
            Scalar<string>("SELECT IFNULL(GROUP_CONCAT(chunk_id), '') FROM (SELECT chunk_id FROM chunks ORDER BY chunk_id)"));

    private T Scalar<T>(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

public sealed class ListLoggerProvider : ILoggerProvider
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Logger(Entries);

    public void Dispose()
    {
    }

    private sealed class Logger(List<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add((logLevel, formatter(state, exception)));
    }
}

public sealed class ThrowingIndex(IManualsIndex inner) : IManualsIndex
{
    /// <summary>Writes the chunks and then fails, like an indexing that breaks half way.</summary>
    public async Task AddAsync(long deviceId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default)
    {
        await inner.AddAsync(deviceId, chunks, cancellationToken);
        throw new InvalidOperationException("The index broke.");
    }

    public Task DeleteByDeviceAsync(long deviceId, CancellationToken cancellationToken = default) => inner.DeleteByDeviceAsync(deviceId, cancellationToken);

    public Task DeleteByManualAsync(long deviceId, string manual, CancellationToken cancellationToken = default) => inner.DeleteByManualAsync(deviceId, manual, cancellationToken);

    public Task<IReadOnlyList<Chunk>> SearchAsync(long deviceId, string queryText, int count, CancellationToken cancellationToken = default) =>
        inner.SearchAsync(deviceId, queryText, count, cancellationToken);
}

/// <summary>
/// While <see cref="Blind"/> the checks find nothing, as if they had run just before another registration committed.
/// The insert then hits the UNIQUE constraints and sees the world as it is again.
/// </summary>
public sealed class RacyInventory(IInventory inner) : IInventory
{
    public bool Blind { get; set; }

    public Task<Device?> FindByBrandAndModelAsync(string brand, string model, CancellationToken cancellationToken = default) =>
        Blind ? Task.FromResult<Device?>(null) : inner.FindByBrandAndModelAsync(brand, model, cancellationToken);

    public Task<Device?> FindByManualAsync(string manual, CancellationToken cancellationToken = default) =>
        Blind ? Task.FromResult<Device?>(null) : inner.FindByManualAsync(manual, cancellationToken);

    public Task<IReadOnlyList<Device>> ListAllAsync(CancellationToken cancellationToken = default) => inner.ListAllAsync(cancellationToken);

    public async Task<Device> InsertAsync(Device device, CancellationToken cancellationToken = default)
    {
        Blind = false;
        return await inner.InsertAsync(device, cancellationToken);
    }

    public Task<Device?> UpdateAsync(Device device, CancellationToken cancellationToken = default)
    {
        Blind = false;
        return inner.UpdateAsync(device, cancellationToken);
    }

    public Task<bool> DeleteAsync(long deviceId, CancellationToken cancellationToken = default) => inner.DeleteAsync(deviceId, cancellationToken);
}

public sealed class ThrowingExtractor : IManualTextExtractor
{
    public Task<IReadOnlyList<string>> ExtractPagesAsync(string fullPath, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The manual could not be read.");
}

/// <summary>An inventory that cannot be read, like a database that fails under a lookup.</summary>
public sealed class UnreadableInventory(IInventory inner) : IInventory
{
    public Task<Device?> FindByBrandAndModelAsync(string brand, string model, CancellationToken cancellationToken = default) =>
        inner.FindByBrandAndModelAsync(brand, model, cancellationToken);

    public Task<Device?> FindByManualAsync(string manual, CancellationToken cancellationToken = default) =>
        inner.FindByManualAsync(manual, cancellationToken);

    public Task<IReadOnlyList<Device>> ListAllAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The database is unreadable.");

    public Task<Device> InsertAsync(Device device, CancellationToken cancellationToken = default) => inner.InsertAsync(device, cancellationToken);

    public Task<Device?> UpdateAsync(Device device, CancellationToken cancellationToken = default) => inner.UpdateAsync(device, cancellationToken);

    public Task<bool> DeleteAsync(long deviceId, CancellationToken cancellationToken = default) => inner.DeleteAsync(deviceId, cancellationToken);
}

/// <summary>An index that deletes the chunks and then fails, like a deletion that breaks half way.</summary>
public sealed class ThrowingDeleteIndex(IManualsIndex inner) : IManualsIndex
{
    public Task AddAsync(long deviceId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default) => inner.AddAsync(deviceId, chunks, cancellationToken);

    public async Task DeleteByDeviceAsync(long deviceId, CancellationToken cancellationToken = default)
    {
        await inner.DeleteByDeviceAsync(deviceId, cancellationToken);
        throw new InvalidOperationException("The index broke while deleting.");
    }

    public Task DeleteByManualAsync(long deviceId, string manual, CancellationToken cancellationToken = default) => inner.DeleteByManualAsync(deviceId, manual, cancellationToken);

    public Task<IReadOnlyList<Chunk>> SearchAsync(long deviceId, string queryText, int count, CancellationToken cancellationToken = default) =>
        inner.SearchAsync(deviceId, queryText, count, cancellationToken);
}

/// <summary>An index whose search breaks, and that remembers the query text it was given.</summary>
public sealed class SearchSpyIndex(IManualsIndex inner, bool fail = false) : IManualsIndex
{
    public List<(long DeviceId, string QueryText, int Count)> Searches { get; } = [];

    public Task AddAsync(long deviceId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default) => inner.AddAsync(deviceId, chunks, cancellationToken);

    public Task DeleteByDeviceAsync(long deviceId, CancellationToken cancellationToken = default) => inner.DeleteByDeviceAsync(deviceId, cancellationToken);

    public Task DeleteByManualAsync(long deviceId, string manual, CancellationToken cancellationToken = default) => inner.DeleteByManualAsync(deviceId, manual, cancellationToken);

    public Task<IReadOnlyList<Chunk>> SearchAsync(long deviceId, string queryText, int count, CancellationToken cancellationToken = default)
    {
        Searches.Add((deviceId, queryText, count));
        return fail ? throw new InvalidOperationException("The index broke while searching.") : inner.SearchAsync(deviceId, queryText, count, cancellationToken);
    }
}

/// <summary>A hash generator that can be broken after the registration, like a model that fails when it is used.</summary>
public sealed class FlakyEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HashEmbeddingGenerator _inner = new();

    public bool Broken { get; set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        Broken ? throw new InvalidOperationException("The model broke.") : _inner.GenerateAsync(values, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();
}
