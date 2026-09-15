using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Storage;
using UniversalRemote.Abstractions;
using UniversalRemote.Hub.Wifi;

namespace UniversalRemote.Maui.Hub;

/// <summary>Stores Wi-Fi hub connection metadata in the platform secure store rather than the device SQLite catalogue.</summary>
public sealed class SecureWifiInfraredHubConnectionStore : IWifiInfraredHubConnectionStore
{
    public async ValueTask<WifiInfraredHubConnection?> FindAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await SecureStorage.Default.GetAsync(Key(hubId)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var parts = raw.Split('\n');
        if (parts.Length != 3
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
            || !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var apiVersion))
            return null;

        try { return new WifiInfraredHubConnection(hubId, parts[0], port, apiVersion); }
        catch (ArgumentException) { return null; }
    }

    public Task SaveAsync(WifiInfraredHubConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        var raw = string.Join('\n',
            connection.Address,
            connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            connection.ApiVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return SecureStorage.Default.SetAsync(Key(connection.HubId), raw);
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
        return $"universalremote.hub.wifi.{Convert.ToHexString(hash[..16]).ToLowerInvariant()}";
    }
}
