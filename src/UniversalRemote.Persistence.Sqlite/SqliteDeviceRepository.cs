using Microsoft.Data.Sqlite;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Persistence.Sqlite;

/// <summary>
/// Local device catalogue. Pairing identity is the exact provider-id/device-key pair; secrets remain in provider secure storage.
/// </summary>
public sealed class SqliteDeviceRepository : IDeviceRepository, IDeviceRegistrar
{
    private readonly string connectionString;
    private readonly string databasePath;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private int initialized;

    public SqliteDeviceRepository(SqliteDeviceStoreOptions options)
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

    public async Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return null;
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await LoadDeviceAsync(connection, transaction: null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM devices ORDER BY display_name COLLATE NOCASE, id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                ids.Add(Guid.Parse(reader.GetString(0)));
        }

        var result = new List<Device>(ids.Count);
        foreach (var id in ids)
        {
            var device = await LoadDeviceAsync(connection, transaction: null, id, cancellationToken).ConfigureAwait(false);
            if (device is not null) result.Add(device);
        }
        return result;
    }

    public async Task UpsertAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await AssertRoutesAvailableAsync(connection, transaction, device, cancellationToken).ConfigureAwait(false);
            await UpsertDeviceRowAsync(connection, transaction, device.Id, device.DisplayName, cancellationToken).ConfigureAwait(false);
            await DeleteRoutesAsync(connection, transaction, device.Id, cancellationToken).ConfigureAwait(false);
            await InsertRoutesAsync(connection, transaction, device.Id, device.Routes, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { writeGate.Release(); }
    }

    public async Task<Device> RegisterPairingAsync(
        string displayName,
        DeviceRoute route,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(route);
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var existing = await FindRouteOwnerAsync(connection, transaction, route.ProviderId, route.DeviceKey, cancellationToken).ConfigureAwait(false);
            var id = existing?.DeviceId ?? Guid.NewGuid();

            await UpsertDeviceRowAsync(connection, transaction, id, displayName, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                var nextOrder = await NextRouteOrderAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
                await InsertRouteAsync(connection, transaction, id, nextOrder, route, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ReplaceRouteCapabilitiesAsync(connection, transaction, id, existing.Value.RouteOrder, route.Capabilities, cancellationToken).ConfigureAwait(false);
            }

            var device = await LoadDeviceAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The paired device could not be reloaded.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return device;
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

    private static async Task UpsertDeviceRowAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid id,
        string displayName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO devices(id, display_name, updated_utc)
            VALUES($id, $name, $updated)
            ON CONFLICT(id) DO UPDATE SET display_name = excluded.display_name, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$name", displayName);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteRoutesAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM device_routes WHERE device_id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertRoutesAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid id,
        IReadOnlyList<DeviceRoute> routes,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < routes.Count; index++)
            await InsertRouteAsync(connection, transaction, id, index, routes[index], cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertRouteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid id,
        int routeOrder,
        DeviceRoute route,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO device_routes(device_id, route_order, provider_id, device_key)
                VALUES($id, $order, $provider, $key);
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$order", routeOrder);
            command.Parameters.AddWithValue("$provider", route.ProviderId);
            command.Parameters.AddWithValue("$key", route.DeviceKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await InsertCapabilitiesAsync(connection, transaction, id, routeOrder, route.Capabilities, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplaceRouteCapabilitiesAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid id,
        int routeOrder,
        IReadOnlySet<RemoteAction> capabilities,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM route_capabilities WHERE device_id = $id AND route_order = $order;";
            delete.Parameters.AddWithValue("$id", id.ToString("D"));
            delete.Parameters.AddWithValue("$order", routeOrder);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await InsertCapabilitiesAsync(connection, transaction, id, routeOrder, capabilities, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCapabilitiesAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid id,
        int routeOrder,
        IEnumerable<RemoteAction> capabilities,
        CancellationToken cancellationToken)
    {
        foreach (var capability in capabilities.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO route_capabilities(device_id, route_order, action_id)
                VALUES($id, $order, $action);
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$order", routeOrder);
            command.Parameters.AddWithValue("$action", capability.Id);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task AssertRoutesAvailableAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Device device,
        CancellationToken cancellationToken)
    {
        foreach (var route in device.Routes)
        {
            var owner = await FindRouteOwnerAsync(connection, transaction, route.ProviderId, route.DeviceKey, cancellationToken).ConfigureAwait(false);
            if (owner is not null && owner.Value.DeviceId != device.Id)
                throw new InvalidOperationException("A provider route is already assigned to another device.");
        }
    }

    private static async Task<(Guid DeviceId, int RouteOrder)?> FindRouteOwnerAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string providerId,
        string deviceKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            SELECT device_id, route_order
            FROM device_routes
            WHERE provider_id = $provider AND device_key = $key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$provider", providerId);
        command.Parameters.AddWithValue("$key", deviceKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return (Guid.Parse(reader.GetString(0)), reader.GetInt32(1));
    }

    private static async Task<int> NextRouteOrderAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT COALESCE(MAX(route_order) + 1, 0) FROM device_routes WHERE device_id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Device?> LoadDeviceAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        string? displayName;
        await using (var command = connection.CreateCommand())
        {
            if (transaction is not null) command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "SELECT display_name FROM devices WHERE id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            displayName = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        if (displayName is null) return null;

        var rows = new List<RouteRow>();
        await using (var command = connection.CreateCommand())
        {
            if (transaction is not null) command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                SELECT r.route_order, r.provider_id, r.device_key, c.action_id
                FROM device_routes r
                LEFT JOIN route_capabilities c
                  ON c.device_id = r.device_id AND c.route_order = r.route_order
                WHERE r.device_id = $id
                ORDER BY r.route_order, c.action_id;
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new RouteRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        var routes = rows.GroupBy(x => new { x.RouteOrder, x.ProviderId, x.DeviceKey })
            .OrderBy(x => x.Key.RouteOrder)
            .Select(group => new DeviceRoute(
                group.Key.ProviderId,
                group.Key.DeviceKey,
                group.Where(x => x.ActionId is not null).Select(x => new RemoteAction(x.ActionId!))))
            .ToArray();
        if (routes.Length == 0) throw new InvalidOperationException("Stored device has no route.");
        return new Device(id, displayName, routes);
    }

    private sealed record RouteRow(int RouteOrder, string ProviderId, string DeviceKey, string? ActionId);
}
