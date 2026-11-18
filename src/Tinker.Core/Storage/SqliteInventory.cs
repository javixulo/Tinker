using System.Globalization;
using Microsoft.Data.Sqlite;
using Tinker.Core.Models;

namespace Tinker.Core.Storage;

public sealed class SqliteInventory(SqliteDatabase database) : IInventory
{
    private const int SqliteConstraint = 19;

    public Task<Device?> FindByBrandAndModelAsync(string brand, string model, CancellationToken cancellationToken = default) =>
        database.UseAsync(async (connection, transaction) =>
        {
            using var command = Command(connection, transaction, "SELECT device_id FROM devices WHERE brand_key = $brand AND model_key = $model");
            command.Parameters.AddWithValue("$brand", TextKeys.Fold(brand));
            command.Parameters.AddWithValue("$model", TextKeys.Fold(model));
            return await LoadAsync(connection, transaction, await command.ExecuteScalarAsync(cancellationToken), cancellationToken);
        });

    public Task<Device?> FindByManualAsync(string manual, CancellationToken cancellationToken = default) =>
        database.UseAsync(async (connection, transaction) =>
        {
            using var command = Command(connection, transaction, "SELECT device_id FROM device_manuals WHERE path = $path");
            command.Parameters.AddWithValue("$path", manual);
            return await LoadAsync(connection, transaction, await command.ExecuteScalarAsync(cancellationToken), cancellationToken);
        });

    public Task<IReadOnlyList<Device>> ListAllAsync(CancellationToken cancellationToken = default) =>
        database.UseAsync<IReadOnlyList<Device>>(async (connection, transaction) =>
        {
            var ids = new List<long>();
            using (var command = Command(connection, transaction, "SELECT device_id FROM devices ORDER BY device_id"))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    ids.Add(reader.GetInt64(0));
                }
            }

            var devices = new List<Device>(ids.Count);
            foreach (var id in ids)
            {
                devices.Add((await LoadAsync(connection, transaction, id, cancellationToken))!);
            }

            return devices;
        });

    public Task<Device> InsertAsync(Device device, CancellationToken cancellationToken = default) =>
        database.UseAsync(async (connection, transaction) =>
        {
            try
            {
                long id;
                using (var command = Command(connection, transaction,
                    """
                    INSERT INTO devices (brand, model, brand_key, model_key, device_type, color, location, invoice,
                                         purchase_date, warranty_end_date, status)
                    VALUES ($brand, $model, $brand_key, $model_key, $device_type, $color, $location, $invoice,
                            $purchase_date, $warranty_end_date, $status)
                    RETURNING device_id
                    """))
                {
                    command.Parameters.AddWithValue("$brand", device.Brand);
                    command.Parameters.AddWithValue("$model", device.Model);
                    command.Parameters.AddWithValue("$brand_key", TextKeys.Fold(device.Brand));
                    command.Parameters.AddWithValue("$model_key", TextKeys.Fold(device.Model));
                    command.Parameters.AddWithValue("$device_type", (object?)device.DeviceType ?? DBNull.Value);
                    command.Parameters.AddWithValue("$color", (object?)device.Color ?? DBNull.Value);
                    command.Parameters.AddWithValue("$location", (object?)device.Location ?? DBNull.Value);
                    command.Parameters.AddWithValue("$invoice", (object?)device.Invoice ?? DBNull.Value);
                    command.Parameters.AddWithValue("$purchase_date", (object?)device.PurchaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
                    command.Parameters.AddWithValue("$warranty_end_date", (object?)device.WarrantyEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
                    command.Parameters.AddWithValue("$status", (object?)device.Status ?? DBNull.Value);
                    id = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
                }

                foreach (var manual in device.Manuals ?? [])
                {
                    await InsertChildAsync(connection, transaction, "device_manuals", "path", id, manual, cancellationToken);
                }

                foreach (var url in device.Urls ?? [])
                {
                    await InsertChildAsync(connection, transaction, "device_urls", "url", id, url, cancellationToken);
                }

                return (await LoadAsync(connection, transaction, id, cancellationToken))!;
            }
            catch (SqliteException error) when (error.SqliteErrorCode == SqliteConstraint && error.Message.Contains("UNIQUE", StringComparison.Ordinal))
            {
                // The database is the guarantee: the checks in code give reasons, the constraints decide.
                var conflict = error.Message.Contains("device_manuals", StringComparison.Ordinal) ? Conflict.ManualInUse : Conflict.DeviceExists;
                throw new InventoryConflictException(conflict, error);
            }
        });

    private static async Task InsertChildAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string table, string column, long deviceId, string value, CancellationToken cancellationToken)
    {
        // The table and column names are constants of this class, never client input.
        using var command = Command(connection, transaction, $"INSERT INTO {table} (device_id, {column}) VALUES ($id, $value)");
        command.Parameters.AddWithValue("$id", deviceId);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Device?> LoadAsync(SqliteConnection connection, SqliteTransaction? transaction, object? id, CancellationToken cancellationToken)
    {
        if (id is null or DBNull)
        {
            return null;
        }

        Device? device = null;
        using (var command = Command(connection, transaction, "SELECT * FROM devices WHERE device_id = $id"))
        {
            command.Parameters.AddWithValue("$id", id);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                device = new Device
                {
                    DeviceId = reader.GetInt64(reader.GetOrdinal("device_id")),
                    Brand = reader.GetString(reader.GetOrdinal("brand")),
                    Model = reader.GetString(reader.GetOrdinal("model")),
                    DeviceType = Text(reader, "device_type"),
                    Color = Text(reader, "color"),
                    Location = Text(reader, "location"),
                    Invoice = Text(reader, "invoice"),
                    PurchaseDate = Date(reader, "purchase_date"),
                    WarrantyEndDate = Date(reader, "warranty_end_date"),
                    Status = Text(reader, "status"),
                };
            }
        }

        if (device is null)
        {
            return null;
        }

        return device with
        {
            Manuals = await ListAsync(connection, transaction, "SELECT path FROM device_manuals WHERE device_id = $id ORDER BY rowid", id, cancellationToken),
            Urls = await ListAsync(connection, transaction, "SELECT url FROM device_urls WHERE device_id = $id ORDER BY rowid", id, cancellationToken),
        };
    }

    private static async Task<IReadOnlyList<string>> ListAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string sql, object id, CancellationToken cancellationToken)
    {
        using var command = Command(connection, transaction, sql);
        command.Parameters.AddWithValue("$id", id);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static string? Text(SqliteDataReader reader, string column) =>
        reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));

    private static DateOnly? Date(SqliteDataReader reader, string column) =>
        Text(reader, column) is { } value ? DateOnly.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : null;

    internal static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }
}
