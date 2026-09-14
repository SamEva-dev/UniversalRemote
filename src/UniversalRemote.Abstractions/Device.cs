using System.Collections.Frozen;

namespace UniversalRemote.Abstractions;

public enum SupportLevel { Experimental, Preview, Stable, Community }

/// <summary>A provider-specific route. Endpoint and pairing secrets belong in provider storage.</summary>
public sealed record DeviceRoute
{
    public string ProviderId { get; }
    public string DeviceKey { get; }
    public IReadOnlySet<RemoteAction> Capabilities { get; }
    public DeviceRoute(string providerId, string deviceKey, IEnumerable<RemoteAction> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentNullException.ThrowIfNull(capabilities);
        ProviderId = providerId;
        DeviceKey = deviceKey;
        var snapshot = capabilities.ToArray();
        if (snapshot.Any(a => a is null)) throw new ArgumentException("Null capability.", nameof(capabilities));
        Capabilities = snapshot.ToFrozenSet();
    }
}

/// <summary>Immutable snapshot of one device and its explicitly ordered routes.</summary>
public sealed class Device
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<DeviceRoute> Routes { get; }
    public IReadOnlySet<RemoteAction> Capabilities { get; }
    public Device(Guid id, string displayName, IEnumerable<DeviceRoute> routes)
    {
        if (id == Guid.Empty) throw new ArgumentException("Device ID must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(routes);
        var snapshot = routes.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(r => r is null))
            throw new ArgumentException("At least one valid route is required.", nameof(routes));
        if (snapshot.Select(r => (r.ProviderId, r.DeviceKey)).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Duplicate device route.", nameof(routes));
        Id = id;
        DisplayName = displayName;
        Routes = Array.AsReadOnly(snapshot);
        Capabilities = snapshot.SelectMany(r => r.Capabilities).ToFrozenSet();
    }
}
