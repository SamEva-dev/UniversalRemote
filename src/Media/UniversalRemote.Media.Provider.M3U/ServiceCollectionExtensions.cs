using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.M3U;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the M3U/M3U8 catalogue parser, HTTP client, media provider and stream resolver.</summary>
    public static IServiceCollection AddUniversalRemoteM3uProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<M3uPlaylistParser>();
        services.AddHttpClient<M3uPlaylistClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalRemote/1.0");
        }).RemoveAllLoggers();
        services.AddScoped<M3uMediaProvider>();
        services.AddScoped<IMediaProvider>(sp => sp.GetRequiredService<M3uMediaProvider>());
        services.AddScoped<IStreamResolver, M3uStreamResolver>();
        return services;
    }
}
