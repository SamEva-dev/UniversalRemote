using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
namespace UniversalRemote.Remote.Provider.Samsung;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteSamsung(this IServiceCollection services)
    {
        services.AddSingleton<SamsungPairingProvider>();
        services.AddSingleton<IDevicePairingProvider>(sp => sp.GetRequiredService<SamsungPairingProvider>());
        services.AddSingleton<SamsungRemoteProvider>();
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<SamsungRemoteProvider>());
        return services;
    }
}
