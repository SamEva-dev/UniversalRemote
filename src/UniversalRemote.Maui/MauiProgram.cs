using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Compatibility;
using UniversalRemote.Discovery;
using UniversalRemote.Maui.Activities;
using UniversalRemote.Maui.AndroidTv;
using UniversalRemote.Maui.Compatibility;
using UniversalRemote.Maui.Discovery;
using UniversalRemote.Maui.Remote;
using UniversalRemote.Maui.Privacy;
using UniversalRemote.Maui.Rooms;
using UniversalRemote.Maui.Freebox;
using UniversalRemote.Maui.Favorites;
using UniversalRemote.Maui.Samsung;
using UniversalRemote.Maui.LG;
using UniversalRemote.Maui.Hub;
using UniversalRemote.Persistence.Sqlite;
using UniversalRemote.Provider.AndroidTv;
using UniversalRemote.Provider.Freebox;
using UniversalRemote.Provider.GenericUpnp;
using UniversalRemote.Provider.OrangeTv;
using UniversalRemote.Provider.Samsung;
using UniversalRemote.Provider.LG;
using UniversalRemote.Provider.Simulator;
using Device = UniversalRemote.Abstractions.Device;
using UniversalRemote.Provider.GenericIr;
using UniversalRemote.Hub;
using UniversalRemote.Hub.Wifi;
using UniversalRemote.Hub.Ble;
using UniversalRemote.Telemetry;
#if ANDROID
using UniversalRemote.Platform.Android;
#endif

namespace UniversalRemote.Maui;

public static class MauiProgram
{
    public static readonly Guid DemoDeviceId = Guid.Parse("b9aeb203-574d-4a81-9ed1-7e01dfed1901");

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<ITelemetryConsentStore, PreferencesTelemetryConsentStore>();
        var telemetryPath = Path.Combine(FileSystem.AppDataDirectory, "telemetry-v1.jsonl");
        builder.Services.AddUniversalRemoteTelemetry(telemetryPath);
        builder.Services.AddUniversalRemoteApplication();
        builder.Services.AddUniversalRemoteDiscovery();
        builder.Services.AddSingleton<IWifiInfraredHubConnectionStore, SecureWifiInfraredHubConnectionStore>();
        builder.Services.AddUniversalRemoteInfraredHubWifi();
        builder.Services.AddSingleton<IBleInfraredHubConnectionStore, SecureBleInfraredHubConnectionStore>();
#if ANDROID
        builder.Services.AddUniversalRemoteAndroidBleHub();
#endif
        builder.Services.AddUniversalRemoteInfraredHubBle();
        builder.Services.AddSingleton<IInfraredHubSelectionStore, SecureInfraredHubSelectionStore>();
        builder.Services.AddSingleton<IInfraredHubSelectionAccessor, InfraredHubSelectionAccessor>();
        builder.Services.AddSingleton<IOperatorCompatibilityCatalog>(BuiltInOperatorCompatibilityCatalog.Instance);
        builder.Services.AddSingleton<IOperatorCompatibilityQualifier>(BuiltInOperatorCompatibilityQualifier.Instance);
        builder.Services.AddSingleton<IOperatorCompatibilityDiagnostics, OperatorCompatibilityDiagnostics>();
#if ANDROID
        builder.Services.AddUniversalRemoteAndroidInfrared();
        builder.Services.AddSingleton<IDiscoveryNetworkLease, AndroidDiscoveryNetworkLease>();
#endif
        // Replace the legacy native-only IInfraredTransmitter with a pre-send selector. The native transmitter remains
        // registered through IOnDeviceInfraredTransmitter; selected Wi-Fi/BLE hubs are resolved transport-neutrally.
        builder.Services.RemoveAll<IInfraredTransmitter>();
        builder.Services.AddSingleton<IInfraredTransmitter, AdaptiveInfraredTransmitter>();
        var irProfilesPath = Path.Combine(FileSystem.AppDataDirectory, "ir-profiles");
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "universalremote.devices.db");
        builder.Services.AddUniversalRemoteSqlitePersistence(databasePath);
        builder.Services.AddUniversalRemoteGenericIr(irProfilesPath);
        builder.Services.AddSingleton<IRemoteProvider, SimulatorProvider>();

        builder.Services.AddSingleton<IAndroidTvCredentialStore, SecureAndroidTvCredentialStore>();
        builder.Services.AddUniversalRemoteAndroidTv();
        builder.Services.AddSingleton<IFreeboxRemoteCodeStore, SecureFreeboxRemoteCodeStore>();
        builder.Services.AddUniversalRemoteFreebox();
        builder.Services.AddUniversalRemoteGenericUpnp();
        builder.Services.AddUniversalRemoteOrangeTv();
        builder.Services.AddSingleton<ISamsungCredentialStore, SecureSamsungCredentialStore>();
        builder.Services.AddUniversalRemoteSamsung();
        builder.Services.AddSingleton<ILgCredentialStore, SecureLgCredentialStore>();
        builder.Services.AddUniversalRemoteLg();
        builder.Services.AddSingleton<DeviceSelectionState>();

        builder.Services.AddSingleton<DiscoveryViewModel>();
        builder.Services.AddSingleton<DiscoveryPage>();
        builder.Services.AddSingleton<RemoteLayoutPreferenceStore>();
        builder.Services.AddSingleton<RemoteViewModel>();

        builder.Services.AddSingleton<IRemoteLayoutRenderer, MinimalRemoteRenderer>();
        foreach (var layout in UniversalRemote.Theming.BuiltInRemoteStyles.Layouts.Where(x => x.Id != "minimal"))
            builder.Services.AddSingleton<IRemoteLayoutRenderer>(new ReferenceRemoteRenderer(layout.Id));
        builder.Services.AddSingleton<RemotePage>();
        builder.Services.AddSingleton<RoomsViewModel>();
        builder.Services.AddSingleton<RoomsPage>();
        builder.Services.AddSingleton<FavoritesViewModel>();
        builder.Services.AddSingleton<FavoritesPage>();
        builder.Services.AddSingleton<ActivitiesViewModel>();
        builder.Services.AddSingleton<ActivitiesPage>();
        builder.Services.AddSingleton<CompatibilityViewModel>();
        builder.Services.AddSingleton<CompatibilityPage>();
        builder.Services.AddSingleton<HubViewModel>();
        builder.Services.AddSingleton<HubPage>();
        builder.Services.AddSingleton<PrivacyViewModel>();
        builder.Services.AddSingleton<PrivacyPage>();
        builder.Services.AddSingleton<AppShell>();

        var app = builder.Build();
        SeedDemoDevice(app.Services);
        return app;
    }

    private static void SeedDemoDevice(IServiceProvider services)
    {
        var demo = new Device(DemoDeviceId, "TV de démonstration — simulation",
            [new DeviceRoute(SimulatorProvider.ProviderId, "demo-tv",
                UniversalRemote.Presentation.RemoteConceptCatalog.DemoActions)]);
        services.GetRequiredService<IDeviceRegistrar>().UpsertAsync(demo).GetAwaiter().GetResult();
    }
}
