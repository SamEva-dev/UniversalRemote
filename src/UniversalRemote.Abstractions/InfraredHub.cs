namespace UniversalRemote.Remote.Abstractions;

/// <summary>Stable, non-secret identifier assigned to an external infrared hub.</summary>
public readonly record struct InfraredHubId
{
    public string Value { get; }

    public InfraredHubId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(value), "Hub identifiers are limited to 128 characters.");
        Value = normalized;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Physical link used to reach an external hub. The hardware/protocol remains replaceable.</summary>
public enum InfraredHubTransportKind
{
    Wifi,
    BluetoothLowEnergy
}

public enum InfraredHubConnectionState
{
    Offline,
    Ready,
    Busy,
    Faulted
}

/// <summary>Sanitized capabilities advertised by a hub. Secrets and raw endpoints do not belong here.</summary>
public sealed class InfraredHubCapabilities
{
    public bool CanTransmit { get; }
    public bool CanLearn { get; }
    public IReadOnlyList<InfraredFrequencyRange> CarrierFrequencies { get; }
    public int MaxPatternValues { get; }
    public int MaxTotalDurationMicroseconds { get; }

    public InfraredHubCapabilities(
        bool canTransmit,
        bool canLearn,
        IEnumerable<InfraredFrequencyRange>? carrierFrequencies,
        int maxPatternValues = InfraredSignalLimits.MaxPatternValues,
        int maxTotalDurationMicroseconds = InfraredSignalLimits.MaxTotalDurationMicroseconds)
    {
        if (maxPatternValues is <= 0 or > InfraredSignalLimits.MaxPatternValues)
            throw new ArgumentOutOfRangeException(nameof(maxPatternValues));
        if (maxTotalDurationMicroseconds is <= 0 or > InfraredSignalLimits.MaxTotalDurationMicroseconds)
            throw new ArgumentOutOfRangeException(nameof(maxTotalDurationMicroseconds));

        var frequencies = (carrierFrequencies ?? []).ToArray();
        if (frequencies.Any(range => range.MinHz < InfraredSignalLimits.MinPlausibleCarrierFrequencyHz
            || range.MaxHz > InfraredSignalLimits.MaxPlausibleCarrierFrequencyHz))
            throw new ArgumentException("Hub carrier-frequency ranges must stay inside the shared plausible IR limits.", nameof(carrierFrequencies));
        if (canTransmit && frequencies.Length == 0)
            throw new ArgumentException("A transmitting hub must advertise at least one carrier-frequency range.", nameof(carrierFrequencies));

        CanTransmit = canTransmit;
        CanLearn = canLearn;
        CarrierFrequencies = Array.AsReadOnly(frequencies);
        MaxPatternValues = maxPatternValues;
        MaxTotalDurationMicroseconds = maxTotalDurationMicroseconds;
    }

    public bool SupportsFrequency(int frequencyHz)
        => CanTransmit && CarrierFrequencies.Any(range => range.Contains(frequencyHz));

    public bool Accepts(InfraredSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return SupportsFrequency(signal.CarrierFrequencyHz)
            && signal.PatternMicroseconds.Count <= MaxPatternValues
            && signal.TotalDurationMicroseconds < MaxTotalDurationMicroseconds;
    }
}

/// <summary>Product-safe snapshot of one hub; no IP address, BLE address or credential is exposed.</summary>
public sealed class InfraredHubInfo
{
    public InfraredHubId Id { get; }
    public string DisplayName { get; }
    public InfraredHubTransportKind TransportKind { get; }
    public InfraredHubConnectionState State { get; }
    public string? FirmwareVersion { get; }
    public string DiagnosticCode { get; }
    public InfraredHubCapabilities Capabilities { get; }

    public InfraredHubInfo(
        InfraredHubId id,
        string displayName,
        InfraredHubTransportKind transportKind,
        InfraredHubConnectionState state,
        InfraredHubCapabilities capabilities,
        string diagnosticCode,
        string? firmwareVersion = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("Hub ID must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);

        Id = id;
        DisplayName = displayName.Trim();
        TransportKind = transportKind;
        State = state;
        Capabilities = capabilities;
        DiagnosticCode = diagnosticCode.Trim();
        FirmwareVersion = string.IsNullOrWhiteSpace(firmwareVersion) ? null : firmwareVersion.Trim();
    }
}

/// <summary>Validated raw infrared signal shared by native emitters and external hubs.</summary>
public sealed class InfraredSignal
{
    public int CarrierFrequencyHz { get; }
    public IReadOnlyList<int> PatternMicroseconds { get; }
    public int TotalDurationMicroseconds { get; }

