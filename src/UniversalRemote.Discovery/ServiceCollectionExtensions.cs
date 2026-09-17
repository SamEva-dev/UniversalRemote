using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UniversalRemote.Remote.Discovery;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteDiscovery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDiscoveryConsolidator, DiscoveryConsolidator>();
        services.TryAddSingleton<IDiscoveryNetworkLease, NoopDiscoveryNetworkLease>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDiscoverySource, MdnsDiscoverySource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDiscoverySource, SsdpDiscoverySource>());
        services.TryAddTransient<IDeviceDiscovery, DeviceDiscovery>();
        return services;
    }
}
