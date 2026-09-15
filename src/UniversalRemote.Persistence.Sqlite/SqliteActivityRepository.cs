using Microsoft.Data.Sqlite;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Persistence.Sqlite;

/// <summary>SQLite-backed Activities. The complete ordered step list is replaced in one transaction.</summary>
public sealed class SqliteActivityRepository : IActivityRepository
{
    private readonly string connectionString;
    private readonly string databasePath;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private int initialized;

    public SqliteActivityRepository(SqliteDeviceStoreOptions options)
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

    public async Task<Activity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return null;
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await LoadOneAsync(connection, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Activity>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var ids = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM activities ORDER BY name COLLATE NOCASE, id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(Guid.Parse(reader.GetString(0)));
        }

        var result = new List<Activity>(ids.Count);
        foreach (var id in ids)
        {
            var activity = await LoadOneAsync(connection, id, cancellationToken).ConfigureAwait(false);
            if (activity is not null) result.Add(activity);
        }
        return result;
    }

    public async Task<Activity> SaveAsync(Activity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO activities(id, name, room_id, updated_utc)
                    VALUES($id, $name, $room, $updated)
                    ON CONFLICT(id) DO UPDATE SET
                        name = excluded.name,
                        room_id = excluded.room_id,
                        updated_utc = excluded.updated_utc;
                    """;
                command.Parameters.AddWithValue("$id", activity.Id.ToString("D"));
                command.Parameters.AddWithValue("$name", activity.Name);
                command.Parameters.AddWithValue("$room", activity.RoomId is null ? DBNull.Value : activity.RoomId.Value.ToString("D"));
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = (SqliteTransaction)transaction;
                delete.CommandText = "DELETE FROM activity_steps WHERE activity_id = $id;";
                delete.Parameters.AddWithValue("$id", activity.Id.ToString("D"));
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var step in activity.Steps)
                await InsertStepAsync(connection, (SqliteTransaction)transaction, activity.Id, step, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return activity;
        }
        finally { writeGate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return false;
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM activities WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        finally { writeGate.Release(); }
    }

    private static async Task InsertStepAsync(SqliteConnection connection, SqliteTransaction transaction, Guid activityId, ActivityStep step, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO activity_steps(activity_id, step_order, step_kind, device_id, action_id, delay_ms)
            VALUES($activity, $order, $kind, $device, $action, $delay);
            """;
        command.Parameters.AddWithValue("$activity", activityId.ToString("D"));
        command.Parameters.AddWithValue("$order", step.Position);

        switch (step)
        {
            case RemoteActionActivityStep action:
                command.Parameters.AddWithValue("$kind", "action");
                command.Parameters.AddWithValue("$device", action.DeviceId.ToString("D"));
                command.Parameters.AddWithValue("$action", action.Action.Id);
                command.Parameters.AddWithValue("$delay", DBNull.Value);
                break;
            case DelayActivityStep delay:
                command.Parameters.AddWithValue("$kind", "delay");
                command.Parameters.AddWithValue("$device", DBNull.Value);
                command.Parameters.AddWithValue("$action", DBNull.Value);
                command.Parameters.AddWithValue("$delay", checked((int)delay.Duration.TotalMilliseconds));
                break;
            default:
                throw new NotSupportedException("Unsupported ActivityStep type.");
        }
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Activity?> LoadOneAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        string? name = null;
        Guid? roomId = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name, room_id FROM activities WHERE id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            name = reader.GetString(0);
            if (!reader.IsDBNull(1)) roomId = Guid.Parse(reader.GetString(1));
        }

        var steps = new List<ActivityStep>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT step_order, step_kind, device_id, action_id, delay_ms
                FROM activity_steps
                WHERE activity_id = $id
                ORDER BY step_order;
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var position = reader.GetInt32(0);
                var kind = reader.GetString(1);
                steps.Add(kind switch
                {
                    "action" when !reader.IsDBNull(2) && !reader.IsDBNull(3)
                        => new RemoteActionActivityStep(position, Guid.Parse(reader.GetString(2)), new RemoteAction(reader.GetString(3))),
                    "delay" when !reader.IsDBNull(4)
                        => new DelayActivityStep(position, TimeSpan.FromMilliseconds(reader.GetInt32(4))),
                    _ => throw new InvalidDataException("Stored activity step is invalid.")
                });
            }
        }

        return new Activity(id, name!, roomId, steps);
    }

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref initialized) == 1) return;
        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized == 1) return;
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await SqliteSchema.EnsureCreatedAsync(connection, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref initialized, 1);
        }
        finally { initializationGate.Release(); }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
