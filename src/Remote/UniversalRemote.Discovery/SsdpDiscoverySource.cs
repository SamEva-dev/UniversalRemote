using System.Collections.Frozen;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UniversalRemote.Remote.Discovery;

public sealed class SsdpDiscoverySource : IDiscoverySource
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), 1900);
    public string Id => "ssdp";

    public async Task<IReadOnlyList<DiscoveryCandidate>> DiscoverAsync(DiscoveryScanOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var request = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: ssdp:all\r\n\r\n";
        var payload = Encoding.ASCII.GetBytes(request);
        await client.SendAsync(payload.AsMemory(), MulticastEndpoint, cancellationToken).ConfigureAwait(false);

        var results = new Dictionary<string, DiscoveryCandidate>(StringComparer.OrdinalIgnoreCase);
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult response;
            try { response = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            var headers = ParseHeaders(Encoding.UTF8.GetString(response.Buffer));
            headers.TryGetValue("USN", out var usn);
            headers.TryGetValue("LOCATION", out var location);
            headers.TryGetValue("ST", out var serviceType);
            if (string.IsNullOrWhiteSpace(usn) && string.IsNullOrWhiteSpace(location)) continue;

            var key = usn ?? location!;
            var metadata = headers
                .Where(x => x.Key is "SERVER" or "LOCATION" or "USN" or "ST" or "CACHE-CONTROL")
                .ToFrozenDictionary(x => x.Key.ToLowerInvariant(), x => x.Value, StringComparer.OrdinalIgnoreCase);
            results[key] = new DiscoveryCandidate
            {
                SourceId = Id,
                StableKey = key,
                DisplayName = BuildDisplayName(headers, response.RemoteEndPoint.Address),
                HostName = TryLocationHost(location),
                Addresses = new[] { response.RemoteEndPoint.Address.ToString() }.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                Services = string.IsNullOrWhiteSpace(serviceType)
                    ? Array.Empty<string>().ToFrozenSet(StringComparer.OrdinalIgnoreCase)
                    : new[] { serviceType }.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                Metadata = metadata
            };
        }
        return results.Values.ToArray();
    }

    internal static Dictionary<string, string> ParseHeaders(string response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in response.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (name.Length > 0 && value.Length > 0) headers[name] = value;
        }
        return headers;
    }

    private static string BuildDisplayName(IReadOnlyDictionary<string, string> headers, IPAddress address)
    {
        if (headers.TryGetValue("SERVER", out var server) && !string.IsNullOrWhiteSpace(server)) return server;
        if (headers.TryGetValue("ST", out var st) && !string.IsNullOrWhiteSpace(st)) return $"{st} — {address}";
        return $"UPnP/SSDP — {address}";
    }

    private static string? TryLocationHost(string? location)
        => Uri.TryCreate(location, UriKind.Absolute, out var uri) ? uri.Host : null;
}
