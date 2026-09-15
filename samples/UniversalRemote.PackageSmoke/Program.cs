using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
using UniversalRemote.Core;
using UniversalRemote.Persistence.Sqlite;
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

    return result.Error == RemoteErrorCode.DeviceNotFound
        && BuiltInRemoteStyles.Layouts.Count == 7
        && roundTrip == style
        && first.Id == second.Id ? 0 : 1;
}
finally
{
    try { File.Delete(databasePath); } catch (IOException) { }
    try { File.Delete(databasePath + "-shm"); } catch (IOException) { }
    try { File.Delete(databasePath + "-wal"); } catch (IOException) { }
}
