using Microsoft.Data.Sqlite;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Persistence.Sqlite;

/// <summary>SQLite-backed room catalogue. Device assignment is exclusive and moving is atomic.</summary>
public sealed class SqliteRoomRepository : IRoomRepository
{
    private readonly string connectionString;
    private readonly string databasePath;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private int initialized;

    public SqliteRoomRepository(SqliteDeviceStoreOptions options)
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

    public async Task<Room?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return null;
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await LoadRoomAsync(connection, transaction: null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Room>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM rooms ORDER BY name COLLATE NOCASE, id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                ids.Add(Guid.Parse(reader.GetString(0)));
        }

        var rooms = new List<Room>(ids.Count);
        foreach (var id in ids)
        {
            var room = await LoadRoomAsync(connection, transaction: null, id, cancellationToken).ConfigureAwait(false);
            if (room is not null) rooms.Add(room);
        }
        return rooms;
    }

    public async Task<Room> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (await NameExistsAsync(connection, transaction, normalizedName, exceptRoomId: null, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("A room with the same name already exists.");

            var id = Guid.NewGuid();
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT INTO rooms(id, name, updated_utc) VALUES($id, $name, $updated);";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new Room(id, normalizedName);
        }
        finally { writeGate.Release(); }
    }

    public async Task<Room> RenameAsync(Guid roomId, string name, CancellationToken cancellationToken = default)
    {
        if (roomId == Guid.Empty) throw new ArgumentException("Room ID must not be empty.", nameof(roomId));
        var normalizedName = NormalizeName(name);
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (!await RoomExistsAsync(connection, transaction, roomId, cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException("Room not found.");
            if (await NameExistsAsync(connection, transaction, normalizedName, roomId, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("A room with the same name already exists.");

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = "UPDATE rooms SET name = $name, updated_utc = $updated WHERE id = $id;";
                command.Parameters.AddWithValue("$id", roomId.ToString("D"));
                command.Parameters.AddWithValue("$name", normalizedName);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var room = await LoadRoomAsync(connection, transaction, roomId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The renamed room could not be reloaded.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return room;
        }
        finally { writeGate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid roomId, CancellationToken cancellationToken = default)
    {
        if (roomId == Guid.Empty) return false;
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM rooms WHERE id = $id;";
            command.Parameters.AddWithValue("$id", roomId.ToString("D"));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        finally { writeGate.Release(); }
    }

    public async Task<Room> AssignDeviceAsync(Guid roomId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (roomId == Guid.Empty) throw new ArgumentException("Room ID must not be empty.", nameof(roomId));
        if (deviceId == Guid.Empty) throw new ArgumentException("Device ID must not be empty.", nameof(deviceId));
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (!await RoomExistsAsync(connection, transaction, roomId, cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException("Room not found.");
            if (!await DeviceExistsAsync(connection, transaction, deviceId, cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException("Device not found.");

            await using (var remove = connection.CreateCommand())
            {
                remove.Transaction = (SqliteTransaction)transaction;
                remove.CommandText = "DELETE FROM room_devices WHERE device_id = $deviceId;";
                remove.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
                await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText = "INSERT INTO room_devices(room_id, device_id, assigned_utc) VALUES($roomId, $deviceId, $assigned);";
                insert.Parameters.AddWithValue("$roomId", roomId.ToString("D"));
                insert.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
                insert.Parameters.AddWithValue("$assigned", DateTimeOffset.UtcNow.ToString("O"));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await TouchRoomAsync(connection, transaction, roomId, cancellationToken).ConfigureAwait(false);

            var room = await LoadRoomAsync(connection, transaction, roomId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The updated room could not be reloaded.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return room;
        }
        finally { writeGate.Release(); }
    }

    public async Task<Room> UnassignDeviceAsync(Guid roomId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (roomId == Guid.Empty) throw new ArgumentException("Room ID must not be empty.", nameof(roomId));
        if (deviceId == Guid.Empty) throw new ArgumentException("Device ID must not be empty.", nameof(deviceId));
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (!await RoomExistsAsync(connection, transaction, roomId, cancellationToken).ConfigureAwait(false))
                throw new KeyNotFoundException("Room not found.");

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = "DELETE FROM room_devices WHERE room_id = $roomId AND device_id = $deviceId;";
                command.Parameters.AddWithValue("$roomId", roomId.ToString("D"));
                command.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await TouchRoomAsync(connection, transaction, roomId, cancellationToken).ConfigureAwait(false);
            var room = await LoadRoomAsync(connection, transaction, roomId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The updated room could not be reloaded.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return room;
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

    private static async Task<Room?> LoadRoomAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        Guid roomId,
        CancellationToken cancellationToken)
    {
        string? name = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction?)transaction;
            command.CommandText = "SELECT name FROM rooms WHERE id = $id;";
            command.Parameters.AddWithValue("$id", roomId.ToString("D"));
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null || value is DBNull) return null;
            name = (string)value;
        }

        var deviceIds = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction?)transaction;
            command.CommandText = "SELECT device_id FROM room_devices WHERE room_id = $id ORDER BY assigned_utc, device_id;";
            command.Parameters.AddWithValue("$id", roomId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                deviceIds.Add(Guid.Parse(reader.GetString(0)));
        }
        return new Room(roomId, name, deviceIds);
    }

    private static async Task<bool> RoomExistsAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, Guid id, CancellationToken cancellationToken)
        => await ExistsAsync(connection, transaction, "rooms", id, cancellationToken).ConfigureAwait(false);

    private static async Task<bool> DeviceExistsAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, Guid id, CancellationToken cancellationToken)
        => await ExistsAsync(connection, transaction, "devices", id, cancellationToken).ConfigureAwait(false);

    private static async Task<bool> ExistsAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string table, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"SELECT 1 FROM {table} WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<bool> NameExistsAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string name,
        Guid? exceptRoomId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = exceptRoomId is null
            ? "SELECT 1 FROM rooms WHERE name = $name COLLATE NOCASE LIMIT 1;"
            : "SELECT 1 FROM rooms WHERE name = $name COLLATE NOCASE AND id <> $id LIMIT 1;";
        command.Parameters.AddWithValue("$name", name);
        if (exceptRoomId is not null) command.Parameters.AddWithValue("$id", exceptRoomId.Value.ToString("D"));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task TouchRoomAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, Guid roomId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE rooms SET updated_utc = $updated WHERE id = $id;";
        command.Parameters.AddWithValue("$id", roomId.ToString("D"));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 60) throw new ArgumentOutOfRangeException(nameof(name), "Room name must be 60 characters or fewer.");
        return normalized;
    }
}
