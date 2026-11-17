using System.Data;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Tinker.Core.Storage;

/// <summary>Runs a unit of work in one transaction, so every store written inside it commits or rolls back together.</summary>
public interface ITransactionRunner
{
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);
}

/// <summary>
/// The SQLite file with the inventory and the manuals index (technical spec, section 7). Inside
/// <see cref="RunAsync{T}"/> every access shares one connection and its transaction; outside it each access opens its
/// own connection.
/// </summary>
public sealed class SqliteDatabase : ITransactionRunner
{
    private readonly string _connectionString;
    private readonly AsyncLocal<Ambient?> _ambient = new();

    public SqliteDatabase(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
    }

    /// <summary>Opens the database, creating the file if needed, and applies the pending migrations.</summary>
    public void Initialize()
    {
        using var connection = Open();
        var applied = Scalar<long>(connection, "PRAGMA user_version");
        var scripts = Migrations();
        if (applied > scripts.Count)
        {
            throw new InvalidOperationException(
                $"The database has schema version {applied}, newer than the {scripts.Count} this version of the server knows. Update the server.");
        }

        for (var version = (int)applied; version < scripts.Count; version++)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = scripts[version];
            command.ExecuteNonQuery();
            command.CommandText = $"PRAGMA user_version = {version + 1}";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (_ambient.Value is not null)
        {
            return await work();
        }

        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        _ambient.Value = new Ambient(connection, transaction);
        try
        {
            var result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            _ambient.Value = null;
        }
    }

    /// <summary>Runs <paramref name="action"/> with the connection of the current transaction, or a new one.</summary>
    internal async Task<T> UseAsync<T>(Func<SqliteConnection, SqliteTransaction?, Task<T>> action)
    {
        if (_ambient.Value is { } ambient)
        {
            return await action(ambient.Connection, ambient.Transaction);
        }

        await using var connection = Open();
        return await action(connection, null);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static T Scalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<string> Migrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Migrations.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                return reader.ReadToEnd();
            })
            .ToList();
    }

    private sealed record Ambient(SqliteConnection Connection, SqliteTransaction Transaction);
}
