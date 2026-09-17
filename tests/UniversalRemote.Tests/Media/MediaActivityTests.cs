using UniversalRemote.Abstractions;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Activities;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class MediaActivityTests
{
    [Fact]
    public async Task Runner_executes_preparation_then_starts_media()
    {
        var source = new MediaSource(Guid.NewGuid(), "Test", "test");
        var item = new MediaItem(source.Id, "movie-1", MediaItemKind.Movie, "Film test");
        var preparation = new Activity(Guid.NewGuid(), "Préparer TV");
        var definition = Definition(item, preparation.Id, PlaybackTargets.LocalDevice.Id);
        var playback = new FakePlaybackService();
        var runner = BuildRunner(
            definition,
            preparation,
            ActivityReport(preparation, ActivityRunStatus.Completed),
            new MediaCatalogEntry(source, item),
            [Launchable(PlaybackTargets.LocalDevice)],
            playback);

        var report = await runner.RunAsync(definition.Id);

        Assert.Equal(MediaActivityRunStatus.Completed, report.Status);
        Assert.Equal(MediaActivityPlaybackStatus.Started, report.PlaybackStatus);
        Assert.Equal(1, playback.StartCount);
        Assert.Equal(PlaybackTargets.LocalDevice.Id, playback.LastTargetId);
    }

    [Fact]
    public async Task Runner_does_not_start_media_when_preparation_stops()
    {
        var source = new MediaSource(Guid.NewGuid(), "Test", "test");
        var item = new MediaItem(source.Id, "live-1", MediaItemKind.LiveChannel, "Chaîne test");
        var preparation = new Activity(Guid.NewGuid(), "Préparer TV");
        var definition = Definition(item, preparation.Id, PlaybackTargets.LocalDevice.Id);
        var playback = new FakePlaybackService();
        var runner = BuildRunner(
            definition,
            preparation,
            ActivityReport(preparation, ActivityRunStatus.StoppedOnNonAccepted),
            new MediaCatalogEntry(source, item),
            [Launchable(PlaybackTargets.LocalDevice)],
            playback);

        var report = await runner.RunAsync(definition.Id);

        Assert.Equal(MediaActivityRunStatus.StoppedBeforePlayback, report.Status);
        Assert.Equal(MediaActivityPlaybackStatus.NotAttempted, report.PlaybackStatus);
        Assert.Equal(0, playback.StartCount);
    }

    [Fact]
    public async Task Runner_preserves_partial_preparation_issue_and_still_launches_when_policy_allows_it()
    {
        var source = new MediaSource(Guid.NewGuid(), "Test", "test");
        var item = new MediaItem(source.Id, "movie-2", MediaItemKind.Movie, "Film test 2");
        var preparation = new Activity(Guid.NewGuid(), "Préparer TV");
        var definition = new MediaActivityDefinition(
            Guid.NewGuid(), "Cinéma", preparation.Id, MediaReference.From(item), item.Title,
            MediaActivityTargetMode.SpecificTarget, PlaybackTargets.LocalDevice.Id,
            ActivityFailurePolicy.ContinueAfterNonAccepted);
        var playback = new FakePlaybackService();
        var runner = BuildRunner(
            definition,
            preparation,
            ActivityReport(preparation, ActivityRunStatus.CompletedWithIssues),
            new MediaCatalogEntry(source, item),
            [Launchable(PlaybackTargets.LocalDevice)],
            playback);

        var report = await runner.RunAsync(definition.Id);

        Assert.Equal(MediaActivityRunStatus.CompletedWithIssues, report.Status);
        Assert.Equal(MediaActivityPlaybackStatus.Started, report.PlaybackStatus);
        Assert.Equal(1, playback.StartCount);
    }

    [Fact]
    public async Task Runner_refuses_preview_target_that_cannot_launch()
    {
        var source = new MediaSource(Guid.NewGuid(), "Test", "test");
        var item = new MediaItem(source.Id, "movie-3", MediaItemKind.Movie, "Film test 3");
        var remoteTarget = new PlaybackTarget(
            "androidtv:preview",
            "TV Salon",
            PlaybackTargetKind.AndroidTv,
            [PlaybackCapability.Pause]);
        var definition = Definition(item, null, remoteTarget.Id);
        var playback = new FakePlaybackService();
        var runner = BuildRunner(
            definition,
            null,
            null,
            new MediaCatalogEntry(source, item),
            [new PlaybackTargetCandidate { Target = remoteTarget, CanLaunch = false, Status = "Preview" }],
            playback);

        var report = await runner.RunAsync(definition.Id);

        Assert.Equal(MediaActivityRunStatus.PlaybackUnavailable, report.Status);
        Assert.Equal(0, playback.StartCount);
    }

    [Fact]
    public async Task File_repository_roundtrips_safe_definition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ur-media-activities-{Guid.NewGuid():N}.json");
        try
        {
            using var repository = new FileMediaActivityRepository(path);
            var reference = new MediaReference(Guid.NewGuid(), "vod-42", MediaItemKind.Movie);
            var definition = new MediaActivityDefinition(
                Guid.NewGuid(), "Soirée cinéma", null, reference, "Film 42",
                MediaActivityTargetMode.SpecificTarget, "local-device");

            await repository.SaveAsync(definition);
            var loaded = await repository.FindAsync(definition.Id);
            var raw = await File.ReadAllTextAsync(path);

            Assert.NotNull(loaded);
            Assert.Equal(definition.Media, loaded!.Media);
            Assert.Equal(definition.TargetId, loaded.TargetId);
            Assert.DoesNotContain("StreamUri", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Credential", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }

    private static MediaActivityDefinition Definition(MediaItem item, Guid? preparationId, string targetId)
        => new(
            Guid.NewGuid(),
            "Regarder " + item.Title,
            preparationId,
            MediaReference.From(item),
            item.Title,
            MediaActivityTargetMode.SpecificTarget,
            targetId);

    private static MediaActivityRunner BuildRunner(
        MediaActivityDefinition definition,
        Activity? preparation,
        ActivityRunReport? preparationReport,
        MediaCatalogEntry? media,
        IReadOnlyList<PlaybackTargetCandidate> targets,
        FakePlaybackService playback)
    {
        var activityRepo = new FakeActivityRepository(preparation);
        var activityRunner = new FakeActivityRunner(preparationReport);
        return new MediaActivityRunner(
            new FakeMediaActivityRepository(definition),
            activityRepo,
            activityRunner,
            new FakeMediaLibraryService(media),
            new FakeTargetDiscovery(targets),
            new PlaybackTargetSelection(),
            playback);
    }

    private static ActivityRunReport ActivityReport(Activity activity, ActivityRunStatus status)
        => new(activity.Id, activity.Name, ActivityFailurePolicy.StopOnFirstNonAccepted, status, []);

    private static PlaybackTargetCandidate Launchable(IPlaybackTarget target)
        => new() { Target = target, CanLaunch = true, Status = "Disponible" };

    private sealed class FakeMediaActivityRepository(MediaActivityDefinition definition) : IMediaActivityRepository
    {
        public Task<MediaActivityDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<MediaActivityDefinition?>(id == definition.Id ? definition : null);
        public Task<IReadOnlyList<MediaActivityDefinition>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MediaActivityDefinition>>([definition]);
        public Task<MediaActivityDefinition> SaveAsync(MediaActivityDefinition activity, CancellationToken cancellationToken = default)
            => Task.FromResult(activity);
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeActivityRepository(Activity? activity) : IActivityRepository
    {
        public Task<Activity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(activity is not null && activity.Id == id ? activity : null);
        public Task<IReadOnlyList<Activity>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Activity>>(activity is null ? [] : [activity]);
        public Task<Activity> SaveAsync(Activity value, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeActivityRunner(ActivityRunReport? report) : IActivityRunner
    {
        public Task<ActivityRunReport> RunAsync(Activity activity, ActivityFailurePolicy failurePolicy = ActivityFailurePolicy.StopOnFirstNonAccepted, CancellationToken cancellationToken = default)
            => Task.FromResult(report ?? new ActivityRunReport(activity.Id, activity.Name, failurePolicy, ActivityRunStatus.Completed, []));
    }

    private sealed class FakeMediaLibraryService(MediaCatalogEntry? entry) : IMediaLibraryService
    {
        public Task<MediaCatalogEntry?> ResolveAsync(MediaReference reference, CancellationToken cancellationToken = default)
            => Task.FromResult(entry is not null && MediaReference.From(entry.Item) == reference ? entry : null);
        public Task<IReadOnlyList<MediaFavorite>> GetFavoritesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaHistoryEntry>> GetHistoryAsync(int maxItems = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaHistoryEntry>> GetContinueWatchingAsync(int maxItems = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaDetails> GetDetailsAsync(MediaSource source, MediaItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ToggleFavoriteAsync(MediaItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTargetDiscovery(IReadOnlyList<PlaybackTargetCandidate> candidates) : IPlaybackTargetDiscovery
    {
        public Task<IReadOnlyList<PlaybackTargetCandidate>> DiscoverAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(candidates);
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public PlaybackSession? CurrentSession { get; private set; }
        public int StartCount { get; private set; }
        public string? LastTargetId { get; private set; }
        public event EventHandler<PlaybackSessionChangedEventArgs>? SessionChanged;

        public Task<PlaybackSession> StartAsync(MediaSource source, MediaItem item, IPlaybackTarget target, CancellationToken cancellationToken = default)
        {
            StartCount++;
            LastTargetId = target.Id;
            var now = DateTimeOffset.UtcNow;
            CurrentSession = new PlaybackSession(
                Guid.NewGuid(), source.Id, item.ExternalId, item.Kind, item.Title, target.Id,
                PlaybackSessionState.Playing, TimeSpan.Zero, item.Duration, now, now);
            SessionChanged?.Invoke(this, new PlaybackSessionChangedEventArgs(CurrentSession));
            return Task.FromResult(CurrentSession);
        }

        public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
