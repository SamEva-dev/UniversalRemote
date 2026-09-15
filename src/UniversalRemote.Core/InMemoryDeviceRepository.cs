using System.Collections.Concurrent;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Core;

/// <summary>Thread-safe volatile registry used by samples and tests. Production MAUI uses SQLite.</summary>
public sealed class InMemoryDeviceRepository : IDeviceRepository, IDeviceRegistrar
{
    private readonly ConcurrentDictionary<Guid, Device> devices = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);

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

    public async Task UpsertAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { devices[device.Id] = device; }
        finally { writeGate.Release(); }
    }

    public async Task<Device> RegisterPairingAsync(
        string displayName,
        DeviceRoute route,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(route);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = devices.Values.FirstOrDefault(device => device.Routes.Any(existingRoute => SameRoute(existingRoute, route)));
            Device updated;
            if (existing is null)
            {
                updated = new Device(Guid.NewGuid(), displayName, [route]);
            }
            else
            {
                var routes = existing.Routes.Select(existingRoute => SameRoute(existingRoute, route) ? route : existingRoute).ToArray();
                updated = new Device(existing.Id, displayName, routes);
            }

            devices[updated.Id] = updated;
            return updated;
        }
        finally { writeGate.Release(); }
    }

    private static bool SameRoute(DeviceRoute left, DeviceRoute right)
        => string.Equals(left.ProviderId, right.ProviderId, StringComparison.Ordinal)
           && string.Equals(left.DeviceKey, right.DeviceKey, StringComparison.Ordinal);
}