    public InfraredSignal(int carrierFrequencyHz, IEnumerable<int> patternMicroseconds)
    {
        ArgumentNullException.ThrowIfNull(patternMicroseconds);
        var snapshot = patternMicroseconds.ToArray();
        if (!InfraredSignalLimits.IsCarrierFrequencyPlausible(carrierFrequencyHz))
            throw new ArgumentOutOfRangeException(nameof(carrierFrequencyHz));
        if (!InfraredSignalLimits.IsPatternValid(snapshot))
            throw new ArgumentException("Invalid infrared pattern.", nameof(patternMicroseconds));

        CarrierFrequencyHz = carrierFrequencyHz;
        PatternMicroseconds = Array.AsReadOnly(snapshot);
        TotalDurationMicroseconds = checked(snapshot.Sum());
    }
}

public enum InfraredHubTransmitOutcome
{
    Accepted,
    HubUnavailable,
    FrequencyUnsupported,
    InvalidSignal,
    FailedBeforeSend,
    Unknown
}

/// <summary>
/// Accepted means the hub accepted the transmission request; it is not proof that the appliance reacted.
/// Unknown is intentionally non-retryable because the hub may already have emitted the signal.
/// </summary>
public readonly record struct InfraredHubTransmitResult(InfraredHubTransmitOutcome Outcome)
{
    public static InfraredHubTransmitResult Accepted() => new(InfraredHubTransmitOutcome.Accepted);
    public static InfraredHubTransmitResult HubUnavailable() => new(InfraredHubTransmitOutcome.HubUnavailable);
    public static InfraredHubTransmitResult FrequencyUnsupported() => new(InfraredHubTransmitOutcome.FrequencyUnsupported);
    public static InfraredHubTransmitResult InvalidSignal() => new(InfraredHubTransmitOutcome.InvalidSignal);
    public static InfraredHubTransmitResult FailedBeforeSend() => new(InfraredHubTransmitOutcome.FailedBeforeSend);
    public static InfraredHubTransmitResult Unknown() => new(InfraredHubTransmitOutcome.Unknown);
}

public enum InfraredHubLearnOutcome
{
    Captured,
    Timeout,
    Unsupported,
    InvalidCapture,
    Failed
}

/// <summary>Result of a learning attempt. A captured result always contains a validated signal.</summary>
public sealed class InfraredHubLearnResult
{
    public InfraredHubLearnOutcome Outcome { get; }
    public InfraredSignal? Signal { get; }

    private InfraredHubLearnResult(InfraredHubLearnOutcome outcome, InfraredSignal? signal)
    {
        if (outcome == InfraredHubLearnOutcome.Captured && signal is null)
            throw new ArgumentException("A captured learning result requires a signal.", nameof(signal));
        if (outcome != InfraredHubLearnOutcome.Captured && signal is not null)
            throw new ArgumentException("Only a captured learning result may contain a signal.", nameof(signal));

        Outcome = outcome;
        Signal = signal;
    }

    public static InfraredHubLearnResult Captured(InfraredSignal signal)
        => new(InfraredHubLearnOutcome.Captured, signal ?? throw new ArgumentNullException(nameof(signal)));
    public static InfraredHubLearnResult Timeout() => new(InfraredHubLearnOutcome.Timeout, null);
    public static InfraredHubLearnResult Unsupported() => new(InfraredHubLearnOutcome.Unsupported, null);
    public static InfraredHubLearnResult InvalidCapture() => new(InfraredHubLearnOutcome.InvalidCapture, null);
    public static InfraredHubLearnResult Failed() => new(InfraredHubLearnOutcome.Failed, null);
}

/// <summary>Sanitized discovery result. Endpoint details remain private to the transport implementation.</summary>
public sealed class InfraredHubAdvertisement
{
    public InfraredHubId Id { get; }
    public string DisplayName { get; }
    public InfraredHubTransportKind TransportKind { get; }
    public string DiagnosticCode { get; }
    public string? FirmwareVersion { get; }

    public InfraredHubAdvertisement(
        InfraredHubId id,
        string displayName,
        InfraredHubTransportKind transportKind,
        string diagnosticCode,
        string? firmwareVersion = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value)) throw new ArgumentException("Hub ID must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        Id = id;
        DisplayName = displayName.Trim();
        TransportKind = transportKind;
        DiagnosticCode = diagnosticCode.Trim();
        FirmwareVersion = string.IsNullOrWhiteSpace(firmwareVersion) ? null : firmwareVersion.Trim();
    }
}

public enum InfraredHubConnectOutcome
{
    Connected,
    NotFound,
    Unreachable,
    IdentityMismatch,
    UnsupportedApiVersion,
    InvalidResponse
}

