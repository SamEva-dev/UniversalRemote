using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Core;
using UniversalRemote.Remote.Provider.GenericIr;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class FinalAuditRegressionTests
{
    [Fact]
    public void Repeated_ir_registration_keeps_one_provider_and_registers_late_provisioner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInfraredTransmitter, UnusedTransmitter>();
        services.AddUniversalRemoteGenericIr();
        services.AddSingleton<IDeviceRegistrar, InMemoryDeviceRepository>();
        services.AddUniversalRemoteGenericIr();
        using var container = services.BuildServiceProvider();
        var providers = container.GetServices<IRemoteProvider>().ToArray();
        Assert.Single(providers);
        Assert.Same(container.GetRequiredService<GenericIrRemoteProvider>(), providers[0]);
        Assert.NotNull(container.GetRequiredService<IIrDeviceProvisioner>());
        Assert.NotNull(new ProviderResolver(providers));
    }

    [Fact]
    public async Task Rejected_command_is_recorded_as_failed_without_changing_result()
    {
        var recorder = new Capture();
        var behavior = new SanitizedDiagnosticsBehavior<ExecuteRemoteAction, RemoteResult>(recorder);
        var failure = RemoteResult.Failed(RemoteErrorCode.PairingRequired);
        var result = await behavior.Handle(new ExecuteRemoteAction(Guid.NewGuid(), RemoteActions.PowerToggle),
            () => Task.FromResult(failure), CancellationToken.None);
        Assert.Same(failure, result);
        Assert.Equal(TelemetryOutcome.Failed, Assert.Single(recorder.Items).Outcome);
    }

    private sealed class UnusedTransmitter : IInfraredTransmitter
    {
        public ValueTask<InfraredEmitterInfo> GetInfoAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The registration test must not access hardware.");
        public Task<InfraredTransmitResult> TransmitAsync(int carrierFrequencyHz, IReadOnlyList<int> patternMicroseconds,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The registration test must not transmit.");
    }

    private sealed class Capture : ITelemetryRecorder
    {
        public List<TelemetryRecord> Items { get; } = [];
        public ValueTask RecordAsync(TelemetryRecord record, CancellationToken cancellationToken = default)
        {
            Items.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
