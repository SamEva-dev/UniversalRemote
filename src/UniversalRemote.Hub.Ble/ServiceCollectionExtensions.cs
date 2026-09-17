using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub.Ble;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteInfraredHubBle(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IBleInfraredHubRadio, UnsupportedBleInfraredHubRadio>();
        services.TryAddSingleton<IBleInfraredHubConnectionStore, InMemoryBleInfraredHubConnectionStore>();
        services.TryAddSingleton<BleInfraredHubCandidateCache>();
        services.TryAddTransient<BleInfraredHubTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IInfraredHubTransport, BleInfraredHubTransport>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IInfraredHubDiscovery, BleInfraredHubDiscovery>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IInfraredHubConnector, BleInfraredHubConnector>());
        return services;
    }
}
