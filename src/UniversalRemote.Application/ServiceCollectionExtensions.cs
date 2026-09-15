using DomainRelay.Abstractions;
using DomainRelay.DependencyInjection;
using DomainRelay.Mapping.DependencyInjection.Extensions;
using DomainRelay.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Discovery;
using UniversalRemote.Presentation;
using UniversalRemote.Abstractions;
using UniversalRemote.Core;

namespace UniversalRemote.Application;

/// <summary>Call once per container. Explicit handler registration limits assembly scanning; AOT still requires a device test.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteApplication(this IServiceCollection services, Action<CommandOptions>? configure = null)
    {
        if (services.Any(s => s.ServiceType == typeof(RegistrationMarker))) return services;
        services.AddSingleton(new RegistrationMarker());
        services.AddUniversalRemoteCore(configure);
        services.AddUniversalRemoteDiscovery();
        services.TryAddSingleton<IRemoteUiModelBuilder, CapabilityRemoteUiModelBuilder>();
        services.AddDomainRelay(
            configureOptions: options => options.WrapExceptions = false,
            configureRegistration: registration => registration.EnableAssemblyScanning = false);
        // DomainRelay.Diagnostics 3.1.3 exports exception.Message; use sanitized tracing here.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(SanitizedDiagnosticsBehavior<,>));
        services.AddDomainRelayValidation();
        services.AddTransient<IValidator<ExecuteRemoteAction>, ExecuteRemoteActionValidator>();
        services.AddTransient<IValidator<GetRemoteUiModel>, GetRemoteUiModelValidator>();
        services.AddTransient<IValidator<DiscoverDevices>, DiscoverDevicesValidator>();
        services.AddTransient<IRequestHandler<GetRemoteUiModel, RemoteUiModel?>, GetRemoteUiModelHandler>();
        services.AddTransient<IRequestHandler<DiscoverDevices, IReadOnlyList<DiscoveredDeviceSummary>>, DiscoverDevicesHandler>();
        services.AddTransient<IRequestHandler<ExecuteRemoteAction, RemoteResult>, ExecuteRemoteActionHandler>();
        services.AddTransient<IRequestHandler<ListDevices, IReadOnlyList<DeviceSummary>>, ListDevicesHandler>();
        services.AddTransient<IValidator<StartPairing>, StartPairingValidator>();
        services.AddTransient<IValidator<CompletePairing>, CompletePairingValidator>();
        services.AddTransient<IRequestHandler<GetPairingCandidates, IReadOnlyList<PairingCandidate>>, GetPairingCandidatesHandler>();
        services.AddTransient<IRequestHandler<StartPairing, PairingChallenge>, StartPairingHandler>();
        services.AddTransient<IRequestHandler<CompletePairing, PairedDeviceSummary>, CompletePairingHandler>();
        services.AddDomainRelayMapping(builder => builder.AddProfile<DeviceMappingProfile>());
        return services;
    }
    private sealed class RegistrationMarker { }
}
