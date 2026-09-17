using System.Collections.Concurrent;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

public sealed class InMemoryMediaFavoriteRepository : IMediaFavoriteRepository
{
    private readonly ConcurrentDictionary<MediaReference, MediaFavorite> items = new();

    public Task<IReadOnlyList<MediaFavorite>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MediaFavorite> result = items.Values.OrderByDescending(x => x.AddedAt).ToArray();
        return Task.FromResult(result);
    }

    public Task<MediaFavorite?> FindAsync(MediaReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        items.TryGetValue(reference, out var item);
        return Task.FromResult(item);
    }

    public Task SaveAsync(MediaFavorite favorite, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(favorite);
        items[favorite.Reference] = favorite;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(MediaReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.TryRemove(reference, out _));
    }
}
