using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Targets;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteMediaTargets(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPlaybackTargetDiscovery, PlaybackTargetDiscovery>();
        services.AddSingleton<IPlaybackTargetSelection, PlaybackTargetSelection>();
        return services;
    }
}