/// <summary>Sanitized result of validating and persisting one transport connection.</summary>
public sealed class InfraredHubConnectResult
{
    public InfraredHubConnectOutcome Outcome { get; }
    public string DiagnosticCode { get; }
    public IInfraredHub? Hub { get; }
    public InfraredHubInfo? Info { get; }

    private InfraredHubConnectResult(
        InfraredHubConnectOutcome outcome,
        string diagnosticCode,
        IInfraredHub? hub,
        InfraredHubInfo? info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        if (outcome == InfraredHubConnectOutcome.Connected && (hub is null || info is null))
            throw new ArgumentException("A connected result requires both a hub client and hub info.");
        if (outcome != InfraredHubConnectOutcome.Connected && (hub is not null || info is not null))
            throw new ArgumentException("A failed connection result must not contain a hub client or hub info.");

        Outcome = outcome;
        DiagnosticCode = diagnosticCode.Trim();
        Hub = hub;
        Info = info;
    }

    public static InfraredHubConnectResult Connected(IInfraredHub hub, InfraredHubInfo info, string diagnosticCode)
        => new(InfraredHubConnectOutcome.Connected, diagnosticCode,
            hub ?? throw new ArgumentNullException(nameof(hub)),
            info ?? throw new ArgumentNullException(nameof(info)));

    public static InfraredHubConnectResult Failure(InfraredHubConnectOutcome outcome, string diagnosticCode)
    {
        if (outcome == InfraredHubConnectOutcome.Connected) throw new ArgumentOutOfRangeException(nameof(outcome));
        return new InfraredHubConnectResult(outcome, diagnosticCode, null, null);
    }
}

/// <summary>Transport-neutral discovery. Wi-Fi and BLE can be listed together without exposing their endpoints.</summary>
public interface IInfraredHubDiscovery
{
    InfraredHubTransportKind TransportKind { get; }
    Task<IReadOnlyList<InfraredHubAdvertisement>> DiscoverAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Transport-neutral connection lifecycle. The transport owns endpoint and credential persistence.</summary>
public interface IInfraredHubConnector
{
    InfraredHubTransportKind TransportKind { get; }
    Task<InfraredHubConnectResult> ConnectAsync(InfraredHubId hubId, CancellationToken cancellationToken = default);
    Task ForgetAsync(InfraredHubId hubId, CancellationToken cancellationToken = default);
}

/// <summary>Stable user choice of the external hub used for Generic IR fallback.</summary>
public readonly record struct InfraredHubSelection
{
    public InfraredHubId HubId { get; }
    public InfraredHubTransportKind TransportKind { get; }

    public InfraredHubSelection(InfraredHubId hubId, InfraredHubTransportKind transportKind)
    {
        if (string.IsNullOrWhiteSpace(hubId.Value))
            throw new ArgumentException("Hub ID must not be empty.", nameof(hubId));
        HubId = hubId;
        TransportKind = transportKind;
    }
}

/// <summary>Persistence seam for the selected hub. Endpoint details remain owned by each transport store.</summary>
public interface IInfraredHubSelectionStore
{
    ValueTask<InfraredHubSelection?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(InfraredHubSelection selection, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolves the current selection into a transport-neutral hub facade.</summary>
public interface IInfraredHubSelectionAccessor
{
    ValueTask<IInfraredHub?> GetSelectedHubAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Hardware-independent hub facade used by the SDK. Implementations must never expose transport credentials.
/// </summary>
public interface IInfraredHub
{
    InfraredHubId Id { get; }
    InfraredHubTransportKind TransportKind { get; }
    ValueTask<InfraredHubInfo> GetInfoAsync(CancellationToken cancellationToken = default);
    Task<InfraredHubTransmitResult> TransmitAsync(InfraredSignal signal, CancellationToken cancellationToken = default);
    Task<InfraredHubLearnResult> LearnAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Replaceable low-level link to an external hub. Wi-Fi and BLE implementations share this contract.
/// The transport owns endpoint/credential handling and receives only the stable hub identifier from higher layers.
/// </summary>
public interface IInfraredHubTransport
{
    string TransportId { get; }
    InfraredHubTransportKind Kind { get; }
    ValueTask<InfraredHubInfo> GetInfoAsync(InfraredHubId hubId, CancellationToken cancellationToken = default);
    Task<InfraredHubTransmitResult> TransmitAsync(InfraredHubId hubId, InfraredSignal signal, CancellationToken cancellationToken = default);
    Task<InfraredHubLearnResult> LearnAsync(InfraredHubId hubId, CancellationToken cancellationToken = default);
}
