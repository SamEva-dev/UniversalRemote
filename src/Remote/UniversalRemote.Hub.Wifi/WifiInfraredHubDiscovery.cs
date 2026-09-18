using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Discovery;

namespace UniversalRemote.Remote.Hub.Wifi;

/// <summary>Discovers only UniversalRemote IR hubs advertised through the dedicated mDNS service.</summary>
internal sealed class WifiInfraredHubDiscovery(
    IDeviceDiscovery deviceDiscovery,
    WifiInfraredHubCandidateCache candidateCache) : IInfraredHubDiscovery
{
    public InfraredHubTransportKind TransportKind => InfraredHubTransportKind.Wifi;

    public async Task<IReadOnlyList<InfraredHubAdvertisement>> DiscoverAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(3);
        if (effectiveTimeout <= TimeSpan.Zero || effectiveTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var devices = await deviceDiscovery.DiscoverAsync(new DiscoveryScanOptions
        {
            Timeout = effectiveTimeout,
            MdnsServiceTypes = [WifiInfraredHubProtocol.MdnsServiceType]
        }, cancellationToken).ConfigureAwait(false);

        var candidates = new List<(InfraredHubAdvertisement Advertisement, WifiInfraredHubConnection Connection)>();
        foreach (var device in devices)
        {
            if (!device.Services.Contains(WifiInfraredHubProtocol.MdnsServiceType)) continue;
            if (!device.Metadata.TryGetValue("port", out var portRaw)
                || !int.TryParse(portRaw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
                || port is <= 0 or > 65_535)
                continue;

            var txt = device.Metadata.TryGetValue("txt", out var txtRaw) ? ParseTxt(txtRaw) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!txt.TryGetValue("id", out var hubIdRaw) || !txt.TryGetValue("api", out var apiRaw)
                || !int.TryParse(apiRaw, out var apiVersion) || apiVersion != WifiInfraredHubProtocol.ApiVersion)
                continue;

            InfraredHubId hubId;
            try { hubId = new InfraredHubId(hubIdRaw); }
            catch (ArgumentException) { continue; }

            var address = device.Addresses.FirstOrDefault(value => WifiInfraredHubEndpoint.TryNormalizeLocalAddress(value, out _));
            if (!WifiInfraredHubEndpoint.TryNormalizeLocalAddress(address, out var normalizedAddress)) continue;

            WifiInfraredHubConnection connection;
            try { connection = new WifiInfraredHubConnection(hubId, normalizedAddress, port, apiVersion); }
            catch (ArgumentException) { continue; }

            var displayName = txt.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim() : device.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = "Hub IR UniversalRemote";
            var firmware = txt.TryGetValue("fw", out var fw) && !string.IsNullOrWhiteSpace(fw) ? fw.Trim() : null;

            candidates.Add((new InfraredHubAdvertisement(
                hubId,
                displayName,
                TransportKind,
                "hub.wifi.discovered",
                firmware), connection));
        }

        // A duplicated hub ID pointing at different endpoints is suspicious; skip it rather than choosing arbitrarily.
        var results = new List<InfraredHubAdvertisement>();
        foreach (var group in candidates.GroupBy(x => x.Advertisement.Id.Value, StringComparer.Ordinal))
        {
            var distinctEndpoints = group.Select(x => $"{x.Connection.Address}:{x.Connection.Port}")
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (distinctEndpoints.Length != 1) continue;
            var selected = group.First();
            candidateCache.Set(selected.Connection);
            results.Add(selected.Advertisement);
        }

        return results.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, string> ParseTxt(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = item.IndexOf('=');
            if (separator <= 0 || separator == item.Length - 1) continue;
            var key = item[..separator].Trim();
            var text = item[(separator + 1)..].Trim();
            if (key.Length is > 0 and <= 32 && text.Length is > 0 and <= 256)
                result.TryAdd(key, text);
        }
        return result;
    }
}
