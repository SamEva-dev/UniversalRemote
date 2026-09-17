using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Core;
using UniversalRemote.Remote.Hub;
using UniversalRemote.Remote.Hub.Ble;
using UniversalRemote.Remote.Hub.Wifi;
using UniversalRemote.Remote.Provider.GenericIr;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class InfraredHubLearningTests
{
    private static readonly InfraredHubId WifiHubId = new("learn-wifi-hub");
    private static readonly Guid BleHubGuid = Guid.Parse("0ee844b7-b25d-4d99-a4f7-3081354f693c");
    private static readonly InfraredHubId BleHubId = new(BleHubGuid.ToString("N"));
    private static readonly InfraredSignal Signal = new(38_000, [9_000, 4_500, 560, 560]);

    [Fact]
    public async Task Wifi_learning_returns_validated_capture()
    {
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == WifiInfraredHubProtocol.InfoPath)
                return Json(HttpStatusCode.OK, InfoJson(WifiHubId, canLearn: true, transport: "wifi"));
            return Json(HttpStatusCode.OK, $$"""{"schemaVersion":1,"requestId":"{{ExtractRequestId(request)}}","hubId":"{{WifiHubId.Value}}","outcome":"captured","carrierFrequencyHz":38000,"patternMicroseconds":[9000,4500,560,560]}""");
        });
        var store = new InMemoryWifiInfraredHubConnectionStore();
        await store.SaveAsync(new WifiInfraredHubConnection(WifiHubId, "192.168.1.50", 8081, 1));
        var api = new WifiInfraredHubApiClient(new HttpClient(handler));
        var tx = new WifiInfraredHubTransmitClient(new HttpClient(handler));
        var learn = new WifiInfraredHubLearnClient(new HttpClient(handler));
        var transport = new WifiInfraredHubTransport(store, api, tx, learn);
        var result = await transport.LearnAsync(WifiHubId);
        Assert.Equal(InfraredHubLearnOutcome.Captured, result.Outcome);
        Assert.Equal(38_000, result.Signal!.CarrierFrequencyHz);
    }

    [Fact]
    public async Task Wifi_learning_rejects_mismatched_correlation()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath == WifiInfraredHubProtocol.InfoPath
            ? Json(HttpStatusCode.OK, InfoJson(WifiHubId, canLearn: true, transport: "wifi"))
            : Json(HttpStatusCode.OK, $$"""{"schemaVersion":1,"requestId":"wrong","hubId":"{{WifiHubId.Value}}","outcome":"captured","carrierFrequencyHz":38000,"patternMicroseconds":[9000,4500]}"""));
        var store = new InMemoryWifiInfraredHubConnectionStore();
        await store.SaveAsync(new WifiInfraredHubConnection(WifiHubId, "192.168.1.50", 8081, 1));
        var transport = new WifiInfraredHubTransport(store,
            new WifiInfraredHubApiClient(new HttpClient(handler)),
            new WifiInfraredHubTransmitClient(new HttpClient(handler)),
            new WifiInfraredHubLearnClient(new HttpClient(handler)));
        Assert.Equal(InfraredHubLearnOutcome.Failed, (await transport.LearnAsync(WifiHubId)).Outcome);
    }

    [Fact]
    public async Task Wifi_learning_honors_can_learn_capability_before_post()
    {
        var learnPosts = 0;
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == WifiInfraredHubProtocol.InfoPath)
                return Json(HttpStatusCode.OK, InfoJson(WifiHubId, canLearn: false, transport: "wifi"));
            learnPosts++;
            return Json(HttpStatusCode.OK, "{}");
        });
        var store = new InMemoryWifiInfraredHubConnectionStore();
        await store.SaveAsync(new WifiInfraredHubConnection(WifiHubId, "192.168.1.50", 8081, 1));
        var transport = new WifiInfraredHubTransport(store,
            new WifiInfraredHubApiClient(new HttpClient(handler)),
            new WifiInfraredHubTransmitClient(new HttpClient(handler)),
            new WifiInfraredHubLearnClient(new HttpClient(handler)));
        Assert.Equal(InfraredHubLearnOutcome.Unsupported, (await transport.LearnAsync(WifiHubId)).Outcome);
        Assert.Equal(0, learnPosts);
    }

    [Fact]
    public void Ble_learning_protocol_round_trips_captured_signal()
    {
        var requestId = Guid.NewGuid();
        var request = BleInfraredHubProtocol.EncodeLearnRequest(BleHubId, requestId);
        Assert.Equal(33, request.Length);
        var response = BuildBleLearnResponse(BleHubGuid, requestId, Signal);
        Assert.True(BleInfraredHubProtocol.TryParseLearnResult(response, BleHubId, requestId, out var result));
        Assert.Equal(InfraredHubLearnOutcome.Captured, result.Outcome);
        Assert.Equal(Signal.PatternMicroseconds, result.Signal!.PatternMicroseconds);
    }

    [Fact]
    public async Task Ble_transport_uses_radio_learning_when_capability_is_enabled()
    {
        var store = new InMemoryBleInfraredHubConnectionStore();
        await store.SaveAsync(new BleInfraredHubConnection(BleHubId, "AA:BB", 1));
        var radio = new LearningRadio();
        var services = new ServiceCollection();
        services.AddSingleton<IBleInfraredHubConnectionStore>(store);
        services.AddSingleton<IBleInfraredHubRadio>(radio);
        services.AddUniversalRemoteInfraredHubBle();
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetServices<IInfraredHubTransport>().Single(x => x.Kind == InfraredHubTransportKind.BluetoothLowEnergy);
        var result = await transport.LearnAsync(BleHubId);
        Assert.Equal(InfraredHubLearnOutcome.Captured, result.Outcome);
        Assert.Equal(1, radio.LearnCalls);
    }

    [Fact]
    public async Task Adaptive_transmitter_prefers_supported_native_emitter_and_never_calls_hub_after_attempt()
    {
        var native = new FakeOnDeviceTransmitter(InfraredTransmitResult.Unknown(), hasEmitter: true, supports38Khz: true);
        var hubTransport = new FakeHubTransport();
        var store = new InMemoryInfraredHubSelectionStore();
        await store.SaveAsync(new InfraredHubSelection(new InfraredHubId("hub"), InfraredHubTransportKind.Wifi));
        var adaptive = new AdaptiveInfraredTransmitter([native], new InfraredHubSelectionAccessor(store, [hubTransport]));
        var result = await adaptive.TransmitAsync(38_000, Signal.PatternMicroseconds);
        Assert.Equal(InfraredTransmitOutcome.Unknown, result.Outcome);
        Assert.Equal(1, native.TransmitCalls);
        Assert.Equal(0, hubTransport.TransmitCalls);
    }

    [Fact]
    public async Task Adaptive_transmitter_uses_selected_hub_when_phone_has_no_native_ir()
    {
        var native = new FakeOnDeviceTransmitter(InfraredTransmitResult.EmitterUnavailable(), hasEmitter: false, supports38Khz: false);
        var hubTransport = new FakeHubTransport();
        var store = new InMemoryInfraredHubSelectionStore();
        await store.SaveAsync(new InfraredHubSelection(new InfraredHubId("hub"), InfraredHubTransportKind.Wifi));
        var adaptive = new AdaptiveInfraredTransmitter([native], new InfraredHubSelectionAccessor(store, [hubTransport]));
        var info = await adaptive.GetInfoAsync();
        var result = await adaptive.TransmitAsync(38_000, Signal.PatternMicroseconds);
        Assert.True(info.HasEmitter);
        Assert.Equal(InfraredTransmitOutcome.Accepted, result.Outcome);
        Assert.Equal(1, hubTransport.TransmitCalls);
    }

    [Fact]
    public void Import_forces_unverified_and_file_catalog_survives_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "ur-ir-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = new FileIrProfileCatalog(path, [BuiltInIrProfiles.BenchSyntheticTv]);
            var manager = new IrProfileManager(catalog, new StaticSelectionAccessor(null));
            var imported = manager.ImportJson("""{"version":1,"id":"import.tv","displayName":"Imported","verified":true,"source":"external","carrierFrequencyHz":38000,"commands":[{"actionId":"power.toggle","patternMicroseconds":[9000,4500,560,560]}]}""");
            Assert.False(imported.Verified);
            var restored = new FileIrProfileCatalog(path);
            Assert.True(restored.TryGet("import.tv", out var profile));
            Assert.False(profile.Verified);
            Assert.True(profile.TryGetCommand(RemoteActions.PowerToggle, out _));
        }
        finally { try { Directory.Delete(path, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Learning_saves_profile_and_provisioner_preserves_device_identity_on_update()
    {
        var catalog = new InMemoryIrProfileCatalog();
        var hub = new FakeLearningHub(Signal);
        var manager = new IrProfileManager(catalog, new StaticSelectionAccessor(hub));
        var repository = new InMemoryDeviceRepository();
        var provisioner = new GenericIrDeviceProvisioner(catalog, repository);

        var firstLearn = await manager.LearnAndSaveAsync("learned.tv", "TV", RemoteActions.PowerToggle);
        Assert.Equal(IrProfileLearnOutcome.Saved, firstLearn.Outcome);
        var firstDevice = await provisioner.RegisterProfileAsync("learned.tv");

        var secondLearn = await manager.LearnAndSaveAsync("learned.tv", "TV", RemoteActions.VolumeUp);
        Assert.Equal(IrProfileLearnOutcome.Saved, secondLearn.Outcome);
        var secondDevice = await provisioner.RegisterProfileAsync("learned.tv");
        Assert.Equal(firstDevice.Id, secondDevice.Id);
        Assert.Equal(2, secondDevice.Capabilities.Count);
    }

    [Fact]
    public async Task Learning_does_not_overwrite_existing_action_unless_explicit()
    {
        var catalog = new InMemoryIrProfileCatalog();
        var manager = new IrProfileManager(catalog, new StaticSelectionAccessor(new FakeLearningHub(Signal)));
        Assert.Equal(IrProfileLearnOutcome.Saved, (await manager.LearnAndSaveAsync("tv", "TV", RemoteActions.PowerToggle)).Outcome);
        Assert.Equal(IrProfileLearnOutcome.ActionAlreadyExists, (await manager.LearnAndSaveAsync("tv", "TV", RemoteActions.PowerToggle)).Outcome);
        Assert.Equal(IrProfileLearnOutcome.Saved, (await manager.LearnAndSaveAsync("tv", "TV", RemoteActions.PowerToggle, overwriteExistingAction: true)).Outcome);
    }

    [Fact]
    public async Task Learning_rejects_carrier_frequency_change_inside_same_profile()
    {
        var catalog = new InMemoryIrProfileCatalog();
        var selection = new MutableSelectionAccessor(new FakeLearningHub(Signal));
        var manager = new IrProfileManager(catalog, selection);
        await manager.LearnAndSaveAsync("tv", "TV", RemoteActions.PowerToggle);
        selection.Hub = new FakeLearningHub(new InfraredSignal(56_000, [9000, 4500, 560, 560]));
        var result = await manager.LearnAndSaveAsync("tv", "TV", RemoteActions.VolumeUp);
        Assert.Equal(IrProfileLearnOutcome.CarrierFrequencyMismatch, result.Outcome);
    }

    [Fact]
    public void Remote050_recipe_requires_power_volume_learning_and_activity_on_phone_without_native_ir()
    {
        var recipe = BuiltInInfraredHubRecipes.Remote050;
        Assert.True(recipe.RequiresPhoneWithoutNativeIr);
        Assert.True(recipe.RequiresLearningOrImport);
        Assert.True(recipe.RequiresActivityExecution);
        Assert.Contains(RemoteActions.PowerToggle, recipe.RequiredActions);
        Assert.Contains(RemoteActions.VolumeUp, recipe.RequiredActions);
        Assert.Contains(RemoteActions.VolumeDown, recipe.RequiredActions);
    }

    private static string InfoJson(InfraredHubId id, bool canLearn, string transport) => $$$"""
    {"schemaVersion":1,"hubId":"{{{id.Value}}}","displayName":"Learning Hub","firmwareVersion":"1.0.0","state":"ready","capabilities":{"canTransmit":true,"canLearn":{{{canLearn.ToString().ToLowerInvariant()}}},"carrierFrequencies":[{"minHz":36000,"maxHz":60000}],"maxPatternValues":4096,"maxTotalDurationMicroseconds":2000000}}
    """;

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string ExtractRequestId(HttpRequestMessage request)
    {
        var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("requestId").GetString()!;
    }

    private static string BleInfoJson() => $$"""
    {"schemaVersion":1,"hubId":"{{BleHubId.Value}}","displayName":"Learning Hub","firmwareVersion":"1.0.0","state":"ready","canTransmit":true,"canLearn":true,"carrierFrequencies":[{"minHz":36000,"maxHz":60000}],"maxPatternValues":4096,"maxTotalDurationMicroseconds":2000000}
    """;

    private static byte[] BuildBleLearnResponse(Guid hubId, Guid requestId, InfraredSignal signal)
    {
        var bytes = new byte[40 + signal.PatternMicroseconds.Count * 4];
        bytes[0] = 1;
        Convert.FromHexString(requestId.ToString("N")).CopyTo(bytes, 1);
        Convert.FromHexString(hubId.ToString("N")).CopyTo(bytes, 17);
        bytes[33] = 0;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34, 4), signal.CarrierFrequencyHz);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(38, 2), checked((ushort)signal.PatternMicroseconds.Count));
        var offset = 40;
        foreach (var duration in signal.PatternMicroseconds)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), duration);
            offset += 4;
        }
        return bytes;
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(route(request));
    }

    private sealed class LearningRadio : IBleInfraredHubRadio
    {
        public int LearnCalls { get; private set; }
        public Task<BleInfraredHubRadioScanResult> ScanAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult(new BleInfraredHubRadioScanResult(BleInfraredHubRadioScanOutcome.Success, [], "ok"));
        public Task<BleInfraredHubRadioProbeResult> ProbeAsync(BleInfraredHubConnection connection, CancellationToken cancellationToken = default)
            => Task.FromResult(new BleInfraredHubRadioProbeResult(BleInfraredHubRadioProbeOutcome.Success,
                Encoding.UTF8.GetBytes(BleInfoJson()), "ok"));
        public Task<BleInfraredHubRadioTransmitResult> TransmitAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
            => Task.FromResult(new BleInfraredHubRadioTransmitResult(BleInfraredHubRadioTransmitOutcome.HubUnavailable));
        public Task<BleInfraredHubRadioLearnResult> LearnAsync(BleInfraredHubConnection connection, ReadOnlyMemory<byte> request, CancellationToken cancellationToken = default)
        {
            LearnCalls++;
            var requestId = Guid.ParseExact(Convert.ToHexString(request.Span.Slice(1, 16)), "N");
            return Task.FromResult(new BleInfraredHubRadioLearnResult(BleInfraredHubRadioLearnOutcome.Result,
                BuildBleLearnResponse(BleHubGuid, requestId, Signal)));
        }
    }

    private sealed class FakeOnDeviceTransmitter(InfraredTransmitResult transmitResult, bool hasEmitter, bool supports38Khz) : IOnDeviceInfraredTransmitter
    {
        public int TransmitCalls { get; private set; }
        public ValueTask<InfraredEmitterInfo> GetInfoAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new InfraredEmitterInfo(hasEmitter,
                supports38Khz ? [new InfraredFrequencyRange(36_000, 40_000)] : [], "native.fake"));
        public Task<InfraredTransmitResult> TransmitAsync(int carrierFrequencyHz, IReadOnlyList<int> patternMicroseconds, CancellationToken cancellationToken = default)
        {
            TransmitCalls++;
            return Task.FromResult(transmitResult);
        }
    }

    private sealed class FakeHubTransport : IInfraredHubTransport
    {
        public string TransportId => "fake-wifi";
        public InfraredHubTransportKind Kind => InfraredHubTransportKind.Wifi;
        public int TransmitCalls { get; private set; }
        public ValueTask<InfraredHubInfo> GetInfoAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new InfraredHubInfo(hubId, "Fake", Kind, InfraredHubConnectionState.Ready,
                new InfraredHubCapabilities(true, true, [new InfraredFrequencyRange(36_000, 60_000)]), "ready"));
        public Task<InfraredHubTransmitResult> TransmitAsync(InfraredHubId hubId, InfraredSignal signal, CancellationToken cancellationToken = default)
        { TransmitCalls++; return Task.FromResult(InfraredHubTransmitResult.Accepted()); }
        public Task<InfraredHubLearnResult> LearnAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
            => Task.FromResult(InfraredHubLearnResult.Captured(Signal));
    }

    private sealed class FakeLearningHub(InfraredSignal signal) : IInfraredHub
    {
        public InfraredHubId Id => new("learning-hub");
        public InfraredHubTransportKind TransportKind => InfraredHubTransportKind.Wifi;
        public ValueTask<InfraredHubInfo> GetInfoAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new InfraredHubInfo(Id, "Learning", TransportKind, InfraredHubConnectionState.Ready,
                new InfraredHubCapabilities(true, true, [new InfraredFrequencyRange(10_000, 100_000)]), "ready"));
        public Task<InfraredHubTransmitResult> TransmitAsync(InfraredSignal signal, CancellationToken cancellationToken = default)
            => Task.FromResult(InfraredHubTransmitResult.Accepted());
        public Task<InfraredHubLearnResult> LearnAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(InfraredHubLearnResult.Captured(signal));
    }

    private sealed class StaticSelectionAccessor(IInfraredHub? hub) : IInfraredHubSelectionAccessor
    {
        public ValueTask<IInfraredHub?> GetSelectedHubAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(hub);
    }

    private sealed class MutableSelectionAccessor(IInfraredHub hub) : IInfraredHubSelectionAccessor
    {
        public IInfraredHub Hub { get; set; } = hub;
        public ValueTask<IInfraredHub?> GetSelectedHubAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IInfraredHub?>(Hub);
    }
}
