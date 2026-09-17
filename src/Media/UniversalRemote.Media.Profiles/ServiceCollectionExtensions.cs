using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Profiles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteMediaProfiles(this IServiceCollection services, string profileFilePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileFilePath);
        services.AddSingleton<IMediaProfileRepository>(new FileMediaProfileRepository(profileFilePath));
        services.AddSingleton<IMediaProfileService, MediaProfileService>();
        services.RemoveAll<IMediaAccessPolicy>();
        services.AddSingleton<IMediaAccessPolicy, MediaProfileAccessPolicy>();
        return services;
    }
}
