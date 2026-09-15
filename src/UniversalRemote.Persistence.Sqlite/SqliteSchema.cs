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

            CREATE TABLE IF NOT EXISTS device_favorites (
                device_id TEXT NOT NULL,
                action_id TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                PRIMARY KEY (device_id, action_id),
                FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_device_favorites_order
                ON device_favorites(device_id, sort_order, action_id);

            CREATE TABLE IF NOT EXISTS activities (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                room_id TEXT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS activity_steps (
                activity_id TEXT NOT NULL,
                step_order INTEGER NOT NULL,
                step_kind TEXT NOT NULL,
                device_id TEXT NULL,
                action_id TEXT NULL,
                delay_ms INTEGER NULL,
                PRIMARY KEY (activity_id, step_order),
                FOREIGN KEY (activity_id) REFERENCES activities(id) ON DELETE CASCADE,
                FOREIGN KEY (device_id) REFERENCES devices(id) ON DELETE RESTRICT,
                CHECK (
                    (step_kind = 'action' AND device_id IS NOT NULL AND action_id IS NOT NULL AND delay_ms IS NULL)
                    OR
                    (step_kind = 'delay' AND device_id IS NULL AND action_id IS NULL AND delay_ms BETWEEN 50 AND 60000)
                )
            );

            CREATE INDEX IF NOT EXISTS ix_activities_room
                ON activities(room_id, name, id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
