using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Provider.AndroidTv;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteAndroidTv(this IServiceCollection services)
    {
        services.AddSingleton<AndroidTvPairingProvider>();
        services.AddSingleton<IDevicePairingProvider>(sp => sp.GetRequiredService<AndroidTvPairingProvider>());
        services.AddSingleton<IManualPairingProvider>(sp => sp.GetRequiredService<AndroidTvPairingProvider>());
        services.AddSingleton<AndroidTvRemoteProvider>();
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<AndroidTvRemoteProvider>());
        return services;
    }
}
