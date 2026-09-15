using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Platform.Android;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteAndroidInfrared(this IServiceCollection services)
    {
        services.TryAddSingleton<AndroidInfraredTransmitter>();
        services.TryAddSingleton<IOnDeviceInfraredTransmitter>(sp => sp.GetRequiredService<AndroidInfraredTransmitter>());
        // Backward-compatible default for the native Android IR sample. Product hosts may replace IInfraredTransmitter
        // with AdaptiveInfraredTransmitter after registering external hub transports.
        services.TryAddSingleton<IInfraredTransmitter>(sp => sp.GetRequiredService<AndroidInfraredTransmitter>());
        return services;
    }

    public static IServiceCollection AddUniversalRemoteAndroidBleHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<UniversalRemote.Hub.Ble.IBleInfraredHubRadio>();
        services.AddSingleton<UniversalRemote.Hub.Ble.IBleInfraredHubRadio, AndroidBleInfraredHubRadio>();
        return services;
    }
}
