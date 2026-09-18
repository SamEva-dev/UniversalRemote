using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Telemetry;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteTelemetry(this IServiceCollection services, string localStorePath, TelemetryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(localStorePath)) throw new ArgumentException("A local telemetry path is required.", nameof(localStorePath));
        var effective = options ?? new TelemetryOptions();
        effective.Validate();

        services.TryAddSingleton<ITelemetryConsentStore, DisabledTelemetryConsentStore>();
        services.TryAddSingleton<ITelemetryEventStore>(_ => new LocalTelemetryEventStore(localStorePath, effective));
        services.TryAddSingleton<ITelemetryExporter, TelemetryExporter>();
        services.TryAddSingleton<ITelemetryRecorder, TelemetryRecorder>();
        return services;
    }
}
