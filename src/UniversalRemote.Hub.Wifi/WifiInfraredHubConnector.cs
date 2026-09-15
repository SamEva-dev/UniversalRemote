using UniversalRemote.Abstractions;
using UniversalRemote.Hub;

namespace UniversalRemote.Hub.Wifi;

/// <summary>Validates a discovered/stored endpoint before making it persistent and returning an IInfraredHub client.</summary>
internal sealed class WifiInfraredHubConnector(
    WifiInfraredHubCandidateCache candidateCache,
    IWifiInfraredHubConnectionStore connectionStore,
    WifiInfraredHubApiClient apiClient,
    WifiInfraredHubTransport transport) : IInfraredHubConnector
{
    public InfraredHubTransportKind TransportKind => InfraredHubTransportKind.Wifi;

    public async Task<InfraredHubConnectResult> ConnectAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        WifiInfraredHubConnection? connection = null;
        if (!candidateCache.TryGet(hubId, out connection) || connection is null)
            connection = await connectionStore.FindAsync(hubId, cancellationToken).ConfigureAwait(false);
        if (connection is null)
            return InfraredHubConnectResult.Failure(InfraredHubConnectOutcome.NotFound, "hub.wifi.not_found");

        var probe = await apiClient.ProbeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (probe.Outcome != WifiInfraredHubProbeOutcome.Success || probe.Info is null)
        {
            return probe.Outcome switch
            {
                WifiInfraredHubProbeOutcome.IdentityMismatch => InfraredHubConnectResult.Failure(InfraredHubConnectOutcome.IdentityMismatch, "hub.wifi.identity_mismatch"),
                WifiInfraredHubProbeOutcome.UnsupportedApiVersion => InfraredHubConnectResult.Failure(InfraredHubConnectOutcome.UnsupportedApiVersion, "hub.wifi.api_unsupported"),
                WifiInfraredHubProbeOutcome.InvalidResponse => InfraredHubConnectResult.Failure(InfraredHubConnectOutcome.InvalidResponse, "hub.wifi.invalid_response"),
                _ => InfraredHubConnectResult.Failure(InfraredHubConnectOutcome.Unreachable, "hub.wifi.unreachable")
            };
        }

        await connectionStore.SaveAsync(connection, cancellationToken).ConfigureAwait(false);
        candidateCache.Remove(hubId);
        var hub = new InfraredHubClient(hubId, transport);
        return InfraredHubConnectResult.Connected(hub, probe.Info, "hub.wifi.connected");
    }

    public async Task ForgetAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        candidateCache.Remove(hubId);
        await connectionStore.RemoveAsync(hubId, cancellationToken).ConfigureAwait(false);
    }
}
