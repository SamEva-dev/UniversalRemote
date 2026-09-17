using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Hub;

namespace UniversalRemote.Remote.Hub.Ble;

internal sealed class BleInfraredHubConnector(
    IBleInfraredHubConnectionStore store,
    BleInfraredHubCandidateCache candidateCache,
    IBleInfraredHubRadio radio,
    BleInfraredHubTransport transport) : IInfraredHubConnector
{
    public InfraredHubTransportKind TransportKind => InfraredHubTransportKind.BluetoothLowEnergy;

    public async Task<InfraredHubConnectResult> ConnectAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BleInfraredHubConnection? connection = null;
        if (!candidateCache.TryGet(hubId, out connection))
            connection = await store.FindAsync(hubId, cancellationToken).ConfigureAwait(false);
        if (connection is null)
            return InfraredHubConnectResult.Failure(InfraredHubConnectOutcome.NotFound, "hub.ble.not_found");
        if (connection.ApiVersion != BleInfraredHubProtocol.ApiVersion)
            return InfraredHubConnectResult.Failure(InfraredHubConnectOutcome.UnsupportedApiVersion, "hub.ble.api_unsupported");

        var probe = await radio.ProbeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (probe.Outcome != BleInfraredHubRadioProbeOutcome.Success)
        {
            var mapped = probe.Outcome == BleInfraredHubRadioProbeOutcome.InvalidResponse
                ? InfraredHubConnectOutcome.InvalidResponse
                : InfraredHubConnectOutcome.Unreachable;
            return InfraredHubConnectResult.Failure(mapped, probe.DiagnosticCode);
        }
        if (!BleInfraredHubProtocol.TryParseInfo(probe.InfoPayload.Span, hubId, out var info) || info is null)
            return InfraredHubConnectResult.Failure(InfraredHubConnectOutcome.IdentityMismatch, "hub.ble.identity_or_info_invalid");

        await store.SaveAsync(connection, cancellationToken).ConfigureAwait(false);
        candidateCache.Set(connection);
        var client = new InfraredHubClient(hubId, transport);
        return InfraredHubConnectResult.Connected(client, info, "hub.ble.connected");
    }

    public async Task ForgetAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        candidateCache.Remove(hubId);
        await store.RemoveAsync(hubId, cancellationToken).ConfigureAwait(false);
    }
}
