using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Core;

/// <summary>Matches supported routes in device preference order without depending on concrete providers.</summary>
public sealed class ProviderResolver
{
    private readonly IReadOnlyDictionary<string, IRemoteProvider> providers;
    public ProviderResolver(IEnumerable<IRemoteProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var registry = new Dictionary<string, IRemoteProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(provider.Id);
            if (!registry.TryAdd(provider.Id, provider))
                throw new ArgumentException("Duplicate provider identifier.", nameof(providers));
        }
        this.providers = registry;
    }
    public (IRemoteProvider Provider, DeviceRoute Route)? Resolve(Device device, RemoteAction action)
    {
        foreach (var route in device.Routes)
            if (route.Capabilities.Contains(action) && providers.TryGetValue(route.ProviderId, out var provider))
                return (provider, route);
        return null;
    }
}
