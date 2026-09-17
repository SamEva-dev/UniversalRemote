using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Core;

var deviceId = Guid.Parse("58475f2a-7cb9-4ed2-bcf7-a366356e8d51");
var demoDevice = new Device(
    deviceId,
    "SDK quickstart device",
    [new DeviceRoute(DemoProvider.ProviderId, "quickstart-device", [RemoteActions.PowerToggle, RemoteActions.VolumeUp])]);

var services = new ServiceCollection();
services.AddUniversalRemoteCore(options => options.Timeout = TimeSpan.FromSeconds(2));
services.AddSingleton<IDeviceRepository>(new SingleDeviceRepository(demoDevice));
services.AddSingleton<IRemoteProvider, DemoProvider>();

using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true
});
using var scope = provider.CreateScope();

var remote = scope.ServiceProvider.GetRequiredService<IRemoteControl>();
var result = await remote.ExecuteAsync(deviceId, RemoteActions.PowerToggle);

Console.WriteLine($"{RemoteActions.PowerToggle.Id}: {result.Error} / {result.Delivery}");
return result.IsSuccess && result.Delivery == DeliveryState.Accepted ? 0 : 1;

file sealed class DemoProvider : IRemoteProvider
{
    public const string ProviderId = "sdk-quickstart";
    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Experimental;

    public Task<RemoteResult> ExecuteAsync(
        DeviceRoute route,
        RemoteAction action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(route.ProviderId, Id, StringComparison.Ordinal))
            return Task.FromResult(RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable));

        return Task.FromResult(route.Capabilities.Contains(action)
            ? RemoteResult.Accepted()
            : RemoteResult.Failed(RemoteErrorCode.UnsupportedAction));
    }
}

file sealed class SingleDeviceRepository(Device device) : IDeviceRepository
{
    public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Device?>(id == device.Id ? device : null);
    }

    public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Device>>([device]);
    }
}
