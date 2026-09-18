using System.Collections.Concurrent;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub.Ble;

/// <summary>Transport-private BLE connection identity. DeviceKey may be a platform address/handle and is never product-facing.</summary>
public sealed class BleInfraredHubConnection
{
    public InfraredHubId HubId { get; }
    public string DeviceKey { get; }
    public int ApiVersion { get; }

    public BleInfraredHubConnection(InfraredHubId hubId, string deviceKey, int apiVersion)
    {
        if (string.IsNullOrWhiteSpace(hubId.Value)) throw new ArgumentException("Hub ID must not be empty.", nameof(hubId));
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        if (deviceKey.Length > 256) throw new ArgumentOutOfRangeException(nameof(deviceKey));
        if (apiVersion <= 0) throw new ArgumentOutOfRangeException(nameof(apiVersion));
        HubId = hubId;
        DeviceKey = deviceKey.Trim();
        ApiVersion = apiVersion;
    }
}

public interface IBleInfraredHubConnectionStore
{
    ValueTask<BleInfraredHubConnection?> FindAsync(InfraredHubId hubId, CancellationToken cancellationToken = default);
    Task SaveAsync(BleInfraredHubConnection connection, CancellationToken cancellationToken = default);
    Task RemoveAsync(InfraredHubId hubId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryBleInfraredHubConnectionStore : IBleInfraredHubConnectionStore
{
    private readonly ConcurrentDictionary<string, BleInfraredHubConnection> _connections = new(StringComparer.Ordinal);

    public ValueTask<BleInfraredHubConnection?> FindAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connections.TryGetValue(hubId.Value, out var connection);
        return ValueTask.FromResult(connection);
    }

    public Task SaveAsync(BleInfraredHubConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        _connections[connection.HubId.Value] = connection;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connections.TryRemove(hubId.Value, out _);
        return Task.CompletedTask;
    }
}

internal sealed class BleInfraredHubCandidateCache
{
    private readonly ConcurrentDictionary<string, BleInfraredHubConnection> _candidates = new(StringComparer.Ordinal);
    public void Set(BleInfraredHubConnection connection) => _candidates[connection.HubId.Value] = connection;
    public bool TryGet(InfraredHubId hubId, out BleInfraredHubConnection? connection) => _candidates.TryGetValue(hubId.Value, out connection);
    public void Remove(InfraredHubId hubId) => _candidates.TryRemove(hubId.Value, out _);
}
