using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
namespace UniversalRemote.Provider.Freebox;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteFreebox(this IServiceCollection services)
    {
        services.AddHttpClient<FreeboxRemoteProvider>(c => c.Timeout = TimeSpan.FromSeconds(3));
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<FreeboxRemoteProvider>());
        services.AddSingleton<FreeboxPairingProvider>();
        services.AddSingleton<IDevicePairingProvider>(sp => sp.GetRequiredService<FreeboxPairingProvider>());
        return services;
    }
}
