using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Activities;

/// <summary>
/// Orchestrates an optional device-control Activity before launching one media item. It deliberately reuses the
/// existing Activity runner instead of introducing a second macro engine.
/// </summary>
public sealed class MediaActivityRunner(
    IMediaActivityRepository mediaActivities,
    IActivityRepository activities,
    IActivityRunner activityRunner,
    IMediaLibraryService mediaLibrary,
    IPlaybackTargetDiscovery targetDiscovery,
    IPlaybackTargetSelection targetSelection,
    IPlaybackService playback) : IMediaActivityRunner
{
    public async Task<MediaActivityRunReport> RunAsync(Guid mediaActivityId, CancellationToken cancellationToken = default)
    {
        if (mediaActivityId == Guid.Empty) throw new ArgumentException("Media activity ID must not be empty.", nameof(mediaActivityId));
        var activity = await mediaActivities.FindAsync(mediaActivityId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Media activity not found.");
        return await RunAsync(activity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaActivityRunReport> RunAsync(MediaActivityDefinition activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ActivityRunReport? preparation = null;
        var preparationHasIssues = false;

        if (activity.PreparationActivityId is { } preparationId)
        {
            var deviceActivity = await activities.FindAsync(preparationId, cancellationToken).ConfigureAwait(false);
            if (deviceActivity is null)
            {
                return Report(activity, MediaActivityRunStatus.StoppedBeforePlayback, null,
                    MediaActivityPlaybackStatus.NotAttempted, null,
                    "La séquence de préparation n’est plus disponible.");
            }

            preparation = await activityRunner.RunAsync(
                deviceActivity,
                activity.PreparationFailurePolicy,
                cancellationToken).ConfigureAwait(false);

            if (preparation.Status == ActivityRunStatus.Cancelled)
                return Report(activity, MediaActivityRunStatus.Cancelled, preparation,
                    MediaActivityPlaybackStatus.Cancelled, null, "Le scénario a été annulé.");

            if (preparation.Status == ActivityRunStatus.StoppedOnNonAccepted)
                return Report(activity, MediaActivityRunStatus.StoppedBeforePlayback, preparation,
                    MediaActivityPlaybackStatus.NotAttempted, null,
                    "La préparation a été arrêtée avant le lancement du média.");

            preparationHasIssues = preparation.Status == ActivityRunStatus.CompletedWithIssues;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var entry = await mediaLibrary.ResolveAsync(activity.Media, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Report(activity, MediaActivityRunStatus.PlaybackUnavailable, preparation,
                MediaActivityPlaybackStatus.Unavailable, null,
                "Le contenu n’est plus disponible dans sa source média.");
        }

        var candidate = await ResolveTargetAsync(activity, cancellationToken).ConfigureAwait(false);
        if (candidate is null || !candidate.CanLaunch || !candidate.Target.Capabilities.Contains(PlaybackCapability.Start))
        {
            return Report(activity, MediaActivityRunStatus.PlaybackUnavailable, preparation,
                MediaActivityPlaybackStatus.Unavailable, null,
                "La cible choisie ne permet pas encore de lancer ce média.");
        }

        try
        {
            var session = await playback.StartAsync(entry.Source, entry.Item, candidate.Target, cancellationToken).ConfigureAwait(false);
            if (session.State == PlaybackSessionState.Failed)
            {
                return Report(activity, MediaActivityRunStatus.PlaybackFailed, preparation,
                    MediaActivityPlaybackStatus.Failed, session,
                    session.UserMessage ?? "Le lecteur n’a pas pu démarrer le média.");
            }

            return Report(
                activity,
                preparationHasIssues ? MediaActivityRunStatus.CompletedWithIssues : MediaActivityRunStatus.Completed,
                preparation,
                MediaActivityPlaybackStatus.Started,
                session,
                preparationHasIssues
                    ? "Le média a démarré, avec au moins une commande de préparation non confirmée."
                    : "Le média a démarré.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Report(activity, MediaActivityRunStatus.Cancelled, preparation,
                MediaActivityPlaybackStatus.Cancelled, null, "Le scénario a été annulé.");
        }
        catch
        {
            return Report(activity, MediaActivityRunStatus.PlaybackFailed, preparation,
                MediaActivityPlaybackStatus.Failed, null,
                "Le lecteur n’a pas pu démarrer le média.");
        }
    }

    private async Task<PlaybackTargetCandidate?> ResolveTargetAsync(
        MediaActivityDefinition activity,
        CancellationToken cancellationToken)
    {
        var desired = activity.TargetMode == MediaActivityTargetMode.SelectedTarget
            ? targetSelection.Selected.Id
            : activity.TargetId!;

        var candidates = await targetDiscovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        return candidates.FirstOrDefault(x => string.Equals(x.Target.Id, desired, StringComparison.Ordinal));
    }

    private static MediaActivityRunReport Report(
        MediaActivityDefinition activity,
        MediaActivityRunStatus status,
        ActivityRunReport? preparation,
        MediaActivityPlaybackStatus playbackStatus,
        PlaybackSession? playbackSession,
        string? message)
        => new(activity.Id, activity.Name, status, preparation, playbackStatus, playbackSession, message);
}
