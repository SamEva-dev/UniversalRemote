using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Platform.Android;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteAndroidInfrared(this IServiceCollection services)
    {
        services.TryAddSingleton<IInfraredTransmitter, AndroidInfraredTransmitter>();
        return services;
    }
}
