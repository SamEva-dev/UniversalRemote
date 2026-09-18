using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
namespace UniversalRemote.Remote.Provider.LG;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteLg(this IServiceCollection services)
    {
        services.AddSingleton<LgPairingProvider>(); services.AddSingleton<IDevicePairingProvider>(sp => sp.GetRequiredService<LgPairingProvider>());
        services.AddSingleton<LgRemoteProvider>(); services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<LgRemoteProvider>());
        return services;
    }
}
