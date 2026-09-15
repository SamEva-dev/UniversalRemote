using UniversalRemote.Abstractions;

namespace UniversalRemote.Hub.Ble;

internal sealed class BleInfraredHubDiscovery(
    IBleInfraredHubRadio radio,
    BleInfraredHubCandidateCache candidateCache) : IInfraredHubDiscovery
{
    public InfraredHubTransportKind TransportKind => InfraredHubTransportKind.BluetoothLowEnergy;

    public async Task<IReadOnlyList<InfraredHubAdvertisement>> DiscoverAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(4);
        if (effectiveTimeout <= TimeSpan.Zero || effectiveTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var scan = await radio.ScanAsync(effectiveTimeout, cancellationToken).ConfigureAwait(false);
        if (scan.Outcome != BleInfraredHubRadioScanOutcome.Success) return [];

        var results = new List<InfraredHubAdvertisement>();
        foreach (var group in scan.Advertisements
            .Where(x => x.ApiVersion == BleInfraredHubProtocol.ApiVersion)
            .GroupBy(x => x.HubId.Value, StringComparer.Ordinal))
        {
            var deviceKeys = group.Select(x => x.DeviceKey).Distinct(StringComparer.Ordinal).ToArray();
            if (deviceKeys.Length != 1) continue; // stable hub id must not ambiguously resolve to two radios
            var selected = group.First();
            candidateCache.Set(new BleInfraredHubConnection(selected.HubId, selected.DeviceKey, selected.ApiVersion));
            results.Add(new InfraredHubAdvertisement(
                selected.HubId,
                selected.DisplayName,
                TransportKind,
                "hub.ble.discovered"));
        }

        return results.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
