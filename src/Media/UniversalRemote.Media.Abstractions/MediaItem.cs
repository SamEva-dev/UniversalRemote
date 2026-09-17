namespace UniversalRemote.Media.Abstractions;

public enum MediaItemKind
{
    LiveChannel,
    Movie,
    Series,
    Episode
}

/// <summary>
/// Provider-independent catalogue item. It intentionally contains no stream URL or credential.
/// Stream resolution happens only when playback is requested.
/// </summary>
public sealed record MediaItem
{
    public Guid SourceId { get; }
    public string ExternalId { get; }
    public MediaItemKind Kind { get; }
    public string Title { get; }
    public string? Category { get; }
    public TimeSpan? Duration { get; }
    public Uri? ArtworkUri { get; }
    public string? GuideId { get; }
    public int? MinimumAge { get; }

    public MediaItem(
        Guid sourceId,
        string externalId,
        MediaItemKind kind,
        string title,
        string? category = null,
        TimeSpan? duration = null,
        Uri? artworkUri = null,
        string? guideId = null,
        int? minimumAge = null)
    {
        if (sourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), "Duration must not be negative.");
        if (artworkUri is not null && !artworkUri.IsAbsoluteUri)
            throw new ArgumentException("Artwork URI must be absolute when specified.", nameof(artworkUri));
        if (minimumAge is < 0 or > 21)
            throw new ArgumentOutOfRangeException(nameof(minimumAge), "Minimum age must be between 0 and 21 when specified.");

        var normalizedExternalId = externalId.Trim();
        var normalizedTitle = title.Trim();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var normalizedGuideId = string.IsNullOrWhiteSpace(guideId) ? null : guideId.Trim();

        if (normalizedExternalId.Length > 300)
            throw new ArgumentOutOfRangeException(nameof(externalId), "External media ID must be 300 characters or fewer.");
        if (normalizedTitle.Length > 250)
            throw new ArgumentOutOfRangeException(nameof(title), "Media title must be 250 characters or fewer.");
        if (normalizedCategory?.Length > 150)
            throw new ArgumentOutOfRangeException(nameof(category), "Media category must be 150 characters or fewer.");
        if (normalizedGuideId?.Length > 300)
            throw new ArgumentOutOfRangeException(nameof(guideId), "Guide ID must be 300 characters or fewer.");

        SourceId = sourceId;
        ExternalId = normalizedExternalId;
        Kind = kind;
        Title = normalizedTitle;
        Category = normalizedCategory;
        Duration = duration;
        ArtworkUri = artworkUri;
        GuideId = normalizedGuideId;
        MinimumAge = minimumAge;
    }
}
