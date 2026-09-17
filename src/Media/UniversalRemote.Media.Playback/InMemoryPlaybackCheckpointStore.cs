using System.Collections.Concurrent;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Playback;

public sealed class InMemoryPlaybackCheckpointStore : IPlaybackCheckpointStore
{
    private readonly ConcurrentDictionary<string, TimeSpan> positions = new(StringComparer.Ordinal);

    public Task<TimeSpan?> LoadAsync(Guid sourceId, string mediaExternalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(sourceId, mediaExternalId);
        return Task.FromResult<TimeSpan?>(positions.TryGetValue(Key(sourceId, mediaExternalId), out var value) ? value : null);
    }

    public Task SaveAsync(Guid sourceId, string mediaExternalId, TimeSpan position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(sourceId, mediaExternalId);
        if (position < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));
        positions[Key(sourceId, mediaExternalId)] = position;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid sourceId, string mediaExternalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(sourceId, mediaExternalId);
        positions.TryRemove(Key(sourceId, mediaExternalId), out _);
        return Task.CompletedTask;
    }

    private static string Key(Guid sourceId, string mediaExternalId) => $"{sourceId:N}|{mediaExternalId.Trim()}";

    private static void Validate(Guid sourceId, string mediaExternalId)
    {
        if (sourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaExternalId);
    }
}
