using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Activities;

public enum MediaActivityTargetMode
{
    SelectedTarget,
    SpecificTarget
}

/// <summary>
/// Persistable link between an existing device-control Activity and one safe media reference.
/// Resolved stream URLs, provider credentials and transport payloads never belong in this model.
/// </summary>
public sealed record MediaActivityDefinition
{
    public Guid Id { get; }
    public string Name { get; }
    public Guid? PreparationActivityId { get; }
    public MediaReference Media { get; }
    public string MediaTitle { get; }
    public MediaActivityTargetMode TargetMode { get; }
    public string? TargetId { get; }
    public ActivityFailurePolicy PreparationFailurePolicy { get; }

    public MediaActivityDefinition(
        Guid id,
        string name,
        Guid? preparationActivityId,
        MediaReference media,
        string mediaTitle,
        MediaActivityTargetMode targetMode,
        string? targetId = null,
        ActivityFailurePolicy preparationFailurePolicy = ActivityFailurePolicy.StopOnFirstNonAccepted)
    {
        if (id == Guid.Empty) throw new ArgumentException("Media activity ID must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (preparationActivityId == Guid.Empty)
            throw new ArgumentException("Preparation Activity ID must not be empty when specified.", nameof(preparationActivityId));
        if (media.SourceId == Guid.Empty) throw new ArgumentException("Media reference is invalid.", nameof(media));
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaTitle);
        if (!Enum.IsDefined(targetMode)) throw new ArgumentOutOfRangeException(nameof(targetMode));
        if (!Enum.IsDefined(preparationFailurePolicy)) throw new ArgumentOutOfRangeException(nameof(preparationFailurePolicy));

        var normalizedName = name.Trim();
        var normalizedTitle = mediaTitle.Trim();
        var normalizedTargetId = string.IsNullOrWhiteSpace(targetId) ? null : targetId.Trim();

        if (normalizedName.Length > 80) throw new ArgumentOutOfRangeException(nameof(name), "Media activity name must be 80 characters or fewer.");
        if (normalizedTitle.Length > 250) throw new ArgumentOutOfRangeException(nameof(mediaTitle), "Media title must be 250 characters or fewer.");
        if (normalizedTargetId?.Length > 200) throw new ArgumentOutOfRangeException(nameof(targetId), "Playback target ID must be 200 characters or fewer.");
        if (targetMode == MediaActivityTargetMode.SpecificTarget && normalizedTargetId is null)
            throw new ArgumentException("A specific-target media activity requires a target ID.", nameof(targetId));
        if (targetMode == MediaActivityTargetMode.SelectedTarget && normalizedTargetId is not null)
            throw new ArgumentException("A selected-target media activity must not persist a target ID.", nameof(targetId));

        Id = id;
        Name = normalizedName;
        PreparationActivityId = preparationActivityId;
        Media = media;
        MediaTitle = normalizedTitle;
        TargetMode = targetMode;
        TargetId = normalizedTargetId;
        PreparationFailurePolicy = preparationFailurePolicy;
    }
}

public interface IMediaActivityRepository
{
    Task<MediaActivityDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaActivityDefinition>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaActivityDefinition> SaveAsync(MediaActivityDefinition activity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public enum MediaActivityPlaybackStatus
{
    NotAttempted,
    Started,
    Unavailable,
    Failed,
    Cancelled
}

public enum MediaActivityRunStatus
{
    Completed,
    CompletedWithIssues,
    StoppedBeforePlayback,
    PlaybackUnavailable,
    PlaybackFailed,
    Cancelled
}

/// <summary>Sanitized orchestration report. It never contains a resolved stream URI or provider secret.</summary>
public sealed record MediaActivityRunReport(
    Guid MediaActivityId,
    string MediaActivityName,
    MediaActivityRunStatus Status,
    ActivityRunReport? Preparation,
    MediaActivityPlaybackStatus PlaybackStatus,
    PlaybackSession? PlaybackSession,
    string? UserMessage);

public interface IMediaActivityRunner
{
    Task<MediaActivityRunReport> RunAsync(Guid mediaActivityId, CancellationToken cancellationToken = default);
    Task<MediaActivityRunReport> RunAsync(MediaActivityDefinition activity, CancellationToken cancellationToken = default);
}
