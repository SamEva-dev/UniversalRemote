using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Core;

/// <summary>Process-local fallback used by tests and lightweight samples.</summary>
public sealed class InMemoryFavoriteRepository : IFavoriteRepository
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, List<Favorite>> items = [];

    public Task<IReadOnlyList<Favorite>> ListAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (deviceId == Guid.Empty) return Task.FromResult<IReadOnlyList<Favorite>>(Array.Empty<Favorite>());
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<Favorite>>(
                items.TryGetValue(deviceId, out var list)
                    ? list.OrderBy(x => x.Position).ToArray()
                    : Array.Empty<Favorite>());
        }
    }

    public Task<Favorite> AddAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (deviceId == Guid.Empty) throw new ArgumentException("Favorite device ID must not be empty.", nameof(deviceId));
        ArgumentNullException.ThrowIfNull(action);

        lock (gate)
        {
            if (!items.TryGetValue(deviceId, out var list))
            {
                list = [];
                items.Add(deviceId, list);
            }

            var existing = list.FirstOrDefault(x => string.Equals(x.Action.Id, action.Id, StringComparison.Ordinal));
            if (existing is not null) return Task.FromResult(existing);

            var favorite = new Favorite(deviceId, action, list.Count == 0 ? 0 : list.Max(x => x.Position) + 1);
            list.Add(favorite);
            return Task.FromResult(favorite);
        }
    }

    public Task<bool> RemoveAsync(Guid deviceId, RemoteAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(action);
        lock (gate)
        {
            if (!items.TryGetValue(deviceId, out var list)) return Task.FromResult(false);
            var removed = list.RemoveAll(x => string.Equals(x.Action.Id, action.Id, StringComparison.Ordinal)) > 0;
            if (list.Count == 0) items.Remove(deviceId);
            return Task.FromResult(removed);
        }
    }
}
