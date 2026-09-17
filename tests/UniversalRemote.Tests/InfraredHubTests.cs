using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Hub;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class InfraredHubTests
{
    private static readonly InfraredFrequencyRange TvRange = new(36_000, 40_000);
    private static readonly InfraredSignal TestSignal = new(38_000, [9_000, 4_500, 560, 560]);

    [Fact]
    public void Hub_id_is_trimmed_and_rejects_blank_values()
    {
        Assert.Equal("living-room", new InfraredHubId("  living-room  ").Value);
        Assert.Throws<ArgumentException>(() => new InfraredHubId("   "));
        Assert.Throws<ArgumentException>(() => new InfraredHubClient(default, ReadyTransport()));
    }

    [Fact]
    public void Signal_reuses_global_ir_safety_limits()
    {
        Assert.Equal(38_000, TestSignal.CarrierFrequencyHz);
        Assert.Equal(14_620, TestSignal.TotalDurationMicroseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InfraredSignal(1_000, [100, 100]));
        Assert.Throws<ArgumentException>(() => new InfraredSignal(38_000, [100, 0]));
    }


    [Fact]
    public void Hub_capabilities_reject_frequency_ranges_outside_shared_ir_limits()
    {
        Assert.Throws<ArgumentException>(() => new InfraredHubCapabilities(
            true,
            false,
            [new InfraredFrequencyRange(1_000, 40_000)]));
    }

    [Fact]
    public async Task Client_delegates_stable_hub_id_to_replaceable_transport()
    {
        var transport = ReadyTransport();
        var client = new InfraredHubClient(new InfraredHubId("hub-01"), transport);

        var info = await client.GetInfoAsync();
        var send = await client.TransmitAsync(TestSignal);
        var learned = await client.LearnAsync();

        Assert.Equal(InfraredHubTransportKind.Wifi, client.TransportKind);
        Assert.All(transport.SeenHubIds, id => Assert.Equal("hub-01", id.Value));
        Assert.Equal(InfraredHubConnectionState.Ready, info.State);
        Assert.Equal(InfraredHubTransmitOutcome.Accepted, send.Outcome);
        Assert.Equal(InfraredHubLearnOutcome.Captured, learned.Outcome);
    }

    [Fact]
    public async Task Transmitter_adapter_exposes_ready_hub_as_existing_ir_emitter()
    {
        var adapter = new HubInfraredTransmitter(new InfraredHubClient(new InfraredHubId("hub-01"), ReadyTransport()));

        var info = await adapter.GetInfoAsync();

        Assert.True(info.HasEmitter);
        Assert.True(info.SupportsFrequency(38_000));
        Assert.Equal("ir.hub.ready", info.DiagnosticCode);
    }

    [Fact]
    public async Task Adapter_maps_accepted_and_unknown_without_retrying()
    {
        var acceptedTransport = ReadyTransport();
        var accepted = new HubInfraredTransmitter(new InfraredHubClient(new InfraredHubId("hub-a"), acceptedTransport));
        Assert.Equal(InfraredTransmitOutcome.Accepted,
            (await accepted.TransmitAsync(38_000, TestSignal.PatternMicroseconds)).Outcome);
        Assert.Equal(1, acceptedTransport.TransmitCalls);

        var unknownTransport = ReadyTransport(InfraredHubTransmitResult.Unknown());
        var unknown = new HubInfraredTransmitter(new InfraredHubClient(new InfraredHubId("hub-b"), unknownTransport));
        Assert.Equal(InfraredTransmitOutcome.Unknown,
            (await unknown.TransmitAsync(38_000, TestSignal.PatternMicroseconds)).Outcome);
        Assert.Equal(1, unknownTransport.TransmitCalls);
    }

    [Fact]
    public async Task Adapter_rejects_frequency_not_advertised_by_hub_before_send()
    {
        var transport = ReadyTransport();
        var adapter = new HubInfraredTransmitter(new InfraredHubClient(new InfraredHubId("hub-01"), transport));

        var result = await adapter.TransmitAsync(56_000, TestSignal.PatternMicroseconds);

        Assert.Equal(InfraredTransmitOutcome.FrequencyUnsupported, result.Outcome);
        Assert.Equal(0, transport.TransmitCalls);
    }

    [Fact]
    public async Task Exception_after_entering_transport_is_conservatively_unknown_and_not_retried()
    {
        var transport = ReadyTransport();
        transport.ThrowOnTransmit = true;
        var adapter = new HubInfraredTransmitter(new InfraredHubClient(new InfraredHubId("hub-01"), transport));

        var result = await adapter.TransmitAsync(38_000, TestSignal.PatternMicroseconds);

        Assert.Equal(InfraredTransmitOutcome.Unknown, result.Outcome);
        Assert.Equal(1, transport.TransmitCalls);
    }


    [Fact]
    public async Task Caller_cancellation_is_propagated_and_still_never_retried()
    {
        using var cts = new CancellationTokenSource();
        var transport = ReadyTransport();
        transport.CancelDuringTransmit = cts;
        var adapter = new HubInfraredTransmitter(new InfraredHubClient(new InfraredHubId("hub-01"), transport));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.TransmitAsync(38_000, TestSignal.PatternMicroseconds, cts.Token));

        Assert.Equal(1, transport.TransmitCalls);
    }

    [Fact]
    public async Task Busy_or_offline_hub_is_not_exposed_as_available_emitter()
    {
        var transport = ReadyTransport();
        transport.State = InfraredHubConnectionState.Busy;
        var adapter = new HubInfraredTransmitter(new InfraredHubClient(new InfraredHubId("hub-01"), transport));

        var info = await adapter.GetInfoAsync();
        var result = await adapter.TransmitAsync(38_000, TestSignal.PatternMicroseconds);

        Assert.False(info.HasEmitter);
        Assert.Equal("ir.hub.busy", info.DiagnosticCode);
        Assert.Equal(InfraredTransmitOutcome.EmitterUnavailable, result.Outcome);
        Assert.Equal(0, transport.TransmitCalls);
    }

    [Fact]
    public void Learning_result_only_contains_signal_when_capture_succeeded()
    {
        var captured = InfraredHubLearnResult.Captured(TestSignal);
        Assert.Same(TestSignal, captured.Signal);
        Assert.Null(InfraredHubLearnResult.Timeout().Signal);
        Assert.Null(InfraredHubLearnResult.Unsupported().Signal);
    }

    private static FakeHubTransport ReadyTransport(InfraredHubTransmitResult? result = null)
        => new(result ?? InfraredHubTransmitResult.Accepted());

    private sealed class FakeHubTransport(InfraredHubTransmitResult transmitResult) : IInfraredHubTransport
    {
        public string TransportId => "test-wifi";
        public InfraredHubTransportKind Kind => InfraredHubTransportKind.Wifi;
        public List<InfraredHubId> SeenHubIds { get; } = [];
        public InfraredHubConnectionState State { get; set; } = InfraredHubConnectionState.Ready;
        public int TransmitCalls { get; private set; }
        public bool ThrowOnTransmit { get; set; }
        public CancellationTokenSource? CancelDuringTransmit { get; set; }

        public ValueTask<InfraredHubInfo> GetInfoAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SeenHubIds.Add(hubId);
            return ValueTask.FromResult(new InfraredHubInfo(
                hubId,
                "Test hub",
                Kind,
                State,
                new InfraredHubCapabilities(true, true, [TvRange]),
                State == InfraredHubConnectionState.Ready ? "hub.ready" : "hub.not_ready",
                "test-fw"));
        }

        public Task<InfraredHubTransmitResult> TransmitAsync(InfraredHubId hubId, InfraredSignal signal, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SeenHubIds.Add(hubId);
            TransmitCalls++;
            if (CancelDuringTransmit is not null)
            {
                CancelDuringTransmit.Cancel();
                return Task.FromCanceled<InfraredHubTransmitResult>(cancellationToken);
            }
            if (ThrowOnTransmit) throw new IOException("simulated transport ambiguity");
            return Task.FromResult(transmitResult);
        }

        public Task<InfraredHubLearnResult> LearnAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SeenHubIds.Add(hubId);
            return Task.FromResult(InfraredHubLearnResult.Captured(TestSignal));
        }
    }
}
