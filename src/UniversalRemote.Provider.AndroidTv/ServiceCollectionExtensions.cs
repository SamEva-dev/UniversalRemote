using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.AndroidTv;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteAndroidTv(this IServiceCollection services)
    {
        services.AddSingleton<AndroidTvPairingProvider>();
        services.AddSingleton<IDevicePairingProvider>(sp => sp.GetRequiredService<AndroidTvPairingProvider>());
        services.AddSingleton<AndroidTvRemoteProvider>();
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<AndroidTvRemoteProvider>());
        return services;
    }
}
