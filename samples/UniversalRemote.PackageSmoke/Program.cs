using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
using UniversalRemote.Core;
using UniversalRemote.Compatibility;
using UniversalRemote.Persistence.Sqlite;
using UniversalRemote.Provider.GenericIr;
using UniversalRemote.Hub;
using UniversalRemote.Hub.Wifi;
using UniversalRemote.Hub.Ble;
using UniversalRemote.Theming;

var databasePath = Path.Combine(Path.GetTempPath(), $"universalremote-smoke-{Guid.NewGuid():N}.db");
try
{
    var services = new ServiceCollection();
    services.AddUniversalRemoteCore();
    services.AddUniversalRemoteSqlitePersistence(databasePath);
    using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    using var scope = root.CreateScope();

    var result = await scope.ServiceProvider.GetRequiredService<IRemoteControl>()
        .ExecuteAsync(Guid.NewGuid(), RemoteActions.VolumeUp);
    Console.WriteLine($"NuGet smoke: {result.Error}");

    var style = RemoteLayoutPreferences.ForLayout("neo");
    var roundTrip = RemoteLayoutPreferencesJson.ParseOrDefault(RemoteLayoutPreferencesJson.Serialize(style));
    Console.WriteLine($"NuGet styles: {BuiltInRemoteStyles.Layouts.Count}; restored={roundTrip.LayoutId}");

    var registrar = scope.ServiceProvider.GetRequiredService<IDeviceRegistrar>();
    var first = await registrar.RegisterPairingAsync("Smoke TV", new DeviceRoute("smoke", "stable-key", [RemoteActions.PowerToggle]));
    var second = await registrar.RegisterPairingAsync("Smoke TV", new DeviceRoute("smoke", "stable-key", [RemoteActions.PowerToggle, RemoteActions.VolumeUp]));
    Console.WriteLine($"NuGet persistence: stable={first.Id == second.Id}");

    var rooms = scope.ServiceProvider.GetRequiredService<IRoomRepository>();
    var room = await rooms.CreateAsync("Smoke Room");
    room = await rooms.AssignDeviceAsync(room.Id, second.Id);
    Console.WriteLine($"NuGet rooms: assigned={room.DeviceIds.Contains(second.Id)}");

    var favorites = scope.ServiceProvider.GetRequiredService<IFavoriteRepository>();
    await favorites.AddAsync(second.Id, RemoteActions.VolumeUp);
    var favoriteItems = await favorites.ListAsync(second.Id);
    Console.WriteLine($"NuGet favorites: count={favoriteItems.Count}");

    var activities = scope.ServiceProvider.GetRequiredService<IActivityRepository>();
    var activity = await activities.SaveAsync(new Activity(Guid.NewGuid(), "Smoke Activity", room.Id,
    [
        new RemoteActionActivityStep(0, second.Id, RemoteActions.PowerToggle),
        new DelayActivityStep(1, TimeSpan.FromMilliseconds(250)),
        new RemoteActionActivityStep(2, second.Id, RemoteActions.VolumeUp)
    ]));
    var restoredActivity = await activities.FindAsync(activity.Id);
    Console.WriteLine($"NuGet activities: steps={restoredActivity?.Steps.Count ?? 0}");

    var operatorProfiles = BuiltInOperatorCompatibilityCatalog.Instance.List();
    var bbox = BuiltInOperatorCompatibilityCatalog.Instance.Find("bouygues-bbox-androidtv");
    var bboxQualification = BuiltInOperatorCompatibilityQualifier.Instance.Qualify(new OperatorDeviceProbe(
        "androidtv",
        "Bbox 4K salon",
        ["_androidtvremote2._tcp.local"],
        new Dictionary<string, string> { ["model"] = "HMB9213NW-v2.1", ["firmware"] = "smoke-firmware" }));
    var sfr = BuiltInOperatorCompatibilityCatalog.Instance.Find("sfr-connect-tv-androidtv");
    var sfrQualification = BuiltInOperatorCompatibilityQualifier.Instance.Qualify(new OperatorDeviceProbe(
        "androidtv",
        "SFR Connect TV salon",
        ["_androidtvremote2._tcp.local"],
        new Dictionary<string, string> { ["model"] = "DV8555", ["build.id"] = "smoke-sfr-firmware" }));
    Console.WriteLine($"NuGet operator compatibility: profiles={operatorProfiles.Count}; bboxProvider={bbox?.ProviderId}; bboxQualified={bboxQualification?.ReadyForPhysicalRecipe}; sfrProvider={sfr?.ProviderId}; sfrQualified={sfrQualification?.ReadyForPhysicalRecipe}");

    var diagnostics = new OperatorCompatibilityDiagnostics(
        BuiltInOperatorCompatibilityCatalog.Instance,
        BuiltInOperatorCompatibilityQualifier.Instance);
    var matrix = diagnostics.BuildSnapshot(
    [
        new OperatorDeviceProbe("androidtv", "Bbox 4K", ["_androidtvremote2._tcp.local"],
            new Dictionary<string, string> { ["model"] = "HMB9213NW-v2.1", ["firmware"] = "smoke-firmware" }),
        new OperatorDeviceProbe("androidtv", "SFR Connect TV", ["_androidtvremote2._tcp.local"],
            new Dictionary<string, string> { ["model"] = "DV8555", ["build.id"] = "smoke-sfr-firmware" })
    ]);
    var matrixJson = diagnostics.Export(matrix, CompatibilityMatrixFormat.Json);
    Console.WriteLine($"NuGet operator diagnostics: detected={matrix.DetectedCount}; ready={matrix.ReadyForPhysicalRecipeCount}; research={matrix.ResearchOnlyCount}");

    var hubTransport = new SmokeInfraredHubTransport();
    var hub = new InfraredHubClient(new InfraredHubId("smoke-hub"), hubTransport);
    var hubTransmitter = new HubInfraredTransmitter(hub);
    var hubInfo = await hubTransmitter.GetInfoAsync();
    var hubSend = await hubTransmitter.TransmitAsync(38_000, [9_000, 4_500, 560, 560]);
    var hubLearn = await hub.LearnAsync();
    Console.WriteLine($"NuGet hub contract: transport={hub.TransportKind}; ready={hubInfo.HasEmitter}; send={hubSend.Outcome}; learn={hubLearn.Outcome}");

    var wifiConnectionStore = new InMemoryWifiInfraredHubConnectionStore();
    var wifiConnection = new WifiInfraredHubConnection(new InfraredHubId("smoke-wifi-hub"), "192.168.10.25", 8081, WifiInfraredHubProtocol.ApiVersion);
    await wifiConnectionStore.SaveAsync(wifiConnection);
    var restoredWifiConnection = await wifiConnectionStore.FindAsync(wifiConnection.HubId);
    Console.WriteLine($"NuGet hub Wi-Fi: service={WifiInfraredHubProtocol.MdnsServiceType}; transmit={WifiInfraredHubProtocol.TransmitPath}; restored={restoredWifiConnection is not null}");

    var bleGuid = Guid.Parse("8e6ff715-38dc-49fb-bdb8-e8f5bf4b7191");
    var bleHubId = new InfraredHubId(bleGuid.ToString("N"));
    var bleAdvert = BleInfraredHubProtocol.BuildAdvertisementServiceData(bleGuid);
    var bleParsed = BleInfraredHubProtocol.TryParseAdvertisementServiceData(bleAdvert, out var restoredBleHubId, out var bleApi);
    var bleFrames = BleInfraredHubProtocol.BuildGattFrames(new byte[512], BleInfraredHubProtocol.DefaultAttMtu);
    Console.WriteLine($"NuGet hub BLE: service={BleInfraredHubProtocol.ServiceUuid}; advert={bleParsed}; frames={bleFrames.Count}");

    var importCatalog = new InMemoryIrProfileCatalog();
    var importedIr = IrProfileParser.Parse("""{"version":1,"id":"smoke.import","displayName":"Smoke imported","verified":false,"source":"smoke","carrierFrequencyHz":38000,"commands":[{"actionId":"power.toggle","patternMicroseconds":[9000,4500,560,560]}]}""");
    importCatalog.Upsert(importedIr);
    var importedRoundTrip = IrProfileParser.Parse(IrProfileJson.Serialize(importedIr));
    var hubRecipe = BuiltInInfraredHubRecipes.Remote050;
    Console.WriteLine($"NuGet IR learning: profile={importedRoundTrip.Id}; recipeActions={hubRecipe.RequiredActions.Count}");

    var runner = scope.ServiceProvider.GetRequiredService<IActivityRunner>();
    var runnerSmoke = await runner.RunAsync(new Activity(Guid.NewGuid(), "Runner smoke", steps:
        [new DelayActivityStep(0, TimeSpan.FromMilliseconds(50))]));
    Console.WriteLine($"NuGet activity runner: status={runnerSmoke.Status}");

    return result.Error == RemoteErrorCode.DeviceNotFound
        && BuiltInRemoteStyles.Layouts.Count == 7
        && roundTrip == style
        && first.Id == second.Id
        && room.DeviceIds.Contains(second.Id)
        && favoriteItems.Count == 1
        && favoriteItems[0].Action == RemoteActions.VolumeUp
        && restoredActivity is not null
        && restoredActivity.Steps.Count == 3
        && operatorProfiles.Count >= 4
        && bbox?.ProviderId == "androidtv"
        && bboxQualification is not null
        && bboxQualification.MatchedModel == "HMB9213NW-v2.1"
        && bboxQualification.ReadyForPhysicalRecipe
        && sfr?.ProviderId == "androidtv"
        && sfrQualification is not null
        && sfrQualification.MatchedModel == "DV8555"
        && sfrQualification.ReadyForPhysicalRecipe
        && matrix.Profiles.Count >= 4
        && matrix.DetectedCount == 2
        && matrix.ReadyForPhysicalRecipeCount == 2
        && matrix.ResearchOnlyCount == 1
        && matrixJson.Contains("\"schemaVersion\": 1", StringComparison.Ordinal)
        && hubInfo.HasEmitter
        && hubInfo.SupportsFrequency(38_000)
        && hubSend.Outcome == InfraredTransmitOutcome.Accepted
        && hubLearn.Outcome == InfraredHubLearnOutcome.Captured
        && hubTransport.TransmitCalls == 1
        && restoredWifiConnection is not null
        && restoredWifiConnection.Address == "192.168.10.25"
        && restoredWifiConnection.ApiVersion == WifiInfraredHubProtocol.ApiVersion
        && bleParsed
        && restoredBleHubId == bleHubId
        && bleApi == BleInfraredHubProtocol.ApiVersion
        && bleFrames.Count > 1
        && importedRoundTrip.Id == "smoke.import"
        && importedRoundTrip.TryGetCommand(RemoteActions.PowerToggle, out _)
        && hubRecipe.RequiresPhoneWithoutNativeIr
        && hubRecipe.RequiresLearningOrImport
        && hubRecipe.RequiresActivityExecution
        && WifiInfraredHubProtocol.TransmitPath == "/api/v1/transmit"
        && WifiInfraredHubProtocol.LearnPath == "/api/v1/learn"
        && WifiInfraredHubProtocol.MaxTransmitRequestBytes >= 32 * 1024
        && WifiInfraredHubProtocol.MaxTransmitResponseBytes <= 16 * 1024
        && runnerSmoke.Status == ActivityRunStatus.Completed
        && runnerSmoke.Steps.Count == 1
        && runnerSmoke.Steps[0].Status == ActivityStepRunStatus.DelayCompleted ? 0 : 1;
}
finally
{
    try { File.Delete(databasePath); } catch (IOException) { }
    try { File.Delete(databasePath + "-shm"); } catch (IOException) { }
    try { File.Delete(databasePath + "-wal"); } catch (IOException) { }
}


file sealed class SmokeInfraredHubTransport : IInfraredHubTransport
{
    public string TransportId => "smoke-wifi";
    public InfraredHubTransportKind Kind => InfraredHubTransportKind.Wifi;
    public int TransmitCalls { get; private set; }

    public ValueTask<InfraredHubInfo> GetInfoAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new InfraredHubInfo(
            hubId,
            "Smoke hub",
            Kind,
            InfraredHubConnectionState.Ready,
            new InfraredHubCapabilities(true, true, [new InfraredFrequencyRange(36_000, 40_000)]),
            "hub.smoke.ready",
            "smoke-fw"));

    public Task<InfraredHubTransmitResult> TransmitAsync(InfraredHubId hubId, InfraredSignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TransmitCalls++;
        return Task.FromResult(InfraredHubTransmitResult.Accepted());
    }

    public Task<InfraredHubLearnResult> LearnAsync(InfraredHubId hubId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InfraredHubLearnResult.Captured(new InfraredSignal(38_000, [9_000, 4_500, 560, 560])));
    }
}
