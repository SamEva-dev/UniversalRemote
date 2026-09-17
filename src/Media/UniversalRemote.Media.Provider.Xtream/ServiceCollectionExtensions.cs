using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.Xtream;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteXtreamProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient<XtreamApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UniversalRemote/1.0");
        }).RemoveAllLoggers();
        services.AddScoped<XtreamMediaProvider>();
        services.AddScoped<IMediaProvider>(sp => sp.GetRequiredService<XtreamMediaProvider>());
        services.AddScoped<IMediaSeriesProvider>(sp => sp.GetRequiredService<XtreamMediaProvider>());
        services.AddScoped<IStreamResolver, XtreamStreamResolver>();
        return services;
    }
}
