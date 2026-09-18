using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
namespace UniversalRemote.Remote.Provider.GenericUpnp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteGenericUpnp(this IServiceCollection services)
    {
        services.AddHttpClient<GenericUpnpRemoteProvider>(c => c.Timeout = TimeSpan.FromSeconds(4));
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<GenericUpnpRemoteProvider>());
        return services;
    }
}
