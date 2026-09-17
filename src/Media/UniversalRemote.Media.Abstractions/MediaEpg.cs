namespace UniversalRemote.Media.Abstractions;

/// <summary>
/// User-configured EPG source. The actual endpoint/token is stored behind CredentialReference and never
/// embedded in this model.
/// </summary>
public sealed record EpgSource
{
    public Guid Id { get; }
    public Guid MediaSourceId { get; }
    public string DisplayName { get; }
    public string ProviderId { get; }
    public string CredentialReference { get; }
    public bool IsEnabled { get; }

    public EpgSource(
        Guid id,
        Guid mediaSourceId,
        string displayName,
        string providerId,
        string credentialReference,
        bool isEnabled = true)
    {
        if (id == Guid.Empty) throw new ArgumentException("EPG source ID must not be empty.", nameof(id));
        if (mediaSourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(mediaSourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialReference);

        var normalizedName = displayName.Trim();
        var normalizedProvider = providerId.Trim();
        var normalizedCredentialReference = credentialReference.Trim();
        if (normalizedName.Length > 100) throw new ArgumentOutOfRangeException(nameof(displayName));
        if (normalizedProvider.Length > 80) throw new ArgumentOutOfRangeException(nameof(providerId));
        if (normalizedCredentialReference.Length > 200) throw new ArgumentOutOfRangeException(nameof(credentialReference));

        Id = id;
        MediaSourceId = mediaSourceId;
        DisplayName = normalizedName;
        ProviderId = normalizedProvider;
        CredentialReference = normalizedCredentialReference;
        IsEnabled = isEnabled;
    }
}

public sealed record EpgProgram
{
    public string ChannelGuideId { get; }
    public string Title { get; }
    public string? Description { get; }
    public string? Category { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset EndsAt { get; }
    public Uri? ArtworkUri { get; }

    public EpgProgram(
        string channelGuideId,
        string title,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        string? description = null,
        string? category = null,
        Uri? artworkUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelGuideId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (endsAt <= startsAt) throw new ArgumentException("EPG programme end must be after start.", nameof(endsAt));
        if (artworkUri is not null && !artworkUri.IsAbsoluteUri)
            throw new ArgumentException("Artwork URI must be absolute when specified.", nameof(artworkUri));

        ChannelGuideId = channelGuideId.Trim();
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        ArtworkUri = artworkUri;
    }
}

public sealed record EpgChannel
{
    public string GuideId { get; }
    public IReadOnlyList<string> DisplayNames { get; }
    public Uri? IconUri { get; }

    public EpgChannel(string guideId, IEnumerable<string> displayNames, Uri? iconUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guideId);
        ArgumentNullException.ThrowIfNull(displayNames);
        var names = displayNames
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        if (names.Length == 0) throw new ArgumentException("EPG channel requires at least one display name.", nameof(displayNames));
        if (iconUri is not null && !iconUri.IsAbsoluteUri)
            throw new ArgumentException("Channel icon URI must be absolute when specified.", nameof(iconUri));

        GuideId = guideId.Trim();
        DisplayNames = Array.AsReadOnly(names);
        IconUri = iconUri;
    }
}

public sealed record EpgDocumentSnapshot
{
    public IReadOnlyList<EpgChannel> Channels { get; }
    public IReadOnlyList<EpgProgram> Programs { get; }
    public DateTimeOffset RefreshedAt { get; }

    public EpgDocumentSnapshot(IEnumerable<EpgChannel> channels, IEnumerable<EpgProgram> programs, DateTimeOffset refreshedAt)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(programs);
        Channels = Array.AsReadOnly(channels.ToArray());
        Programs = Array.AsReadOnly(programs.ToArray());
        RefreshedAt = refreshedAt;
    }
}

public sealed record EpgGuideChannel
{
    public MediaItem Channel { get; }
    public string? MatchedGuideId { get; }
    public IReadOnlyList<EpgProgram> Programs { get; }

    public EpgGuideChannel(MediaItem channel, string? matchedGuideId, IEnumerable<EpgProgram> programs)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.Kind != MediaItemKind.LiveChannel)
            throw new ArgumentException("Only live channels can appear in an EPG guide.", nameof(channel));
        ArgumentNullException.ThrowIfNull(programs);
        Channel = channel;
        MatchedGuideId = string.IsNullOrWhiteSpace(matchedGuideId) ? null : matchedGuideId.Trim();
        Programs = Array.AsReadOnly(programs.OrderBy(static x => x.StartsAt).ToArray());
    }
}

public sealed record EpgGuideSnapshot
{
    public Guid MediaSourceId { get; }
    public Guid EpgSourceId { get; }
    public DateTimeOffset WindowStart { get; }
    public DateTimeOffset WindowEnd { get; }
    public IReadOnlyList<EpgGuideChannel> Channels { get; }
    public DateTimeOffset RefreshedAt { get; }

    public EpgGuideSnapshot(
        Guid mediaSourceId,
        Guid epgSourceId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IEnumerable<EpgGuideChannel> channels,
        DateTimeOffset refreshedAt)
    {
        if (mediaSourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(mediaSourceId));
        if (epgSourceId == Guid.Empty) throw new ArgumentException("EPG source ID must not be empty.", nameof(epgSourceId));
        if (windowEnd <= windowStart) throw new ArgumentException("EPG window end must be after start.", nameof(windowEnd));
        ArgumentNullException.ThrowIfNull(channels);
        MediaSourceId = mediaSourceId;
        EpgSourceId = epgSourceId;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        Channels = Array.AsReadOnly(channels.ToArray());
        RefreshedAt = refreshedAt;
    }
}

public sealed record EpgGuideRequest
{
    public DateTimeOffset WindowStart { get; }
    public DateTimeOffset WindowEnd { get; }
    public bool ForceRefresh { get; }

    public EpgGuideRequest(DateTimeOffset windowStart, DateTimeOffset windowEnd, bool forceRefresh = false)
    {
        if (windowEnd <= windowStart) throw new ArgumentException("EPG window end must be after start.", nameof(windowEnd));
        if (windowEnd - windowStart > TimeSpan.FromDays(14))
            throw new ArgumentOutOfRangeException(nameof(windowEnd), "EPG requests are limited to 14 days.");
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        ForceRefresh = forceRefresh;
    }
}

public interface IEpgProvider
{
    string Id { get; }
    Task<EpgDocumentSnapshot> GetAsync(EpgSource source, EpgGuideRequest request, CancellationToken cancellationToken = default);
}

public interface IEpgGuideService
{
    Task<EpgGuideSnapshot> GetGuideAsync(
        MediaSource mediaSource,
        EpgSource epgSource,
        EpgGuideRequest request,
        CancellationToken cancellationToken = default);
}

public interface IEpgCache
{
    Task<EpgGuideSnapshot?> GetAsync(Guid mediaSourceId, Guid epgSourceId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default);
    Task SetAsync(EpgGuideSnapshot snapshot, CancellationToken cancellationToken = default);
    Task ClearAsync(Guid mediaSourceId, Guid epgSourceId, CancellationToken cancellationToken = default);
}
