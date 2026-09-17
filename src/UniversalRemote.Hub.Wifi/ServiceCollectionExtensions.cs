using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub.Wifi;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteInfraredHubWifi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IWifiInfraredHubConnectionStore, InMemoryWifiInfraredHubConnectionStore>();
        services.TryAddSingleton<WifiInfraredHubCandidateCache>();

        services.AddHttpClient<WifiInfraredHubApiClient>(client => client.Timeout = TimeSpan.FromSeconds(4))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            });

        services.AddHttpClient<WifiInfraredHubTransmitClient>(client => client.Timeout = TimeSpan.FromSeconds(4))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            });

        services.AddHttpClient<WifiInfraredHubLearnClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            });

        services.TryAddTransient<WifiInfraredHubTransport>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IInfraredHubTransport, WifiInfraredHubTransport>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IInfraredHubDiscovery, WifiInfraredHubDiscovery>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IInfraredHubConnector, WifiInfraredHubConnector>());
        return services;
    }
}
