using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Discovery;
using UniversalRemote.Maui.AndroidTv;
using UniversalRemote.Maui.Discovery;
using UniversalRemote.Maui.Remote;
using UniversalRemote.Maui.Rooms;
using UniversalRemote.Maui.Freebox;
using UniversalRemote.Maui.Samsung;
using UniversalRemote.Maui.LG;
using UniversalRemote.Persistence.Sqlite;
using UniversalRemote.Provider.AndroidTv;
using UniversalRemote.Provider.Freebox;
using UniversalRemote.Provider.GenericUpnp;
using UniversalRemote.Provider.Samsung;
using UniversalRemote.Provider.LG;
using UniversalRemote.Provider.Simulator;
using Device = UniversalRemote.Abstractions.Device;
using UniversalRemote.Provider.GenericIr;
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
        builder.Services.AddUniversalRemoteApplication();
        builder.Services.AddUniversalRemoteDiscovery();
#if ANDROID
        builder.Services.AddUniversalRemoteAndroidInfrared();
        builder.Services.AddUniversalRemoteGenericIr();
        builder.Services.AddSingleton<IDiscoveryNetworkLease, AndroidDiscoveryNetworkLease>();
#endif

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "universalremote.devices.db");
        builder.Services.AddUniversalRemoteSqlitePersistence(databasePath);
        builder.Services.AddSingleton<IRemoteProvider, SimulatorProvider>();

        builder.Services.AddSingleton<IAndroidTvCredentialStore, SecureAndroidTvCredentialStore>();
        builder.Services.AddUniversalRemoteAndroidTv();
        builder.Services.AddSingleton<IFreeboxRemoteCodeStore, SecureFreeboxRemoteCodeStore>();
        builder.Services.AddUniversalRemoteFreebox();
        builder.Services.AddUniversalRemoteGenericUpnp();
        builder.Services.AddSingleton<ISamsungCredentialStore, SecureSamsungCredentialStore>();
        builder.Services.AddUniversalRemoteSamsung();
        builder.Services.AddSingleton<ILgCredentialStore, SecureLgCredentialStore>();
        builder.Services.AddUniversalRemoteLg();
        builder.Services.AddSingleton<DeviceSelectionState>();

        builder.Services.AddSingleton<DiscoveryViewModel>();
        builder.Services.AddSingleton<DiscoveryPage>();
        builder.Services.AddSingleton<RemoteLayoutPreferenceStore>();
        builder.Services.AddSingleton<RemoteViewModel>();
        builder.Services.AddSingleton<IRemoteLayoutRenderer, ClassicRemoteRenderer>();
        builder.Services.AddSingleton<IRemoteLayoutRenderer, MinimalRemoteRenderer>();
        foreach (var layout in UniversalRemote.Theming.BuiltInRemoteStyles.Layouts.Where(x => x.Id is not ("classic" or "minimal")))
            builder.Services.AddSingleton<IRemoteLayoutRenderer>(new StyledRemoteRenderer(layout));
        builder.Services.AddSingleton<RemotePage>();
        builder.Services.AddSingleton<RoomsViewModel>();
        builder.Services.AddSingleton<RoomsPage>();
        builder.Services.AddSingleton<AppShell>();

        var app = builder.Build();
        SeedDemoDevice(app.Services);
        return app;
    }

    private static void SeedDemoDevice(IServiceProvider services)
    {
        var demo = new Device(DemoDeviceId, "TV de démonstration — simulation",
            [new DeviceRoute(SimulatorProvider.ProviderId, "demo-tv",
                [RemoteActions.PowerToggle, RemoteActions.VolumeUp, RemoteActions.VolumeDown, RemoteActions.Ok])]);
        services.GetRequiredService<IDeviceRegistrar>().UpsertAsync(demo).GetAwaiter().GetResult();
    }
}
