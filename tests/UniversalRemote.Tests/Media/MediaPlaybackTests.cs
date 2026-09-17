using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Playback;
using Xunit;

namespace UniversalRemote.Media.Tests;

public sealed class MediaPlaybackTests
{
    [Fact]
    public async Task Local_start_resolves_stream_and_exposes_only_sanitized_session_state()
    {
        var source = Source();
        var item = Movie(source.Id);
        var resolver = new FakeResolver(source.ProviderId, new Uri("https://demo:secret@example.test/movie.m3u8?token=very-secret"));
        var engine = new FakeEngine();
        var service = new PlaybackService(engine, new InMemoryPlaybackCheckpointStore(), [resolver]);

        var session = await service.StartAsync(source, item, PlaybackTargets.LocalDevice);

        Assert.Equal(PlaybackSessionState.Playing, session.State);
        Assert.Equal(item.ExternalId, session.MediaExternalId);
        Assert.Equal(item.Title, session.Title);
        Assert.Equal("local-device", session.TargetId);
        Assert.Equal("https://demo:secret@example.test/movie.m3u8?token=very-secret", engine.LoadedStream!.StreamUri.AbsoluteUri);
        Assert.DoesNotContain("very-secret", session.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", session.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pause_persists_position_and_next_start_resumes_from_checkpoint()
    {
        var source = Source();
        var item = Movie(source.Id);
        var checkpoints = new InMemoryPlaybackCheckpointStore();
        var resolver = new FakeResolver(source.ProviderId, new Uri("https://example.test/movie.mp4"));
        var firstEngine = new FakeEngine();
        var first = new PlaybackService(firstEngine, checkpoints, [resolver]);
        await first.StartAsync(source, item, PlaybackTargets.LocalDevice);
        firstEngine.AdvanceTo(TimeSpan.FromMinutes(12));

        await first.PauseAsync();

        var secondEngine = new FakeEngine();
        var second = new PlaybackService(secondEngine, checkpoints, [resolver]);
        await second.StartAsync(source, item, PlaybackTargets.LocalDevice);
        Assert.Equal(TimeSpan.FromMinutes(12), secondEngine.LastResumePosition);
    }

    [Fact]
    public async Task Seek_updates_non_live_session_but_is_rejected_for_live_channel()
    {
        var source = Source();
        var resolver = new FakeResolver(source.ProviderId, new Uri("https://example.test/media.m3u8"));
        var engine = new FakeEngine();
        var service = new PlaybackService(engine, new InMemoryPlaybackCheckpointStore(), [resolver]);
        await service.StartAsync(source, Movie(source.Id), PlaybackTargets.LocalDevice);

        await service.SeekAsync(TimeSpan.FromMinutes(20));
        Assert.Equal(TimeSpan.FromMinutes(20), service.CurrentSession!.Position);

        var live = new MediaItem(source.Id, "fake:live:1", MediaItemKind.LiveChannel, "Live");
        await service.StartAsync(source, live, PlaybackTargets.LocalDevice);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.SeekAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Resolver_failure_returns_generic_failed_session_without_leaking_exception_text()
    {
        var source = Source();
        var item = Movie(source.Id);
        var resolver = new ThrowingResolver(source.ProviderId, "https://user:password@example.test/private?token=secret");
        var service = new PlaybackService(new FakeEngine(), new InMemoryPlaybackCheckpointStore(), [resolver]);

        var session = await service.StartAsync(source, item, PlaybackTargets.LocalDevice);

        Assert.Equal(PlaybackSessionState.Failed, session.State);
        Assert.Equal("playback.start_failed", session.FailureCode);
        Assert.DoesNotContain("password", session.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.test", session.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", session.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dependency_injection_registers_playback_service_and_checkpoint_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocalPlaybackEngine, FakeEngine>();
        services.AddSingleton<IStreamResolver>(new FakeResolver("fake", new Uri("https://example.test/test.mp4")));
        services.AddUniversalRemoteMediaPlayback();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IPlaybackService>());
        Assert.IsType<InMemoryPlaybackCheckpointStore>(provider.GetRequiredService<IPlaybackCheckpointStore>());
    }

    private static MediaSource Source()
        => new(Guid.NewGuid(), "Test", "fake", "credential:fake");

    private static MediaItem Movie(Guid sourceId)
        => new(sourceId, "fake:movie:42", MediaItemKind.Movie, "Demo Movie", duration: TimeSpan.FromHours(2));

    private sealed class FakeResolver(string providerId, Uri uri) : IStreamResolver
    {
        public string ProviderId { get; } = providerId;
        public Task<ResolvedMediaStream> ResolveAsync(MediaSource source, MediaItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedMediaStream(uri, item.Kind == MediaItemKind.LiveChannel, "video/mp4"));
    }

    private sealed class ThrowingResolver(string providerId, string sensitiveMessage) : IStreamResolver
    {
        public string ProviderId { get; } = providerId;
        public Task<ResolvedMediaStream> ResolveAsync(MediaSource source, MediaItem item, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(sensitiveMessage);
    }

    private sealed class FakeEngine : ILocalPlaybackEngine
    {
        public LocalPlaybackEngineSnapshot Snapshot { get; private set; } = new(LocalPlaybackEngineState.None, TimeSpan.Zero, null);
        public ResolvedMediaStream? LoadedStream { get; private set; }
        public TimeSpan LastResumePosition { get; private set; }
        public event EventHandler<LocalPlaybackEngineChangedEventArgs>? Changed;

        public Task LoadAsync(ResolvedMediaStream stream, string title, TimeSpan resumePosition, CancellationToken cancellationToken = default)
        {
            LoadedStream = stream;
            LastResumePosition = resumePosition;
            Set(new LocalPlaybackEngineSnapshot(LocalPlaybackEngineState.Stopped, resumePosition, TimeSpan.FromHours(2)));
            return Task.CompletedTask;
        }

        public Task PlayAsync(CancellationToken cancellationToken = default)
        {
            Set(Snapshot with { State = LocalPlaybackEngineState.Playing });
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            Set(Snapshot with { State = LocalPlaybackEngineState.Paused });
            return Task.CompletedTask;
        }

        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            Set(Snapshot with { Position = position });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Set(Snapshot with { State = LocalPlaybackEngineState.Stopped });
            return Task.CompletedTask;
        }

        public void AdvanceTo(TimeSpan position) => Set(Snapshot with { Position = position });

        private void Set(LocalPlaybackEngineSnapshot value)
        {
            Snapshot = value;
            Changed?.Invoke(this, new LocalPlaybackEngineChangedEventArgs(value));
        }
    }
}
