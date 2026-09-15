using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
namespace UniversalRemote.Provider.GenericUpnp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteGenericUpnp(this IServiceCollection services)
    {
        services.AddHttpClient<GenericUpnpRemoteProvider>(c => c.Timeout = TimeSpan.FromSeconds(4));
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<GenericUpnpRemoteProvider>());
        return services;
    }
}
