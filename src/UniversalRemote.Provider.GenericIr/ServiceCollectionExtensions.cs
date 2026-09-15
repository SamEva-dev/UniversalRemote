using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.GenericIr;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteGenericIr(this IServiceCollection services)
    {
        services.TryAddSingleton<IIrProfileCatalog>(_ => new InMemoryIrProfileCatalog([BuiltInIrProfiles.BenchSyntheticTv]));
        services.AddSingleton<GenericIrRemoteProvider>();
        services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<GenericIrRemoteProvider>());
        return services;
    }
}
