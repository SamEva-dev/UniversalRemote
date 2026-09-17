using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Epg;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteMediaEpg(this IServiceCollection services, string cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        services.AddHttpClient<XmlTvClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(35);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalRemote/1.0");
        }).RemoveAllLoggers();
        services.TryAddSingleton<XmlTvParser>();
        services.TryAddSingleton<EpgChannelMatcher>();
        services.TryAddScoped<EpgProviderResolver>();
        services.TryAddScoped<XmlTvEpgProvider>();
        services.TryAddScoped<IEpgProvider>(sp => sp.GetRequiredService<XmlTvEpgProvider>());
        services.TryAddSingleton<IEpgCache>(_ => new FileEpgCache(cacheDirectory));
        services.TryAddScoped<IEpgGuideService, EpgGuideService>();
        return services;
    }
}
