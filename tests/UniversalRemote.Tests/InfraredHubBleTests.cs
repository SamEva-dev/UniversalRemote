using Microsoft.Extensions.DependencyInjection;
using System.Text;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Hub.Ble;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class InfraredHubBleTests
{
    private static readonly Guid HubGuid = Guid.Parse("8e6ff715-38dc-49fb-bdb8-e8f5bf4b7191");
    private static readonly InfraredHubId HubId = new(HubGuid.ToString("N"));
    private static readonly InfraredSignal Signal = new(38_000, [9_000, 4_500, 560, 560]);

    [Fact]
    public void Advertisement_service_data_round_trips_canonical_hub_id()
    {
        var bytes = BleInfraredHubProtocol.BuildAdvertisementServiceData(HubGuid);
        Assert.True(BleInfraredHubProtocol.TryParseAdvertisementServiceData(bytes, out var id, out var api));
        Assert.Equal(HubId, id);
        Assert.Equal(BleInfraredHubProtocol.ApiVersion, api);
    }

    [Fact]
    public void Gatt_frames_fit_default_mtu_and_reassemble_exact_request()
    {
        var request = Enumerable.Range(0, 301).Select(x => (byte)(x % 251)).ToArray();
        var frames = BleInfraredHubProtocol.BuildGattFrames(request, BleInfraredHubProtocol.DefaultAttMtu);
        Assert.True(frames.Count > 1);
        Assert.All(frames, f => Assert.True(f.Length <= BleInfraredHubProtocol.DefaultAttMtu - 3));
        var rebuilt = frames.SelectMany(frame => frame.Skip(BleInfraredHubProtocol.GattFrameHeaderBytes)).ToArray();
        Assert.Equal(request, rebuilt);
        Assert.Equal(1, frames[0][1] & 1);
        Assert.Equal(2, frames[^1][1] & 2);
    }

    [Fact]
    public async Task Discovery_filters_unsupported_api_and_ambiguous_duplicate_hub_ids()
    {
        var radio = new FakeRadio
        {
            Advertisements =
            [
                new(HubId, "AA:01", "Hub A", BleInfraredHubProtocol.ApiVersion),
                new(HubId, "AA:02", "Hub clone", BleInfraredHubProtocol.ApiVersion),
                new(new InfraredHubId(Guid.NewGuid().ToString("N")), "AA:03", "Future", 2)
            ]
        };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IBleInfraredHubRadio>(radio);
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var discovery = provider.GetServices<IInfraredHubDiscovery>().Single(x => x.TransportKind == InfraredHubTransportKind.BluetoothLowEnergy);
        var results = await discovery.DiscoverAsync(TimeSpan.FromMilliseconds(20));
        Assert.Empty(results);
    }

    [Fact]
    public async Task Connect_validates_info_identity_before_persisting()
    {
        var radio = new FakeRadio { Advertisements = [new(HubId, "AA:01", "Salon Hub", 1)] };
        radio.InfoPayload = InfoJson(HubId, "Salon Hub");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IBleInfraredHubRadio>(radio);
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var discovery = provider.GetServices<IInfraredHubDiscovery>().Single(x => x.TransportKind == InfraredHubTransportKind.BluetoothLowEnergy);
        Assert.Single(await discovery.DiscoverAsync(TimeSpan.FromMilliseconds(20)));
        var connector = provider.GetServices<IInfraredHubConnector>().Single(x => x.TransportKind == InfraredHubTransportKind.BluetoothLowEnergy);
        var result = await connector.ConnectAsync(HubId);
        Assert.Equal(InfraredHubConnectOutcome.Connected, result.Outcome);
        Assert.Equal(InfraredHubConnectionState.Ready, result.Info!.State);
        var stored = await provider.GetRequiredService<IBleInfraredHubConnectionStore>().FindAsync(HubId);
        Assert.Equal("AA:01", stored!.DeviceKey);
    }

    [Fact]
    public async Task Connect_rejects_mismatched_info_identity()
    {
        var radio = new FakeRadio { Advertisements = [new(HubId, "AA:01", "Hub", 1)] };
        radio.InfoPayload = InfoJson(new InfraredHubId(Guid.NewGuid().ToString("N")), "Other Hub");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IBleInfraredHubRadio>(radio);
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var discovery = provider.GetServices<IInfraredHubDiscovery>().Single(x => x.TransportKind == InfraredHubTransportKind.BluetoothLowEnergy);
        await discovery.DiscoverAsync(TimeSpan.FromMilliseconds(20));
        var connector = provider.GetServices<IInfraredHubConnector>().Single(x => x.TransportKind == InfraredHubTransportKind.BluetoothLowEnergy);
        var result = await connector.ConnectAsync(HubId);
        Assert.Equal(InfraredHubConnectOutcome.IdentityMismatch, result.Outcome);
        Assert.Null(await provider.GetRequiredService<IBleInfraredHubConnectionStore>().FindAsync(HubId));
    }

    [Fact]
    public async Task Transmit_maps_correlated_acknowledgement_and_calls_radio_once()
    {
        var store = new InMemoryBleInfraredHubConnectionStore();
        await store.SaveAsync(new BleInfraredHubConnection(HubId, "AA:01", 1));
        var radio = new FakeRadio { AckOutcome = 0 };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IBleInfraredHubConnectionStore>(store);
        services.AddSingleton<IBleInfraredHubRadio>(radio);
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetServices<IInfraredHubTransport>().Single(x => x.Kind == InfraredHubTransportKind.BluetoothLowEnergy);
        var result = await transport.TransmitAsync(HubId, Signal);
        Assert.Equal(InfraredHubTransmitOutcome.Accepted, result.Outcome);
        Assert.Equal(1, radio.TransmitCalls);
    }

    [Fact]
    public async Task Definite_pre_send_failure_stays_failed_before_send_and_is_not_retried()
    {
        var store = new InMemoryBleInfraredHubConnectionStore();
        await store.SaveAsync(new BleInfraredHubConnection(HubId, "AA:01", 1));
        var radio = new FakeRadio { TransmitOutcome = BleInfraredHubRadioTransmitOutcome.FailedBeforeSend };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IBleInfraredHubConnectionStore>(store);
        services.AddSingleton<IBleInfraredHubRadio>(radio);
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetServices<IInfraredHubTransport>().Single(x => x.Kind == InfraredHubTransportKind.BluetoothLowEnergy);
        var result = await transport.TransmitAsync(HubId, Signal);
        Assert.Equal(InfraredHubTransmitOutcome.FailedBeforeSend, result.Outcome);
        Assert.Equal(1, radio.TransmitCalls);
    }

    [Fact]
    public async Task Ambiguous_radio_failure_remains_unknown_and_is_not_retried()
    {
        var store = new InMemoryBleInfraredHubConnectionStore();
        await store.SaveAsync(new BleInfraredHubConnection(HubId, "AA:01", 1));
        var radio = new FakeRadio { TransmitOutcome = BleInfraredHubRadioTransmitOutcome.Unknown };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IBleInfraredHubConnectionStore>(store);
        services.AddSingleton<IBleInfraredHubRadio>(radio);
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetServices<IInfraredHubTransport>().Single(x => x.Kind == InfraredHubTransportKind.BluetoothLowEnergy);
        var result = await transport.TransmitAsync(HubId, Signal);
        Assert.Equal(InfraredHubTransmitOutcome.Unknown, result.Outcome);
        Assert.Equal(1, radio.TransmitCalls);
    }

    [Fact]
    public async Task Invalid_acknowledgement_is_unknown()
    {
        var store = new InMemoryBleInfraredHubConnectionStore();
        await store.SaveAsync(new BleInfraredHubConnection(HubId, "AA:01", 1));
        var radio = new FakeRadio { InvalidAck = true };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IBleInfraredHubConnectionStore>(store);
        services.AddSingleton<IBleInfraredHubRadio>(radio);
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetServices<IInfraredHubTransport>().Single(x => x.Kind == InfraredHubTransportKind.BluetoothLowEnergy);
        var result = await transport.TransmitAsync(HubId, Signal);
        Assert.Equal(InfraredHubTransmitOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Learn_stays_unsupported_until_remote_050()
    {
        var store = new InMemoryBleInfraredHubConnectionStore();
        await store.SaveAsync(new BleInfraredHubConnection(HubId, "AA:01", 1));
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IBleInfraredHubConnectionStore>(store);
        services.AddSingleton<IBleInfraredHubRadio>(new FakeRadio());
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetServices<IInfraredHubTransport>().Single(x => x.Kind == InfraredHubTransportKind.BluetoothLowEnergy);
        var result = await transport.LearnAsync(HubId);
        Assert.Equal(InfraredHubLearnOutcome.Unsupported, result.Outcome);
    }

    [Fact]
    public void DI_registers_ble_discovery_connector_and_transport_without_platform_radio()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceProviderOptions { ValidateOnBuild = true });
        Assert.Contains(provider.GetServices<IInfraredHubDiscovery>(), x => x.TransportKind == InfraredHubTransportKind.BluetoothLowEnergy);
        Assert.Contains(provider.GetServices<IInfraredHubConnector>(), x => x.TransportKind == InfraredHubTransportKind.BluetoothLowEnergy);
        Assert.Contains(provider.GetServices<IInfraredHubTransport>(), x => x.Kind == InfraredHubTransportKind.BluetoothLowEnergy);
    }

    private static byte[] InfoJson(InfraredHubId id, string name) => Encoding.UTF8.GetBytes($$"""
    {"schemaVersion":1,"hubId":"{{id.Value}}","displayName":"{{name}}","firmwareVersion":"1.0.0","state":"ready","canTransmit":true,"canLearn":false,"carrierFrequencies":[{"minHz":36000,"maxHz":40000}],"maxPatternValues":4096,"maxTotalDurationMicroseconds":2000000}
    """);

    private sealed class FakeRadio : IBleInfraredHubRadio
    {
        public IReadOnlyList<BleInfraredHubRadioAdvertisement> Advertisements { get; set; } = [];
        public byte[] InfoPayload { get; set; } = InfoJson(HubId, "Fake Hub");
        public BleInfraredHubRadioTransmitOutcome TransmitOutcome { get; set; } = BleInfraredHubRadioTransmitOutcome.Acknowledged;
        public byte AckOutcome { get; set; }
        public bool InvalidAck { get; set; }
        public int TransmitCalls { get; private set; }

        public Task<BleInfraredHubRadioScanResult> ScanAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult(new BleInfraredHubRadioScanResult(BleInfraredHubRadioScanOutcome.Success, Advertisements, "ok"));

        public Task<BleInfraredHubRadioProbeResult> ProbeAsync(BleInfraredHubConnection connection, CancellationToken cancellationToken = default)
            => Task.FromResult(new BleInfraredHubRadioProbeResult(BleInfraredHubRadioProbeOutcome.Success, InfoPayload, "ok"));

        public Task<BleInfraredHubRadioTransmitResult> TransmitAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
        {
            TransmitCalls++;
            if (TransmitOutcome != BleInfraredHubRadioTransmitOutcome.Acknowledged)
                return Task.FromResult(new BleInfraredHubRadioTransmitResult(TransmitOutcome));
            if (InvalidAck) return Task.FromResult(new BleInfraredHubRadioTransmitResult(TransmitOutcome, new byte[] { 1, 2, 3 }));
            var ack = new byte[34];
            ack[0] = 1;
            request.Span.Slice(1, 16).CopyTo(ack.AsSpan(1, 16));
            Convert.FromHexString(HubGuid.ToString("N")).CopyTo(ack, 17);
            ack[33] = AckOutcome;
            return Task.FromResult(new BleInfraredHubRadioTransmitResult(TransmitOutcome, ack));
        }

        public Task<BleInfraredHubRadioLearnResult> LearnAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
            => Task.FromResult(new BleInfraredHubRadioLearnResult(BleInfraredHubRadioLearnOutcome.Failed));
    }
}
