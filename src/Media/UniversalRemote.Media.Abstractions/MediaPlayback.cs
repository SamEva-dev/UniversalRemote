namespace UniversalRemote.Media.Abstractions;

/// <summary>
/// Ephemeral stream descriptor returned at playback time. The URI may contain provider credentials or signed
/// query parameters and therefore must never be persisted, logged or rendered in diagnostics.
/// </summary>
public sealed class ResolvedMediaStream
{
    public Uri StreamUri { get; }
    public string? ContentType { get; }
    public bool IsLive { get; }
    public DateTimeOffset? ExpiresAt { get; }

    public ResolvedMediaStream(Uri streamUri, bool isLive, string? contentType = null, DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        if (!streamUri.IsAbsoluteUri)
            throw new ArgumentException("Playback stream URI must be absolute.", nameof(streamUri));

        StreamUri = streamUri;
        IsLive = isLive;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim();
        ExpiresAt = expiresAt;
    }

    public override string ToString() => $"ResolvedMediaStream(Scheme={StreamUri.Scheme}, Live={IsLive}, Uri=[REDACTED])";
}

/// <summary>
/// Provider boundary that resolves the actual stream only when playback is requested. Implementations must not
/// place the resolved URI in exceptions, logs, telemetry or persisted models.
/// </summary>
public interface IStreamResolver
{
    string ProviderId { get; }

    Task<ResolvedMediaStream> ResolveAsync(
        MediaSource source,
        MediaItem item,
        CancellationToken cancellationToken = default);
}

public enum PlaybackSessionState
{
    Resolving,
    Opening,
    Buffering,
    Playing,
    Paused,
    Stopped,
    Completed,
    Failed
}

/// <summary>
/// Provider-independent playback state. It intentionally contains no stream URI or credentials.
/// </summary>
public sealed record PlaybackSession
{
    public Guid SessionId { get; }
    public Guid SourceId { get; }
    public string MediaExternalId { get; }
    public MediaItemKind MediaKind { get; }
    public string Title { get; }
    public string TargetId { get; }
    public PlaybackSessionState State { get; }
    public TimeSpan Position { get; }
    public TimeSpan? Duration { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public string? FailureCode { get; }
    public string? UserMessage { get; }
    public string? Category { get; }
    public int? MinimumAge { get; }

    public PlaybackSession(
        Guid sessionId,
        Guid sourceId,
        string mediaExternalId,
        MediaItemKind mediaKind,
        string title,
        string targetId,
        PlaybackSessionState state,
        TimeSpan position,
        TimeSpan? duration,
        DateTimeOffset startedAt,
        DateTimeOffset updatedAt,
        string? failureCode = null,
        string? userMessage = null,
        string? category = null,
        int? minimumAge = null)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Playback session ID must not be empty.", nameof(sessionId));
        if (sourceId == Guid.Empty) throw new ArgumentException("Media source ID must not be empty.", nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (!Enum.IsDefined(mediaKind)) throw new ArgumentOutOfRangeException(nameof(mediaKind));
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (position < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (updatedAt < startedAt) throw new ArgumentException("Playback update time cannot precede the session start.", nameof(updatedAt));

        SessionId = sessionId;
        SourceId = sourceId;
        MediaExternalId = mediaExternalId.Trim();
        MediaKind = mediaKind;
        Title = title.Trim();
        TargetId = targetId.Trim();
        State = state;
        Position = position;
        Duration = duration;
        StartedAt = startedAt;
        UpdatedAt = updatedAt;
        if (minimumAge is < 0 or > 21) throw new ArgumentOutOfRangeException(nameof(minimumAge));
        FailureCode = NormalizeOptional(failureCode, 80);
        UserMessage = NormalizeOptional(userMessage, 300);
        Category = NormalizeOptional(category, 150);
        MinimumAge = minimumAge;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

public sealed class PlaybackSessionChangedEventArgs : EventArgs
{
    public PlaybackSession Session { get; }

    public PlaybackSessionChangedEventArgs(PlaybackSession session)
        => Session = session ?? throw new ArgumentNullException(nameof(session));
}

/// <summary>Stores only resumable position metadata; implementations must never persist a resolved stream URI.</summary>
public interface IPlaybackCheckpointStore
{
    Task<TimeSpan?> LoadAsync(Guid sourceId, string mediaExternalId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid sourceId, string mediaExternalId, TimeSpan position, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid sourceId, string mediaExternalId, CancellationToken cancellationToken = default);
}

public interface IPlaybackService
{
    PlaybackSession? CurrentSession { get; }
    event EventHandler<PlaybackSessionChangedEventArgs>? SessionChanged;

    Task<PlaybackSession> StartAsync(
        MediaSource source,
        MediaItem item,
        IPlaybackTarget target,
        CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Well-known targets that do not require remote discovery.</summary>
public static class PlaybackTargets
{
    public static IPlaybackTarget LocalDevice { get; } = new PlaybackTarget(
        "local-device",
        "Cet appareil",
        PlaybackTargetKind.LocalDevice,
        [
            PlaybackCapability.Start,
            PlaybackCapability.Stop,
            PlaybackCapability.Pause,
            PlaybackCapability.Resume,
            PlaybackCapability.Seek
        ]);
}
