using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
using UniversalRemote.Core;

var services = new ServiceCollection();
services.AddUniversalRemoteCore();
services.AddSingleton<IDeviceRepository, EmptyRepository>();
using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var scope = root.CreateScope();
var result = await scope.ServiceProvider.GetRequiredService<IRemoteControl>().ExecuteAsync(Guid.NewGuid(), RemoteActions.VolumeUp);
Console.WriteLine($"NuGet smoke: {result.Error}");
return result.Error == RemoteErrorCode.DeviceNotFound ? 0 : 1;

sealed class EmptyRepository : IDeviceRepository
{
    public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Device?>(null);
    public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Device>>(Array.Empty<Device>());
}
