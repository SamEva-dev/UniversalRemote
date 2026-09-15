using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.GenericIr;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteGenericIr(this IServiceCollection services, string? profileDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(profileDirectory))
            services.TryAddSingleton<IIrProfileCatalog>(_ => new InMemoryIrProfileCatalog([BuiltInIrProfiles.BenchSyntheticTv]));
        else
            services.TryAddSingleton<IIrProfileCatalog>(_ => new FileIrProfileCatalog(profileDirectory, [BuiltInIrProfiles.BenchSyntheticTv]));

        // Keeps GenericIr independently consumable in tests/samples. Product hosts register the real selection accessor first.
        services.TryAddSingleton<IInfraredHubSelectionAccessor, NoSelectedInfraredHubAccessor>();
        services.TryAddSingleton<IIrProfileManager, IrProfileManager>();
        // Device provisioning is host-level: register it only when a registrar is already available.
        // This keeps the standalone GenericIr package valid in tests/samples that only need parsing/transmit.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IDeviceRegistrar)))
            services.TryAddSingleton<IIrDeviceProvisioner, GenericIrDeviceProvisioner>();
        services.TryAddSingleton<GenericIrRemoteProvider>();
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ProviderRegistrationMarker)))
        {
            services.AddSingleton(new ProviderRegistrationMarker());
            services.AddSingleton<IRemoteProvider>(sp => sp.GetRequiredService<GenericIrRemoteProvider>());
        }
        return services;
    }

    private sealed class ProviderRegistrationMarker { }
}
