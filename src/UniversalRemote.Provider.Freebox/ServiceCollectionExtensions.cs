using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
namespace UniversalRemote.Remote.Provider.Freebox;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteFreebox(this IServiceCollection services)
    {
        services.AddHttpClient<FreeboxRemoteProvider>(c => c.Timeout = TimeSpan.FromSeconds(3))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<FreeboxRemoteProvider>());
        services.AddSingleton<FreeboxPairingProvider>();
        services.AddSingleton<IDevicePairingProvider>(sp => sp.GetRequiredService<FreeboxPairingProvider>());
        return services;
    }
}
