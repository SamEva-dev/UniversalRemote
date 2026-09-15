using UniversalRemote.Abstractions;

namespace UniversalRemote.Hub.Wifi;

internal sealed class WifiInfraredHubTransport : IInfraredHubTransport
{
    private readonly IWifiInfraredHubConnectionStore connectionStore;
    private readonly WifiInfraredHubApiClient apiClient;
    private readonly WifiInfraredHubTransmitClient transmitClient;
    private readonly WifiInfraredHubLearnClient? learnClient;

    public WifiInfraredHubTransport(
        IWifiInfraredHubConnectionStore connectionStore,
        WifiInfraredHubApiClient apiClient,
        WifiInfraredHubTransmitClient transmitClient)
        : this(connectionStore, apiClient, transmitClient, null) { }

    public WifiInfraredHubTransport(
        IWifiInfraredHubConnectionStore connectionStore,
        WifiInfraredHubApiClient apiClient,
        WifiInfraredHubTransmitClient transmitClient,
        WifiInfraredHubLearnClient? learnClient)
    {
        this.connectionStore = connectionStore;
        this.apiClient = apiClient;
        this.transmitClient = transmitClient;
        this.learnClient = learnClient;
    }

    public string TransportId => "hub-wifi-v1";
    public InfraredHubTransportKind Kind => InfraredHubTransportKind.Wifi;

    public async ValueTask<InfraredHubInfo> GetInfoAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        var connection = await connectionStore.FindAsync(hubId, cancellationToken).ConfigureAwait(false);
        if (connection is null) return Offline(hubId, "hub.wifi.not_connected");

        var probe = await apiClient.ProbeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (probe.Outcome == WifiInfraredHubProbeOutcome.Success && probe.Info is not null) return probe.Info;

        return probe.Outcome switch
        {
            WifiInfraredHubProbeOutcome.IdentityMismatch => Faulted(hubId, "hub.wifi.identity_mismatch"),
            WifiInfraredHubProbeOutcome.UnsupportedApiVersion => Faulted(hubId, "hub.wifi.api_unsupported"),
            WifiInfraredHubProbeOutcome.InvalidResponse => Faulted(hubId, "hub.wifi.invalid_response"),
            _ => Offline(hubId, "hub.wifi.unreachable")
        };
    }

    public async Task<InfraredHubTransmitResult> TransmitAsync(
        InfraredHubId hubId,
        InfraredSignal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await connectionStore.FindAsync(hubId, cancellationToken).ConfigureAwait(false);
        if (connection is null) return InfraredHubTransmitResult.HubUnavailable();
        return await transmitClient.TransmitAsync(connection, signal, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InfraredHubLearnResult> LearnAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await connectionStore.FindAsync(hubId, cancellationToken).ConfigureAwait(false);
        if (connection is null) return InfraredHubLearnResult.Failed();
        if (connection.ApiVersion != WifiInfraredHubProtocol.ApiVersion) return InfraredHubLearnResult.Unsupported();
        if (learnClient is null) return InfraredHubLearnResult.Unsupported();

        var probe = await apiClient.ProbeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (probe.Outcome != WifiInfraredHubProbeOutcome.Success || probe.Info is null)
            return InfraredHubLearnResult.Failed();
        if (probe.Info.State != InfraredHubConnectionState.Ready) return InfraredHubLearnResult.Failed();
        if (!probe.Info.Capabilities.CanLearn) return InfraredHubLearnResult.Unsupported();
        return await learnClient.LearnAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static InfraredHubInfo Offline(InfraredHubId id, string diagnostic)
        => new(id, "Hub IR UniversalRemote", InfraredHubTransportKind.Wifi, InfraredHubConnectionState.Offline,
            new InfraredHubCapabilities(false, false, []), diagnostic);

    private static InfraredHubInfo Faulted(InfraredHubId id, string diagnostic)
        => new(id, "Hub IR UniversalRemote", InfraredHubTransportKind.Wifi, InfraredHubConnectionState.Faulted,
            new InfraredHubCapabilities(false, false, []), diagnostic);
}
