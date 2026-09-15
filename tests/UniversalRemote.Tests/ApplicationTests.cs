using DomainRelay.Abstractions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Provider.Simulator;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class ApplicationTests
{
    private static ServiceProvider Container()
    {
        var services = new ServiceCollection();
        services.AddUniversalRemoteApplication();
        services.AddUniversalRemoteApplication();
        services.AddSingleton<IDeviceRepository, DemoDeviceRepository>();
        services.AddSingleton<IDeviceRegistrar, UniversalRemote.Core.InMemoryDeviceRepository>();
        services.AddSingleton<IRemoteProvider, SimulatorProvider>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task Query_traverses_mediator_and_mapping()
    {
        using var root = Container(); using var scope = root.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ListDevices());
        var device = Assert.Single(result);
        Assert.Equal(DemoDeviceRepository.DemoDeviceId, device.Id);
        Assert.Contains("simulation", device.DisplayName);
    }

    [Fact]
    public async Task Command_traverses_mediator_and_provider()
    {
        using var root = Container(); using var scope = root.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ExecuteRemoteAction(DemoDeviceRepository.DemoDeviceId, RemoteActions.VolumeUp));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Invalid_command_is_rejected_by_DomainRelay_validation()
    {
        using var root = Container(); using var scope = root.CreateScope();
        await Assert.ThrowsAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ExecuteRemoteAction(Guid.Empty, RemoteActions.VolumeUp)));
    }

    [Fact]
    public async Task Null_action_is_rejected_by_validation()
    {
        using var root = Container(); using var scope = root.CreateScope();
        await Assert.ThrowsAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ExecuteRemoteAction(DemoDeviceRepository.DemoDeviceId, null!)));
    }

    [Fact]
    public async Task DomainRelay_diagnostics_emit_request_activity()
    {
        var observed = new ConcurrentBag<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("DomainRelay", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => observed.Add(activity.DisplayName)
        };
        ActivitySource.AddActivityListener(listener);
        using var root = Container(); using var scope = root.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ListDevices());
        Assert.Contains(observed, name => name.Contains(nameof(ListDevices), StringComparison.Ordinal));
    }

    [Fact]
    public async Task DomainRelay_exception_messages_are_not_exported()
    {
        var observed = new ConcurrentBag<System.Diagnostics.Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("DomainRelay", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => observed.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        var behavior = new SanitizedDiagnosticsBehavior<ListDevices, IReadOnlyList<DeviceSummary>>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(new ListDevices(),
            () => throw new InvalidOperationException("pairing-token=secret"), CancellationToken.None));
        Assert.Contains(observed, a => a.GetTagItem("domainrelay.exception.type") is not null);
        Assert.All(observed, a => Assert.Null(a.GetTagItem("domainrelay.exception.message")));
    }
}
