namespace UniversalRemote.Abstractions;

public sealed record PairingProbe(
    string DisplayName,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> Services,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record PairingCandidate(string ProviderId, string DeviceKey, string DisplayName);

public sealed record PairingChallenge(
    Guid Id,
    string ProviderId,
    string DeviceKey,
    string DisplayName,
    string Prompt,
    int CodeLength);

public sealed record PairingCompletion(DeviceRoute Route, string DisplayName);

/// <summary>Provider-specific association behind a generic application contract.</summary>
public interface IDevicePairingProvider
{
    string Id { get; }
    PairingCandidate? Match(PairingProbe probe);
    Task<PairingChallenge> StartAsync(PairingCandidate candidate, CancellationToken cancellationToken);
    Task<PairingCompletion> CompleteAsync(Guid challengeId, string code, CancellationToken cancellationToken);
}

/// <summary>Optional generic entry point for providers that can safely onboard a device from a local address.</summary>
public interface IManualPairingProvider
{
    string Id { get; }
    PairingCandidate? CreateManualCandidate(string deviceKey, string? displayName = null);
}

/// <summary>Writes paired devices without forcing persistence technology into the Core.</summary>
public interface IDeviceRegistrar
{
    Task UpsertAsync(Device device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a completed pairing. Persistent registrars should preserve the existing Device.Id when the
    /// same provider/device key is paired again. The default implementation preserves compatibility for
    /// third-party registrars but cannot provide stable identity by itself.
    /// </summary>
    async Task<Device> RegisterPairingAsync(
        string displayName,
        DeviceRoute route,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(route);
        var device = new Device(Guid.NewGuid(), displayName, [route]);
        await UpsertAsync(device, cancellationToken).ConfigureAwait(false);
        return device;
    }
}
