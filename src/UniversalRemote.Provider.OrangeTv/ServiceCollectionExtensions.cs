using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.OrangeTv;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteOrangeTv(this IServiceCollection services)
    {
        services.AddHttpClient<OrangeTvRemoteProvider>(client => client.Timeout = TimeSpan.FromSeconds(3))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<OrangeTvRemoteProvider>());

        services.AddHttpClient<OrangeTvPairingProvider>(client => client.Timeout = TimeSpan.FromSeconds(3))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IDevicePairingProvider>(sp => sp.GetRequiredService<OrangeTvPairingProvider>());
        services.AddSingleton<IManualPairingProvider>(sp => sp.GetRequiredService<OrangeTvPairingProvider>());
        return services;
    }
}
