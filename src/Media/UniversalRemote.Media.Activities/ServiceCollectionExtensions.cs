using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UniversalRemote.Media.Activities;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteMediaActivities(this IServiceCollection services, string persistencePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(persistencePath);
        services.TryAddSingleton<IMediaActivityRepository>(_ => new FileMediaActivityRepository(persistencePath));
        services.TryAddScoped<IMediaActivityRunner, MediaActivityRunner>();
        return services;
    }
}
