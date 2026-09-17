using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the provider-independent Media foundation. Concrete providers are registered by their own packages.
    /// </summary>
    public static IServiceCollection AddUniversalRemoteMediaCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IMediaSourceRepository, InMemoryMediaSourceRepository>();
        services.TryAddScoped<MediaProviderResolver>();
        services.TryAddScoped<IMediaCatalog, MediaCatalog>();
        services.TryAddSingleton<IMediaFavoriteRepository, InMemoryMediaFavoriteRepository>();
        services.TryAddSingleton<IMediaHistoryRepository, InMemoryMediaHistoryRepository>();
        services.TryAddSingleton<IMediaAccessPolicy, AllowAllMediaAccessPolicy>();
        services.TryAddScoped<IMediaBrowseService, MediaBrowseService>();
        services.TryAddScoped<IMediaSearchService, MediaSearchService>();
        services.TryAddScoped<IMediaLibraryService, MediaLibraryService>();
        services.TryAddScoped<IMediaSeriesService, MediaSeriesService>();
        return services;
    }
}
