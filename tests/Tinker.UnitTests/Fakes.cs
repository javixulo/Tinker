using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tinker.Core;
using Tinker.Core.Embeddings;
using Tinker.Core.Manuals;
using Tinker.Core.Models;
using Tinker.Core.Storage;

namespace Tinker.UnitTests;

/// <summary>In-memory doubles of the ports, so the services are tested with no disk, database or network.</summary>
internal sealed class FakeInventory : IInventory
{
    private readonly List<Device> _devices = [];
    private long _lastId;

    public Exception? ListFails { get; set; }
    public Exception? InsertFails { get; set; }
    public Exception? UpdateFails { get; set; }

    /// <summary>The device other requests would have created between the checks and the write (a race).</summary>
    public Func<Device>? RaceWinner { get; set; }

    public bool Blind { get; set; }
    public int Inserts { get; private set; }
    public int Updates { get; private set; }
    public List<long> Deleted { get; } = [];
    public IReadOnlyList<Device> Devices => _devices;

    public Device Seed(Device device)
    {
        var stored = device with { DeviceId = ++_lastId, Manuals = device.Manuals ?? [], Urls = device.Urls ?? [] };
        _devices.Add(stored);
        return stored;
    }

    public Task<Device?> FindByBrandAndModelAsync(string brand, string model, CancellationToken cancellationToken = default) =>
        Task.FromResult(Blind ? null : _devices.FirstOrDefault(d => TextKeys.Fold(d.Brand) == TextKeys.Fold(brand) && TextKeys.Fold(d.Model) == TextKeys.Fold(model)));

    public Task<Device?> FindByManualAsync(string manual, CancellationToken cancellationToken = default) =>
        Task.FromResult(Blind ? null : _devices.FirstOrDefault(d => d.Manuals!.Contains(manual)));

    public Task<IReadOnlyList<Device>> ListAllAsync(CancellationToken cancellationToken = default) =>
        ListFails is null ? Task.FromResult<IReadOnlyList<Device>>(_devices.ToList()) : throw ListFails;

    public Task<Device> InsertAsync(Device device, CancellationToken cancellationToken = default)
    {
        if (InsertFails is not null)
        {
            throw InsertFails;
        }

        if (RaceWinner is not null)
        {
            Blind = false;
            Seed(RaceWinner());
            throw new InventoryConflictException(Conflict.DeviceExists, new InvalidOperationException("UNIQUE"));
        }

        Inserts++;
        return Task.FromResult(Seed(device));
    }

    public Task<Device?> UpdateAsync(Device device, CancellationToken cancellationToken = default)
    {
        if (UpdateFails is not null)
        {
            throw UpdateFails;
        }

        if (RaceWinner is not null)
        {
            Blind = false;
            Seed(RaceWinner());
            throw new InventoryConflictException(Conflict.DeviceExists, new InvalidOperationException("UNIQUE"));
        }

        var index = _devices.FindIndex(d => d.DeviceId == device.DeviceId);
        if (index < 0)
        {
            return Task.FromResult<Device?>(null);
        }

        Updates++;
        _devices[index] = device;
        return Task.FromResult<Device?>(device);
    }

    public Task<bool> DeleteAsync(long deviceId, CancellationToken cancellationToken = default)
    {
        Deleted.Add(deviceId);
        return Task.FromResult(_devices.RemoveAll(d => d.DeviceId == deviceId) > 0);
    }
}

internal sealed class FakeIndex : IManualsIndex
{
    public List<(long DeviceId, IReadOnlyList<IndexedChunk> Chunks)> Added { get; } = [];
    public List<long> DeletedByDevice { get; } = [];
    public List<(long DeviceId, string Manual)> DeletedByManual { get; } = [];
    public List<(long DeviceId, string QueryText, int Count)> Searches { get; } = [];
    public List<Chunk> Results { get; set; } = [];
    public Exception? Fails { get; set; }

