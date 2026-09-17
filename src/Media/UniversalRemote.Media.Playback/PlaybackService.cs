using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Playback;

public sealed class PlaybackService : IPlaybackService, IDisposable
{
    private static readonly TimeSpan MinimumResumePosition = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CompletedThreshold = TimeSpan.FromSeconds(30);

    private readonly ILocalPlaybackEngine engine;
    private readonly IPlaybackCheckpointStore checkpoints;
    private readonly IReadOnlyDictionary<string, IStreamResolver> resolvers;
    private readonly IMediaHistoryRepository? history;
    private readonly IMediaAccessPolicy? access;
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private readonly object stateGate = new();
    private PlaybackSession? currentSession;
    private bool disposed;

    public PlaybackSession? CurrentSession
    {
        get { lock (stateGate) return currentSession; }
    }

    public event EventHandler<PlaybackSessionChangedEventArgs>? SessionChanged;

    public PlaybackService(
        ILocalPlaybackEngine engine,
        IPlaybackCheckpointStore checkpoints,
        IEnumerable<IStreamResolver> streamResolvers,
        IMediaHistoryRepository? history = null,
        IMediaAccessPolicy? access = null)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        this.history = history;
        this.access = access;
        ArgumentNullException.ThrowIfNull(streamResolvers);

        var resolverArray = streamResolvers.ToArray();
        var duplicate = resolverArray
            .GroupBy(x => x.ProviderId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"More than one stream resolver is registered for provider '{duplicate.Key}'.");

