using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Storage;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Hub.Ble;

namespace UniversalRemote.Maui.Hub;

/// <summary>Stores the transport-private BLE device key in platform SecureStorage.</summary>
public sealed class SecureBleInfraredHubConnectionStore : IBleInfraredHubConnectionStore
{
    public async ValueTask<BleInfraredHubConnection?> FindAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await SecureStorage.Default.GetAsync(Key(hubId)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split('\n');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var apiVersion)) return null;
        try { return new BleInfraredHubConnection(hubId, parts[0], apiVersion); }
        catch (ArgumentException) { return null; }
    }

    public Task SaveAsync(BleInfraredHubConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.SetAsync(Key(connection.HubId), $"{connection.DeviceKey}\n{connection.ApiVersion}");
    }

    public Task RemoveAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(Key(hubId));
        return Task.CompletedTask;
    }

    private static string Key(InfraredHubId hubId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(hubId.Value));
        return $"universalremote.hub.ble.{Convert.ToHexString(hash[..16]).ToLowerInvariant()}";
    }
}
