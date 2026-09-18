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
        services.AddSingleton<IMediaSourceSetupProvider, M3uSourceSetupProvider>();
        services.AddHttpClient<M3uPlaylistClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalRemote/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
        {
            // Source URLs can contain credentials/tokens. Never forward them through implicit redirects.
            AllowAutoRedirect = false
        })
        .RemoveAllLoggers();
        services.AddScoped<M3uMediaProvider>();
        services.AddScoped<IMediaProvider>(sp => sp.GetRequiredService<M3uMediaProvider>());
        services.AddScoped<IStreamResolver, M3uStreamResolver>();
        return services;
    }
}
