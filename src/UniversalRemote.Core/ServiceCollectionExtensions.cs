using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalRemoteCore(this IServiceCollection services, Action<CommandOptions>? configure = null)
    {
        var options = new CommandOptions();
        configure?.Invoke(options);
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(configure));
        services.TryAddSingleton(options);
        services.TryAddScoped<ProviderResolver>();
        services.TryAddScoped<IRemoteControl, CommandDispatcher>();
        services.TryAddSingleton<IActivityDelayScheduler, SystemActivityDelayScheduler>();
        services.TryAddScoped<IActivityRunner, ActivityRunner>();
        return services;
    }
}
