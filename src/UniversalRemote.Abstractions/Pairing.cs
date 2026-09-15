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

/// <summary>Writes paired devices without forcing persistence technology into the Core.</summary>
public interface IDeviceRegistrar
{
    Task UpsertAsync(Device device, CancellationToken cancellationToken = default);
}
