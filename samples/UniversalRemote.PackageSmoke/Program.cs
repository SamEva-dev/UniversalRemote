using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
using UniversalRemote.Core;
using UniversalRemote.Theming;

var services = new ServiceCollection();
services.AddUniversalRemoteCore();
services.AddSingleton<IDeviceRepository, EmptyRepository>();
using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var scope = root.CreateScope();
var result = await scope.ServiceProvider.GetRequiredService<IRemoteControl>().ExecuteAsync(Guid.NewGuid(), RemoteActions.VolumeUp);
Console.WriteLine($"NuGet smoke: {result.Error}");
var style = RemoteLayoutPreferences.ForLayout("neo");
var roundTrip = RemoteLayoutPreferencesJson.ParseOrDefault(RemoteLayoutPreferencesJson.Serialize(style));
Console.WriteLine($"NuGet styles: {BuiltInRemoteStyles.Layouts.Count}; restored={roundTrip.LayoutId}");
return result.Error == RemoteErrorCode.DeviceNotFound && BuiltInRemoteStyles.Layouts.Count == 7 && roundTrip == style ? 0 : 1;

sealed class EmptyRepository : IDeviceRepository
{
    public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Device?>(null);
    public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Device>>(Array.Empty<Device>());
}
