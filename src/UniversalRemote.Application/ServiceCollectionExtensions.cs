using DomainRelay.Abstractions;
using DomainRelay.DependencyInjection;
using DomainRelay.Diagnostics;
using DomainRelay.Mapping.DependencyInjection.Extensions;
using DomainRelay.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddDomainRelay(
            configureOptions: options => options.WrapExceptions = false,
            configureRegistration: registration => registration.EnableAssemblyScanning = false);
        services.AddDomainRelayDiagnostics();
        services.AddDomainRelayValidation();
        services.AddTransient<IValidator<ExecuteRemoteAction>, ExecuteRemoteActionValidator>();
        services.AddTransient<IRequestHandler<ExecuteRemoteAction, RemoteResult>, ExecuteRemoteActionHandler>();
        services.AddTransient<IRequestHandler<ListDevices, IReadOnlyList<DeviceSummary>>, ListDevicesHandler>();
        services.AddDomainRelayMapping(builder => builder.AddProfile<DeviceMappingProfile>());
        return services;
    }
    private sealed class RegistrationMarker { }
}
