using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

/// <summary>Process-local fallback for tests and lightweight samples. Stores metadata only, never credentials.</summary>
public sealed class InMemoryMediaSourceRepository : IMediaSourceRepository
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, MediaSource> sources = [];

    public Task<MediaSource?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id == Guid.Empty) return Task.FromResult<MediaSource?>(null);
        lock (gate)
        {
            return Task.FromResult(sources.GetValueOrDefault(id));
        }
    }

    public Task<IReadOnlyList<MediaSource>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<MediaSource>>(
                sources.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    public Task<MediaSource> SaveAsync(MediaSource source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        lock (gate)
        {
            sources[source.Id] = source;
            return Task.FromResult(source);
        }
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id == Guid.Empty) return Task.FromResult(false);
        lock (gate)
        {
            return Task.FromResult(sources.Remove(id));
        }
    }
}
