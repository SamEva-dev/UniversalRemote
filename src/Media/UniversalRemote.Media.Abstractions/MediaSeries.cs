namespace UniversalRemote.Media.Abstractions;

/// <summary>One normalized season of a series. Provider-specific IDs and stream URLs stay out of this model.</summary>
public sealed record MediaSeason
{
    public int SeasonNumber { get; }
    public string Title { get; }
    public Uri? ArtworkUri { get; }
    public IReadOnlyList<MediaEpisode> Episodes { get; }

    public MediaSeason(
        int seasonNumber,
        string title,
        IEnumerable<MediaEpisode> episodes,
        Uri? artworkUri = null)
    {
        if (seasonNumber < 0) throw new ArgumentOutOfRangeException(nameof(seasonNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(episodes);
        if (artworkUri is not null && !artworkUri.IsAbsoluteUri)
            throw new ArgumentException("Season artwork URI must be absolute when specified.", nameof(artworkUri));

        var snapshot = episodes.ToArray();
        if (snapshot.Any(x => x is null))
            throw new ArgumentException("Season episode collection must not contain null values.", nameof(episodes));
        if (snapshot.Any(x => x.SeasonNumber != seasonNumber))
            throw new ArgumentException("Every episode must belong to the declared season.", nameof(episodes));

        SeasonNumber = seasonNumber;
        Title = title.Trim();
        ArtworkUri = artworkUri;
        Episodes = Array.AsReadOnly(snapshot.OrderBy(x => x.EpisodeNumber).ToArray());
    }
}

/// <summary>Normalized episode metadata. Stream resolution remains a later playback concern.</summary>
public sealed record MediaEpisode
{
    public Guid SourceId { get; }
    public string SeriesExternalId { get; }
    public string ExternalId { get; }
    public string Title { get; }
    public int SeasonNumber { get; }
    public int EpisodeNumber { get; }
    public TimeSpan? Duration { get; }
    public Uri? ArtworkUri { get; }

    public MediaEpisode(
        Guid sourceId,
        string seriesExternalId,
        string externalId,
        string title,
        int seasonNumber,
        int episodeNumber,
        TimeSpan? duration = null,
        Uri? artworkUri = null)
    {
        if (sourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (seasonNumber < 0) throw new ArgumentOutOfRangeException(nameof(seasonNumber));
        if (episodeNumber < 0) throw new ArgumentOutOfRangeException(nameof(episodeNumber));
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (artworkUri is not null && !artworkUri.IsAbsoluteUri)
            throw new ArgumentException("Episode artwork URI must be absolute when specified.", nameof(artworkUri));

        SourceId = sourceId;
        SeriesExternalId = seriesExternalId.Trim();
        ExternalId = externalId.Trim();
        Title = title.Trim();
        SeasonNumber = seasonNumber;
        EpisodeNumber = episodeNumber;
        Duration = duration;
        ArtworkUri = artworkUri;
    }
}

/// <summary>Provider-independent series details loaded on demand to avoid expanding every series during catalogue refresh.</summary>
public sealed record MediaSeriesDetails
{
    public Guid SourceId { get; }
    public string SeriesExternalId { get; }
    public string Title { get; }
    public IReadOnlyList<MediaSeason> Seasons { get; }

    public MediaSeriesDetails(
        Guid sourceId,
        string seriesExternalId,
        string title,
        IEnumerable<MediaSeason> seasons)
    {
        if (sourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(seasons);

        var normalizedSeriesExternalId = seriesExternalId.Trim();
        var snapshot = seasons.ToArray();
        if (snapshot.Any(x => x is null))
            throw new ArgumentException("Series season collection must not contain null values.", nameof(seasons));
        if (snapshot.SelectMany(x => x.Episodes).Any(x => x.SourceId != sourceId))
            throw new ArgumentException("Every episode must belong to the requested media source.", nameof(seasons));
        if (snapshot.SelectMany(x => x.Episodes).Any(x => !string.Equals(x.SeriesExternalId, normalizedSeriesExternalId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Every episode must belong to the requested series.", nameof(seasons));

        SourceId = sourceId;
        SeriesExternalId = normalizedSeriesExternalId;
        Title = title.Trim();
        Seasons = Array.AsReadOnly(snapshot.OrderBy(x => x.SeasonNumber).ToArray());
    }
}

/// <summary>Optional capability implemented by providers that can expose seasons and episodes on demand.</summary>
public interface IMediaSeriesProvider : IMediaProvider
{
    Task<MediaSeriesDetails> GetSeriesAsync(
        MediaSource source,
        string seriesExternalId,
        CancellationToken cancellationToken = default);
}
