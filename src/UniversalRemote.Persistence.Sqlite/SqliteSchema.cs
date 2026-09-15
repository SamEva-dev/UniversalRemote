using Microsoft.Data.Sqlite;

namespace UniversalRemote.Persistence.Sqlite;

internal static class SqliteSchema
{
    public static async Task EnsureCreatedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS devices (
                id TEXT NOT NULL PRIMARY KEY,
                display_name TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS device_routes (
                device_id TEXT NOT NULL,
                route_order INTEGER NOT NULL,
                provider_id TEXT NOT NULL,
                device_key TEXT NOT NULL,
                PRIMARY KEY (device_id, route_order),
                UNIQUE (provider_id, device_key),
                FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS route_capabilities (
                device_id TEXT NOT NULL,
                route_order INTEGER NOT NULL,
                action_id TEXT NOT NULL,
                PRIMARY KEY (device_id, route_order, action_id),
                FOREIGN KEY (device_id, route_order)
                    REFERENCES device_routes(device_id, route_order) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS rooms (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS room_devices (
                room_id TEXT NOT NULL,
                device_id TEXT NOT NULL UNIQUE,
                assigned_utc TEXT NOT NULL,
                PRIMARY KEY (room_id, device_id),
                FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
                FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
