using System.Collections.Concurrent;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Core;

/// <summary>Thread-safe volatile registry used by samples and the first MAUI client.</summary>
public sealed class InMemoryDeviceRepository : IDeviceRepository, IDeviceRegistrar
{
    private readonly ConcurrentDictionary<Guid, Device> devices = new();

    public InMemoryDeviceRepository(IEnumerable<Device>? seed = null)
    {
        if (seed is null) return;
        foreach (var device in seed) devices[device.Id] = device;
    }

    public Task<Device?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        devices.TryGetValue(id, out var device);
        return Task.FromResult(device);
    }

    public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Device> result = devices.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.FromResult(result);
    }

    public Task UpsertAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        devices[device.Id] = device;
        return Task.CompletedTask;
    }
}
