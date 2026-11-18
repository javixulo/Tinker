using Tinker.Core.Models;

namespace Tinker.Core.Storage;

public enum Conflict
{
    DeviceExists,
    ManualInUse,
}

/// <summary>The database refused an insert because of a UNIQUE constraint: another registration got in first.</summary>
public sealed class InventoryConflictException(Conflict conflict, Exception inner)
    : Exception($"Inventory conflict: {conflict}", inner)
{
    public Conflict Conflict { get; } = conflict;
}

/// <summary>The inventory of devices. The source of truth.</summary>
public interface IInventory
{
    Task<Device?> FindByBrandAndModelAsync(string brand, string model, CancellationToken cancellationToken = default);

    /// <summary>The device that owns the manual (path relative to the manuals folder), if any.</summary>
    Task<Device?> FindByManualAsync(string manual, CancellationToken cancellationToken = default);

    /// <summary>Every registered device with its manuals and URLs, ordered by <c>device_id</c>. A home inventory is small, so matching is done in memory.</summary>
    Task<IReadOnlyList<Device>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the device with its manuals and URLs and returns it with its new <c>device_id</c>.</summary>
    /// <exception cref="InventoryConflictException">The brand and model, or a manual, are already registered.</exception>
    Task<Device> InsertAsync(Device device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every field of the device, with its manuals and URLs, keeping its <c>device_id</c>. Returns the device as stored,
    /// or null when there is no such device.
    /// </summary>
    /// <exception cref="InventoryConflictException">The brand and model, or a manual, belong to another device.</exception>
    Task<Device?> UpdateAsync(Device device, CancellationToken cancellationToken = default);

    /// <summary>Deletes the device with its manuals and URLs (by cascade). Returns false when there is no such device.</summary>
    Task<bool> DeleteAsync(long deviceId, CancellationToken cancellationToken = default);
}

/// <summary>An excerpt of a manual, ready to be indexed.</summary>
public sealed record IndexedChunk(string Manual, int Page, int Number, string Text, float[] Embedding);

/// <summary>The content of the manuals, searchable by meaning. Derived from the inventory and the manuals folder.</summary>
public interface IManualsIndex
{
    /// <summary>Adds the chunks of a device. The ids are deterministic, so adding the same chunks again never duplicates them.</summary>
    Task AddAsync(long deviceId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default);

    Task DeleteByDeviceAsync(long deviceId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the chunks of one manual (path relative to the manuals folder) of a device.</summary>
    Task DeleteByManualAsync(long deviceId, string manual, CancellationToken cancellationToken = default);

    /// <summary>
    /// The only way to read content. It always filters by <paramref name="deviceId"/>, so a search can never return the
    /// chunks of another device. Returns at most <paramref name="count"/> chunks, the most relevant first; empty when
    /// the device has no chunks. The index embeds <paramref name="queryText"/> as it is given.
    /// </summary>
    Task<IReadOnlyList<Chunk>> SearchAsync(long deviceId, string queryText, int count, CancellationToken cancellationToken = default);
}
