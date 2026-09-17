using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Playback;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteMediaPlayback(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPlaybackCheckpointStore, InMemoryPlaybackCheckpointStore>();
        services.TryAddScoped<IPlaybackService, PlaybackService>();
        return services;
    }
}
