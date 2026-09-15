using UniversalRemote.Abstractions;

namespace UniversalRemote.Hub.Ble;

public enum BleInfraredHubRadioScanOutcome
{
    Success,
    Unsupported,
    PermissionDenied,
    BluetoothOff,
    Failed
}

/// <summary>Transport-private radio advertisement. DeviceKey is never promoted to product-facing hub models.</summary>
public sealed class BleInfraredHubRadioAdvertisement
{
    public InfraredHubId HubId { get; }
    public string DeviceKey { get; }
    public string DisplayName { get; }
    public int ApiVersion { get; }

    public BleInfraredHubRadioAdvertisement(InfraredHubId hubId, string deviceKey, string displayName, int apiVersion)
    {
        if (string.IsNullOrWhiteSpace(hubId.Value)) throw new ArgumentException("Hub ID must not be empty.", nameof(hubId));
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (deviceKey.Length > 256) throw new ArgumentOutOfRangeException(nameof(deviceKey));
        if (apiVersion <= 0) throw new ArgumentOutOfRangeException(nameof(apiVersion));
        HubId = hubId;
        DeviceKey = deviceKey.Trim();
        DisplayName = displayName.Trim();
        ApiVersion = apiVersion;
    }
}

public sealed class BleInfraredHubRadioScanResult
{
    public BleInfraredHubRadioScanOutcome Outcome { get; }
    public IReadOnlyList<BleInfraredHubRadioAdvertisement> Advertisements { get; }
    public string DiagnosticCode { get; }

    public BleInfraredHubRadioScanResult(BleInfraredHubRadioScanOutcome outcome, IEnumerable<BleInfraredHubRadioAdvertisement>? advertisements, string diagnosticCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        Outcome = outcome;
        Advertisements = Array.AsReadOnly((advertisements ?? []).ToArray());
        DiagnosticCode = diagnosticCode.Trim();
    }
}

public enum BleInfraredHubRadioProbeOutcome
{
    Success,
    Unreachable,
    PermissionDenied,
    BluetoothOff,
    InvalidResponse
}

public sealed class BleInfraredHubRadioProbeResult
{
    public BleInfraredHubRadioProbeOutcome Outcome { get; }
    public ReadOnlyMemory<byte> InfoPayload { get; }
    public string DiagnosticCode { get; }

    public BleInfraredHubRadioProbeResult(BleInfraredHubRadioProbeOutcome outcome, ReadOnlyMemory<byte> infoPayload, string diagnosticCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        if (outcome != BleInfraredHubRadioProbeOutcome.Success && !infoPayload.IsEmpty)
            throw new ArgumentException("Failed BLE probes must not expose partial info payloads.", nameof(infoPayload));
        Outcome = outcome;
        InfoPayload = infoPayload;
        DiagnosticCode = diagnosticCode.Trim();
    }
}

public enum BleInfraredHubRadioTransmitOutcome
{
    Acknowledged,
    HubUnavailable,
    FailedBeforeSend,
    Unknown
}

public sealed class BleInfraredHubRadioTransmitResult
{
    public BleInfraredHubRadioTransmitOutcome Outcome { get; }
    public ReadOnlyMemory<byte> Acknowledgement { get; }

    public BleInfraredHubRadioTransmitResult(BleInfraredHubRadioTransmitOutcome outcome, ReadOnlyMemory<byte> acknowledgement = default)
    {
        if (outcome == BleInfraredHubRadioTransmitOutcome.Acknowledged && acknowledgement.IsEmpty)
            throw new ArgumentException("An acknowledged BLE send requires an acknowledgement payload.", nameof(acknowledgement));
        if (outcome != BleInfraredHubRadioTransmitOutcome.Acknowledged && !acknowledgement.IsEmpty)
            throw new ArgumentException("Only an acknowledged BLE send may expose a payload.", nameof(acknowledgement));
        Outcome = outcome;
        Acknowledgement = acknowledgement;
    }
}

public enum BleInfraredHubRadioLearnOutcome
{
    Result,
    HubUnavailable,
    Failed
}

public sealed class BleInfraredHubRadioLearnResult
{
    public BleInfraredHubRadioLearnOutcome Outcome { get; }
    public ReadOnlyMemory<byte> Payload { get; }

    public BleInfraredHubRadioLearnResult(BleInfraredHubRadioLearnOutcome outcome, ReadOnlyMemory<byte> payload = default)
    {
        if (outcome == BleInfraredHubRadioLearnOutcome.Result && payload.IsEmpty)
            throw new ArgumentException("A BLE learning result requires a payload.", nameof(payload));
        if (outcome != BleInfraredHubRadioLearnOutcome.Result && !payload.IsEmpty)
            throw new ArgumentException("Failed BLE learning must not expose a payload.", nameof(payload));
        Outcome = outcome;
        Payload = payload;
    }
}

/// <summary>Platform seam for BLE scanning and GATT I/O. Endpoint/device addresses stay below this boundary.</summary>
public interface IBleInfraredHubRadio
{
    Task<BleInfraredHubRadioScanResult> ScanAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<BleInfraredHubRadioProbeResult> ProbeAsync(BleInfraredHubConnection connection, CancellationToken cancellationToken = default);
    Task<BleInfraredHubRadioTransmitResult> TransmitAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default);
    Task<BleInfraredHubRadioLearnResult> LearnAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default);
}

internal sealed class UnsupportedBleInfraredHubRadio : IBleInfraredHubRadio
{
    public Task<BleInfraredHubRadioScanResult> ScanAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => Task.FromResult(new BleInfraredHubRadioScanResult(BleInfraredHubRadioScanOutcome.Unsupported, [], "hub.ble.platform_unsupported"));

    public Task<BleInfraredHubRadioProbeResult> ProbeAsync(BleInfraredHubConnection connection, CancellationToken cancellationToken = default)
        => Task.FromResult(new BleInfraredHubRadioProbeResult(BleInfraredHubRadioProbeOutcome.Unreachable, default, "hub.ble.platform_unsupported"));

    public Task<BleInfraredHubRadioTransmitResult> TransmitAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
        => Task.FromResult(new BleInfraredHubRadioTransmitResult(BleInfraredHubRadioTransmitOutcome.HubUnavailable));

    public Task<BleInfraredHubRadioLearnResult> LearnAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
        => Task.FromResult(new BleInfraredHubRadioLearnResult(BleInfraredHubRadioLearnOutcome.HubUnavailable));
}
