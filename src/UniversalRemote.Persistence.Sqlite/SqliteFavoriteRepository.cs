using Microsoft.Data.Sqlite;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Persistence.Sqlite;

/// <summary>SQLite-backed, device-scoped action favorites.</summary>
public sealed class SqliteFavoriteRepository : IFavoriteRepository
{
    private readonly string connectionString;
    private readonly string databasePath;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private int initialized;

    public SqliteFavoriteRepository(SqliteDeviceStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        databasePath = options.DatabasePath;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task<IReadOnlyList<Favorite>> ListAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty) return Array.Empty<Favorite>();
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await LoadAsync(connection, transaction: null, deviceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Favorite> AddAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty) throw new ArgumentException("Favorite device ID must not be empty.", nameof(deviceId));
        ArgumentNullException.ThrowIfNull(action);
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (!await DeviceExistsAsync(connection, transaction, deviceId, cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException("Device not found.");

            var existing = await FindAsync(connection, transaction, deviceId, action.Id, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return existing;
            }

            var position = await NextPositionAsync(connection, transaction, deviceId, cancellationToken).ConfigureAwait(false);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO device_favorites(device_id, action_id, sort_order, created_utc)
                    VALUES($device, $action, $order, $created);
                    """;
                command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
                command.Parameters.AddWithValue("$action", action.Id);
                command.Parameters.AddWithValue("$order", position);
                command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new Favorite(deviceId, action, position);
        }
        finally { writeGate.Release(); }
    }

    public async Task<bool> RemoveAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty) return false;
        ArgumentNullException.ThrowIfNull(action);
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "DELETE FROM device_favorites WHERE device_id = $device AND action_id = $action;";
            command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
            command.Parameters.AddWithValue("$action", action.Id);
            var changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return changed;
        }
        finally { writeGate.Release(); }
    }

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref initialized) == 1) return;
        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized == 1) return;
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await using var connection = await OpenConnectionAsync(cancellationToken, configure: false).ConfigureAwait(false);
            await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref initialized, 1);
        }
        finally { initializationGate.Release(); }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken, bool configure = true)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (configure)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IReadOnlyList<Favorite>> LoadAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var result = new List<Favorite>();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = "SELECT action_id, sort_order FROM device_favorites WHERE device_id = $device ORDER BY sort_order, action_id;";
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new Favorite(deviceId, new RemoteAction(reader.GetString(0)), reader.GetInt32(1)));
        return result;
    }

    private static async Task<Favorite?> FindAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid deviceId,
        string actionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT sort_order FROM device_favorites WHERE device_id = $device AND action_id = $action LIMIT 1;";
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$action", actionId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : new Favorite(deviceId, new RemoteAction(actionId), Convert.ToInt32(value));
    }

    private static async Task<int> NextPositionAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM device_favorites WHERE device_id = $device;";
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<bool> DeviceExistsAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT 1 FROM devices WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", deviceId.ToString("D"));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }
}
