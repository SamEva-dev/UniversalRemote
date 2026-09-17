using System.Collections.Concurrent;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

public sealed class InMemoryMediaHistoryRepository : IMediaHistoryRepository
{
    private readonly ConcurrentDictionary<MediaReference, MediaHistoryEntry> items = new();

    public Task<IReadOnlyList<MediaHistoryEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MediaHistoryEntry> result = items.Values.OrderByDescending(x => x.LastWatchedAt).ToArray();
        return Task.FromResult(result);
    }

    public Task<MediaHistoryEntry?> FindAsync(MediaReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        items.TryGetValue(reference, out var item);
        return Task.FromResult(item);
    }

    public Task SaveAsync(MediaHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);
        items[entry.Reference] = entry;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(MediaReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.TryRemove(reference, out _));
    }
}
