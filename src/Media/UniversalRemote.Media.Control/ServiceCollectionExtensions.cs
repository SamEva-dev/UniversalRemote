using Microsoft.Extensions.DependencyInjection;

namespace UniversalRemote.Media.Control;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteMediaControl(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IMediaRemoteControl, MediaRemoteControl>();
        return services;
    }
}
