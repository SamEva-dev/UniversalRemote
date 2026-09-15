using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Hub.Wifi;

/// <summary>
/// Transport-private Wi-Fi connection data. It intentionally lives outside UniversalRemote.Abstractions so endpoint
/// details never leak into product-facing hub models.
/// </summary>
public sealed class WifiInfraredHubConnection
{
    public InfraredHubId HubId { get; }
    public string Address { get; }
    public int Port { get; }
    public int ApiVersion { get; }

    public WifiInfraredHubConnection(InfraredHubId hubId, string address, int port, int apiVersion)
    {
        if (string.IsNullOrWhiteSpace(hubId.Value)) throw new ArgumentException("Hub ID must not be empty.", nameof(hubId));
        if (!WifiInfraredHubEndpoint.TryNormalizeLocalAddress(address, out var normalized))
            throw new ArgumentException("Wi-Fi hub endpoint must be a private/local literal IP address.", nameof(address));
        if (port is <= 0 or > 65_535) throw new ArgumentOutOfRangeException(nameof(port));
        if (apiVersion <= 0) throw new ArgumentOutOfRangeException(nameof(apiVersion));

        HubId = hubId;
        Address = normalized;
        Port = port;
        ApiVersion = apiVersion;
    }
}

/// <summary>Persistent transport-private storage. MAUI backs this with SecureStorage.</summary>
public interface IWifiInfraredHubConnectionStore
{
    ValueTask<WifiInfraredHubConnection?> FindAsync(InfraredHubId hubId, CancellationToken cancellationToken = default);
    Task SaveAsync(WifiInfraredHubConnection connection, CancellationToken cancellationToken = default);
    Task RemoveAsync(InfraredHubId hubId, CancellationToken cancellationToken = default);
}

/// <summary>Desktop/test fallback used when a platform-secure store was not registered.</summary>
public sealed class InMemoryWifiInfraredHubConnectionStore : IWifiInfraredHubConnectionStore
{
    private readonly ConcurrentDictionary<string, WifiInfraredHubConnection> _connections = new(StringComparer.Ordinal);

    public ValueTask<WifiInfraredHubConnection?> FindAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connections.TryGetValue(hubId.Value, out var connection);
        return ValueTask.FromResult(connection);
    }

    public Task SaveAsync(WifiInfraredHubConnection connection, CancellationToken cancellationToken = default)
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

internal static class WifiInfraredHubEndpoint
{
    public static bool TryNormalizeLocalAddress(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value.Trim(), out var ip)) return false;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || IPAddress.IsLoopback(ip)) return false;

        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (!IsAllowedLocalAddress(ip)) return false;

        normalized = ip.ToString();
        return true;
    }

    public static Uri Info(WifiInfraredHubConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new UriBuilder(Uri.UriSchemeHttp, connection.Address, connection.Port, WifiInfraredHubProtocol.InfoPath).Uri;
    }

    public static Uri Transmit(WifiInfraredHubConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new UriBuilder(Uri.UriSchemeHttp, connection.Address, connection.Port, WifiInfraredHubProtocol.TransmitPath).Uri;
    }

    public static Uri Learn(WifiInfraredHubConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new UriBuilder(Uri.UriSchemeHttp, connection.Address, connection.Port, WifiInfraredHubProtocol.LearnPath).Uri;
    }

    private static bool IsAllowedLocalAddress(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC; // fc00::/7 unique-local
        }

        return false;
    }
}

internal sealed class WifiInfraredHubCandidateCache
{
    private readonly ConcurrentDictionary<string, WifiInfraredHubConnection> _candidates = new(StringComparer.Ordinal);

    public void Set(WifiInfraredHubConnection connection) => _candidates[connection.HubId.Value] = connection;

    public bool TryGet(InfraredHubId hubId, out WifiInfraredHubConnection? connection)
        => _candidates.TryGetValue(hubId.Value, out connection);

    public void Remove(InfraredHubId hubId) => _candidates.TryRemove(hubId.Value, out _);
}