        resolvers = resolverArray.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
        engine.Changed += OnEngineChanged;
    }

    public async Task<PlaybackSession> StartAsync(
        MediaSource source,
        MediaItem item,
        IPlaybackTarget target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(target);
        if (item.SourceId != source.Id)
            throw new ArgumentException("Media item does not belong to the selected source.", nameof(item));
        if (access is not null)
        {
            var accessDecision = await access.EvaluateAsync(item, cancellationToken).ConfigureAwait(false);
            if (!accessDecision.IsAllowed)
                throw new UnauthorizedAccessException(accessDecision.UserMessage ?? "Ce contenu est bloqué pour le profil actif.");
        }
        if (target.Kind != PlaybackTargetKind.LocalDevice)
            throw new NotSupportedException("No remote playback transport is registered for the selected target.");
        if (!target.Capabilities.Contains(PlaybackCapability.Start))
            throw new NotSupportedException("The selected playback target cannot start media.");
        if (!resolvers.TryGetValue(source.ProviderId, out var resolver))
            throw new InvalidOperationException($"No stream resolver is registered for provider '{source.ProviderId}'.");

        await commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (CurrentSession is { } previous)
            {
                await PersistCheckpointAsync(previous, cancellationToken).ConfigureAwait(false);
                await engine.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow;
            var session = new PlaybackSession(
                Guid.NewGuid(), source.Id, item.ExternalId, item.Kind, item.Title, target.Id,
                PlaybackSessionState.Resolving, TimeSpan.Zero, item.Duration, now, now,
                category: item.Category, minimumAge: item.MinimumAge);
            Publish(session);

            try
            {
                var stream = await resolver.ResolveAsync(source, item, cancellationToken).ConfigureAwait(false);
                var resume = stream.IsLive
                    ? TimeSpan.Zero
                    : await checkpoints.LoadAsync(source.Id, item.ExternalId, cancellationToken).ConfigureAwait(false) ?? TimeSpan.Zero;

                Publish(Copy(CurrentRequired(), state: PlaybackSessionState.Opening, position: resume));
                await engine.LoadAsync(stream, item.Title, resume, cancellationToken).ConfigureAwait(false);
                await engine.PlayAsync(cancellationToken).ConfigureAwait(false);

                var snapshot = engine.Snapshot;
                var updated = Copy(
                    CurrentRequired(),
                    state: Map(snapshot.State),
                    position: snapshot.Position,
                    duration: snapshot.Duration ?? item.Duration);
                Publish(updated);
                await PersistHistoryAsync(updated, cancellationToken).ConfigureAwait(false);
                return updated;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                var failed = Copy(
                    CurrentRequired(),
                    state: PlaybackSessionState.Failed,
                    failureCode: "playback.start_failed",
                    userMessage: "Impossible de démarrer la lecture. Vérifiez la source et votre connexion.");
                Publish(failed);
                return failed;
            }
        }
        finally
        {
            commandGate.Release();
        }
    }

    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteWithSessionAsync(
            async session =>
            {
                await engine.PlayAsync(cancellationToken).ConfigureAwait(false);
                Publish(Copy(session, state: PlaybackSessionState.Playing));
            }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteWithSessionAsync(
            async session =>
            {
                await engine.PauseAsync(cancellationToken).ConfigureAwait(false);
                var snapshot = engine.Snapshot;
                var paused = Copy(session, state: PlaybackSessionState.Paused, position: snapshot.Position, duration: snapshot.Duration ?? session.Duration);
                Publish(paused);
                await PersistCheckpointAsync(paused, cancellationToken).ConfigureAwait(false);
                await PersistHistoryAsync(paused, cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (position < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));
        await ExecuteWithSessionAsync(
            async session =>
            {
                if (session.MediaKind == MediaItemKind.LiveChannel)
                    throw new NotSupportedException("Seeking a live channel is not supported in MEDIA 3.");
                await engine.SeekAsync(position, cancellationToken).ConfigureAwait(false);
                var snapshot = engine.Snapshot;
                var updated = Copy(session, position: snapshot.Position, duration: snapshot.Duration ?? session.Duration);
                Publish(updated);
                await PersistHistoryAsync(updated, cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteWithSessionAsync(
            async session =>
            {
                var beforeStop = engine.Snapshot;
                var checkpointSession = Copy(session, position: beforeStop.Position, duration: beforeStop.Duration ?? session.Duration);
                await PersistCheckpointAsync(checkpointSession, cancellationToken).ConfigureAwait(false);
                await engine.StopAsync(cancellationToken).ConfigureAwait(false);
                var stopped = Copy(checkpointSession, state: PlaybackSessionState.Stopped);
                Publish(stopped);
                await PersistHistoryAsync(stopped, cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteWithSessionAsync(Func<PlaybackSession, Task> action, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(action);
        await commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = CurrentSession ?? throw new InvalidOperationException("No playback session is active.");
            await action(session).ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task PersistCheckpointAsync(PlaybackSession session, CancellationToken cancellationToken)
    {
        if (session.MediaKind == MediaItemKind.LiveChannel) return;

        if (session.State == PlaybackSessionState.Completed ||
            session.Position < MinimumResumePosition ||
            session.Duration is { } duration && duration > TimeSpan.Zero && duration - session.Position <= CompletedThreshold)
        {
            await checkpoints.DeleteAsync(session.SourceId, session.MediaExternalId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await checkpoints.SaveAsync(session.SourceId, session.MediaExternalId, session.Position, cancellationToken).ConfigureAwait(false);
    }

    private void OnEngineChanged(object? sender, LocalPlaybackEngineChangedEventArgs e)
    {
        var session = CurrentSession;
        if (session is null) return;

        var snapshot = e.Snapshot;
        var mapped = Copy(
            session,
            state: Map(snapshot.State),
            position: snapshot.Position,
            duration: snapshot.Duration ?? session.Duration,
            failureCode: snapshot.State == LocalPlaybackEngineState.Failed ? snapshot.FailureCode ?? "playback.engine_failed" : null,
            userMessage: snapshot.State == LocalPlaybackEngineState.Failed ? "La lecture a rencontré une erreur." : null);
        Publish(mapped);
        _ = PersistHistorySafeAsync(mapped);

        if (snapshot.State == LocalPlaybackEngineState.Completed)
            _ = DeleteCheckpointSafeAsync(mapped);
    }

    private async Task PersistHistoryAsync(PlaybackSession session, CancellationToken cancellationToken)
    {
        if (history is null) return;

        var completed = session.State == PlaybackSessionState.Completed ||
            session.Duration is { } duration && duration > TimeSpan.Zero && duration - session.Position <= CompletedThreshold;
        var entry = new MediaHistoryEntry(
            new MediaReference(session.SourceId, session.MediaExternalId, session.MediaKind),
            session.Title,
            session.Position,
            session.Duration,
            DateTimeOffset.UtcNow,
            completed,
            session.Category,
            session.MinimumAge);
        await history.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistHistorySafeAsync(PlaybackSession session)
    {
        try { await PersistHistoryAsync(session, CancellationToken.None).ConfigureAwait(false); }
        catch { /* History persistence must never interrupt playback callbacks. */ }
    }

    private async Task DeleteCheckpointSafeAsync(PlaybackSession session)
    {
        try { await checkpoints.DeleteAsync(session.SourceId, session.MediaExternalId).ConfigureAwait(false); }
        catch { /* Checkpoint persistence must never crash playback state propagation. */ }
    }

    private PlaybackSession CurrentRequired()
        => CurrentSession ?? throw new InvalidOperationException("Playback session was unexpectedly cleared.");

    private void Publish(PlaybackSession session)
    {
        lock (stateGate) currentSession = session;
        SessionChanged?.Invoke(this, new PlaybackSessionChangedEventArgs(session));
    }

    private static PlaybackSession Copy(
        PlaybackSession source,
        PlaybackSessionState? state = null,
        TimeSpan? position = null,
        TimeSpan? duration = null,
        string? failureCode = null,
        string? userMessage = null)
        => new(
            source.SessionId,
            source.SourceId,
            source.MediaExternalId,
            source.MediaKind,
            source.Title,
            source.TargetId,
            state ?? source.State,
            position ?? source.Position,
            duration ?? source.Duration,
            source.StartedAt,
            DateTimeOffset.UtcNow,
            failureCode,
            userMessage,
            source.Category,
            source.MinimumAge);

    private static PlaybackSessionState Map(LocalPlaybackEngineState state) => state switch
    {
        LocalPlaybackEngineState.Opening => PlaybackSessionState.Opening,
        LocalPlaybackEngineState.Buffering => PlaybackSessionState.Buffering,
        LocalPlaybackEngineState.Playing => PlaybackSessionState.Playing,
        LocalPlaybackEngineState.Paused => PlaybackSessionState.Paused,
        LocalPlaybackEngineState.Stopped => PlaybackSessionState.Stopped,
        LocalPlaybackEngineState.Completed => PlaybackSessionState.Completed,
        LocalPlaybackEngineState.Failed => PlaybackSessionState.Failed,
        _ => PlaybackSessionState.Opening
    };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        engine.Changed -= OnEngineChanged;
        commandGate.Dispose();
    }
}
