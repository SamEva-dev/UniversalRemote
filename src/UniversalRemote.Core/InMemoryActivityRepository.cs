using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Core;

/// <summary>Process-local Activity store for samples and tests that do not opt into SQLite.</summary>
public sealed class InMemoryActivityRepository : IActivityRepository
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Activity> items = [];

    public Task<Activity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
            return Task.FromResult(items.TryGetValue(id, out var activity) ? activity : null);
    }

    public Task<IReadOnlyList<Activity>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
            return Task.FromResult<IReadOnlyList<Activity>>(items.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).ToArray());
    }

    public Task<Activity> SaveAsync(Activity activity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(activity);
        lock (gate)
        {
            items[activity.Id] = activity;
            return Task.FromResult(activity);
        }
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(items.Remove(id));
    }
}
