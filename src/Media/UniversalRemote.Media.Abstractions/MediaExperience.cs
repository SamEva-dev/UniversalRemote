namespace UniversalRemote.Media.Abstractions;

/// <summary>Stable provider-independent reference to a media item.</summary>
public readonly record struct MediaReference
{
    public Guid SourceId { get; }
    public string ExternalId { get; }
    public MediaItemKind Kind { get; }

    public MediaReference(Guid sourceId, string externalId, MediaItemKind kind)
    {
        if (sourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        SourceId = sourceId;
        ExternalId = externalId.Trim();
        Kind = kind;
    }

    public static MediaReference From(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new MediaReference(item.SourceId, item.ExternalId, item.Kind);
    }
}

/// <summary>Persistable favorite metadata. It deliberately excludes stream/artwork URLs and credentials.</summary>
public sealed record MediaFavorite
{
    public MediaReference Reference { get; }
    public string Title { get; }
    public string? Category { get; }
    public int? MinimumAge { get; }
    public DateTimeOffset AddedAt { get; }

    public MediaFavorite(MediaReference reference, string title, string? category, DateTimeOffset addedAt, int? minimumAge = null)
    {
        if (reference.SourceId == Guid.Empty) throw new ArgumentException("Favorite media reference is invalid.", nameof(reference));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Reference = reference;
        Title = title.Trim();
        if (minimumAge is < 0 or > 21) throw new ArgumentOutOfRangeException(nameof(minimumAge));
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        MinimumAge = minimumAge;
        AddedAt = addedAt;
    }

    public static MediaFavorite From(MediaItem item, DateTimeOffset addedAt)
        => new(MediaReference.From(item), item.Title, item.Category, addedAt, item.MinimumAge);
}

/// <summary>Safe playback history entry used for recent and continue-watching experiences.</summary>
public sealed record MediaHistoryEntry
{
    public MediaReference Reference { get; }
    public string Title { get; }
    public TimeSpan Position { get; }
    public TimeSpan? Duration { get; }
    public DateTimeOffset LastWatchedAt { get; }
    public bool Completed { get; }
    public string? Category { get; }
    public int? MinimumAge { get; }

    public MediaHistoryEntry(
        MediaReference reference,
        string title,
        TimeSpan position,
        TimeSpan? duration,
        DateTimeOffset lastWatchedAt,
        bool completed,
        string? category = null,
        int? minimumAge = null)
    {
        if (reference.SourceId == Guid.Empty) throw new ArgumentException("History media reference is invalid.", nameof(reference));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (position < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        Reference = reference;
        Title = title.Trim();
        Position = position;
        Duration = duration;
        if (minimumAge is < 0 or > 21) throw new ArgumentOutOfRangeException(nameof(minimumAge));
        LastWatchedAt = lastWatchedAt;
        Completed = completed;
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        MinimumAge = minimumAge;
    }

    public double Progress
        => Duration is { } duration && duration > TimeSpan.Zero
            ? Math.Clamp(Position.TotalSeconds / duration.TotalSeconds, 0d, 1d)
            : 0d;
}

public sealed record MediaCatalogEntry
{
    public MediaSource Source { get; }
    public MediaItem Item { get; }

    public MediaCatalogEntry(MediaSource source, MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(item);
        if (item.SourceId != source.Id)
            throw new ArgumentException("Media item does not belong to the supplied source.", nameof(item));
        Source = source;
        Item = item;
    }
}

public sealed record MediaBrowseResult(
    IReadOnlyList<MediaCatalogEntry> Items,
    int LoadedSourceCount,
    int FailedSourceCount);

public sealed record MediaDetails(
    MediaSource Source,
    MediaItem Item,
    bool IsFavorite,
    MediaHistoryEntry? History);

public interface IMediaFavoriteRepository
{
    Task<IReadOnlyList<MediaFavorite>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaFavorite?> FindAsync(MediaReference reference, CancellationToken cancellationToken = default);
    Task SaveAsync(MediaFavorite favorite, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(MediaReference reference, CancellationToken cancellationToken = default);
}

public interface IMediaHistoryRepository
{
    Task<IReadOnlyList<MediaHistoryEntry>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaHistoryEntry?> FindAsync(MediaReference reference, CancellationToken cancellationToken = default);
    Task SaveAsync(MediaHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(MediaReference reference, CancellationToken cancellationToken = default);
}

public interface IMediaBrowseService
{
    Task<MediaBrowseResult> BrowseAsync(
        IEnumerable<MediaItemKind> kinds,
        CancellationToken cancellationToken = default);
}

public interface IMediaSearchService
{
    Task<MediaBrowseResult> SearchAsync(
        string query,
        IEnumerable<MediaItemKind>? kinds = null,
        int maxResults = 100,
        CancellationToken cancellationToken = default);
}

public interface IMediaSeriesService
{
    Task<MediaSeriesDetails?> GetSeriesAsync(
        MediaSource source,
        MediaItem series,
        CancellationToken cancellationToken = default);
}

public interface IMediaLibraryService
{
    Task<IReadOnlyList<MediaFavorite>> GetFavoritesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaHistoryEntry>> GetHistoryAsync(int maxItems = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaHistoryEntry>> GetContinueWatchingAsync(int maxItems = 20, CancellationToken cancellationToken = default);
    Task<MediaDetails> GetDetailsAsync(MediaSource source, MediaItem item, CancellationToken cancellationToken = default);
    Task<bool> ToggleFavoriteAsync(MediaItem item, CancellationToken cancellationToken = default);
    Task<MediaCatalogEntry?> ResolveAsync(MediaReference reference, CancellationToken cancellationToken = default);
}
