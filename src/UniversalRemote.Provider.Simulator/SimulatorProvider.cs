using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.Simulator;

/// <summary>Explicitly simulated transport. It never controls a real appliance.</summary>
public sealed class SimulatorProvider : IRemoteProvider
{
    public const string ProviderId = "simulator";
    public string Id => ProviderId;
    public SupportLevel SupportLevel => SupportLevel.Experimental;
    public Task<RemoteResult> ExecuteAsync(DeviceRoute route, RemoteAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (route.ProviderId != Id) return Task.FromResult(RemoteResult.Failed(RemoteErrorCode.ProviderUnavailable));
        return Task.FromResult(route.Capabilities.Contains(action)
            ? RemoteResult.Accepted() : RemoteResult.Failed(RemoteErrorCode.UnsupportedAction));
    }
}

/// <summary>Read-only demo data. Production persistence is deliberately deferred.</summary>
public sealed class DemoDeviceRepository : IDeviceRepository
{
    public static readonly Guid DemoDeviceId = Guid.Parse("b9aeb203-574d-4a81-9ed1-7e01dfed1901");
    private readonly Device device = new(DemoDeviceId, "TV de démonstration — simulation",
        [new DeviceRoute(SimulatorProvider.ProviderId, "demo-tv", [RemoteActions.PowerToggle, RemoteActions.VolumeUp, RemoteActions.VolumeDown, RemoteActions.Ok])]);
    public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Device?>(id == device.Id ? device : null);
    }
    public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Device>>(Array.AsReadOnly(new[] { device }));
    }
}