    /// <summary>Called when a search runs, to change the world as another request would have.</summary>
    public Action? OnSearch { get; set; }

    public Task AddAsync(long deviceId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default)
    {
        Added.Add((deviceId, chunks));
        return Fails is null ? Task.CompletedTask : throw Fails;
    }

    public Task DeleteByDeviceAsync(long deviceId, CancellationToken cancellationToken = default)
    {
        DeletedByDevice.Add(deviceId);
        return Fails is null ? Task.CompletedTask : throw Fails;
    }

    public Task DeleteByManualAsync(long deviceId, string manual, CancellationToken cancellationToken = default)
    {
        DeletedByManual.Add((deviceId, manual));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Chunk>> SearchAsync(long deviceId, string queryText, int count, CancellationToken cancellationToken = default)
    {
        Searches.Add((deviceId, queryText, count));
        OnSearch?.Invoke();
        return Fails is null ? Task.FromResult<IReadOnlyList<Chunk>>(Results.ToList()) : throw Fails;
    }
}

/// <summary>Runs the work as the real one does and records whether it was committed or rolled back.</summary>
internal sealed class FakeTransactions : ITransactionRunner
{
    public int Committed { get; private set; }
    public int RolledBack { get; private set; }
    public bool Inside { get; private set; }

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        Inside = true;
        try
        {
            var result = await work();
            Committed++;
            return result;
        }
        catch
        {
            RolledBack++;
            throw;
        }
        finally
        {
            Inside = false;
        }
    }
}

/// <summary>
/// The manuals folder in memory. It judges the text of a path with simple rules (the real ones are tested in
/// <c>ManualPathResolverTests</c>) and knows which files exist and what the extractor finds in them.
/// </summary>
internal sealed class FakeManualFiles : IManualFiles, IManualTextExtractor
{
    private readonly Dictionary<string, object> _files = new(StringComparer.Ordinal);

    /// <summary>The files read, by relative path, in the order they were read.</summary>
    public List<string> Read { get; } = [];

    public FakeManualFiles WithFile(string relativePath, params string[] pages)
    {
        _files[relativePath] = pages;
        return this;
    }

    public FakeManualFiles WithUnreadableFile(string relativePath, string reason)
    {
        _files[relativePath] = new ManualUnreadableException(reason);
        return this;
    }

    public FakeManualFiles WithBrokenReader(string relativePath)
    {
        _files[relativePath] = new InvalidOperationException("the disk is on fire");
        return this;
    }

    public void Delete(string relativePath) => _files.Remove(relativePath);

    public ManualPathResult Resolve(string manual)
    {
        if (manual.StartsWith('/') || manual.StartsWith('~') || manual.StartsWith('\\') || manual.Contains('%'))
        {
            return new ManualPathResult(ManualRejection.OutsideFolder, null, null);
        }

        var parts = manual.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Contains(".."))
        {
            return new ManualPathResult(ManualRejection.ContainsDotDot, null, null);
        }

        var relative = string.Join('/', parts.Where(p => p != "."));
        return new ManualPathResult(ManualRejection.None, relative, "/manuals/" + relative);
    }

    public bool Exists(ManualPathResult manual) => manual.Allowed && _files.ContainsKey(manual.RelativePath!);

    public Task<IReadOnlyList<string>> ExtractPagesAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        var relative = fullPath["/manuals/".Length..];
        Read.Add(relative);
        return _files[relative] switch
        {
            string[] pages => Task.FromResult<IReadOnlyList<string>>(pages),
            Exception error => throw error,
            _ => throw new InvalidOperationException(),
        };
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));

    public bool Has(LogLevel level, string startsWith) =>
        Entries.Any(e => e.Level == level && e.Message.StartsWith(startsWith, StringComparison.Ordinal));
}

internal static class Embedder
{
    public static IEmbeddingGenerator<string, Embedding<float>> Hash { get; } = new HashEmbeddingGenerator();
}
