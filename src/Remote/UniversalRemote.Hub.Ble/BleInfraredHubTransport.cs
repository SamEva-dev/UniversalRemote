using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub.Ble;

/// <summary>BLE implementation of the platform-neutral hub transport. Learning is completed by REMOTE-050.</summary>
internal sealed class BleInfraredHubTransport(
    IBleInfraredHubConnectionStore store,
    IBleInfraredHubRadio radio) : IInfraredHubTransport
{
    public string TransportId => "hub-ble-v1";
    public InfraredHubTransportKind Kind => InfraredHubTransportKind.BluetoothLowEnergy;

    public async ValueTask<InfraredHubInfo> GetInfoAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        var connection = await store.FindAsync(hubId, cancellationToken).ConfigureAwait(false);
        if (connection is null) return Offline(hubId, "hub.ble.not_connected");
        if (connection.ApiVersion != BleInfraredHubProtocol.ApiVersion) return Faulted(hubId, "hub.ble.api_unsupported");
        var probe = await radio.ProbeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (probe.Outcome != BleInfraredHubRadioProbeOutcome.Success)
            return probe.Outcome switch
            {
                BleInfraredHubRadioProbeOutcome.PermissionDenied => Offline(hubId, "hub.ble.permission_required"),
                BleInfraredHubRadioProbeOutcome.BluetoothOff => Offline(hubId, "hub.ble.bluetooth_off"),
                BleInfraredHubRadioProbeOutcome.InvalidResponse => Faulted(hubId, "hub.ble.invalid_response"),
                _ => Offline(hubId, "hub.ble.unreachable")
            };
        return BleInfraredHubProtocol.TryParseInfo(probe.InfoPayload.Span, hubId, out var info) && info is not null
            ? info
            : Faulted(hubId, "hub.ble.identity_or_info_invalid");
    }

    public async Task<InfraredHubTransmitResult> TransmitAsync(InfraredHubId hubId, InfraredSignal signal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await store.FindAsync(hubId, cancellationToken).ConfigureAwait(false);
        if (connection is null) return InfraredHubTransmitResult.HubUnavailable();
        if (connection.ApiVersion != BleInfraredHubProtocol.ApiVersion) return InfraredHubTransmitResult.FailedBeforeSend();

        byte[] request;
        var requestId = Guid.NewGuid();
        try { request = BleInfraredHubProtocol.EncodeTransmit(hubId, requestId, signal); }
        catch (ArgumentException) { return InfraredHubTransmitResult.InvalidSignal(); }
        catch (OverflowException) { return InfraredHubTransmitResult.InvalidSignal(); }

        var radioResult = await radio.TransmitAsync(connection, request, cancellationToken).ConfigureAwait(false);
        return radioResult.Outcome switch
        {
            BleInfraredHubRadioTransmitOutcome.HubUnavailable => InfraredHubTransmitResult.HubUnavailable(),
            BleInfraredHubRadioTransmitOutcome.FailedBeforeSend => InfraredHubTransmitResult.FailedBeforeSend(),
            BleInfraredHubRadioTransmitOutcome.Unknown => InfraredHubTransmitResult.Unknown(),
            BleInfraredHubRadioTransmitOutcome.Acknowledged when BleInfraredHubProtocol.TryParseAcknowledgement(
                radioResult.Acknowledgement.Span, hubId, requestId, out var parsed) => parsed,
            _ => InfraredHubTransmitResult.Unknown()
        };
    }

    public async Task<InfraredHubLearnResult> LearnAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await store.FindAsync(hubId, cancellationToken).ConfigureAwait(false);
        if (connection is null) return InfraredHubLearnResult.Failed();
        if (connection.ApiVersion != BleInfraredHubProtocol.ApiVersion) return InfraredHubLearnResult.Unsupported();

        var probe = await radio.ProbeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (probe.Outcome != BleInfraredHubRadioProbeOutcome.Success
            || !BleInfraredHubProtocol.TryParseInfo(probe.InfoPayload.Span, hubId, out var info)
            || info is null) return InfraredHubLearnResult.Failed();
        if (info.State != InfraredHubConnectionState.Ready) return InfraredHubLearnResult.Failed();
        if (!info.Capabilities.CanLearn) return InfraredHubLearnResult.Unsupported();

        var requestId = Guid.NewGuid();
        byte[] request;
        try { request = BleInfraredHubProtocol.EncodeLearnRequest(hubId, requestId); }
        catch (ArgumentException) { return InfraredHubLearnResult.Failed(); }
        var radioResult = await radio.LearnAsync(connection, request, cancellationToken).ConfigureAwait(false);
        if (radioResult.Outcome == BleInfraredHubRadioLearnOutcome.HubUnavailable) return InfraredHubLearnResult.Failed();
        if (radioResult.Outcome != BleInfraredHubRadioLearnOutcome.Result) return InfraredHubLearnResult.Failed();
        return BleInfraredHubProtocol.TryParseLearnResult(radioResult.Payload.Span, hubId, requestId, out var result)
            ? result
            : InfraredHubLearnResult.InvalidCapture();
    }

    private static InfraredHubInfo Offline(InfraredHubId id, string diagnostic)
        => new(id, "Hub IR UniversalRemote", InfraredHubTransportKind.BluetoothLowEnergy, InfraredHubConnectionState.Offline,
            new InfraredHubCapabilities(false, false, []), diagnostic);
    private static InfraredHubInfo Faulted(InfraredHubId id, string diagnostic)
        => new(id, "Hub IR UniversalRemote", InfraredHubTransportKind.BluetoothLowEnergy, InfraredHubConnectionState.Faulted,
            new InfraredHubCapabilities(false, false, []), diagnostic);
}
