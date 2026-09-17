using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Core;
using UniversalRemote.Media.Playback;
using Xunit;

namespace UniversalRemote.Media.Tests;

public sealed class MediaExperienceTests
{
    [Fact]
    public async Task Browse_aggregates_enabled_sources_and_survives_one_provider_failure()
    {
        var repository = new InMemoryMediaSourceRepository();
        var sourceA = new MediaSource(Guid.NewGuid(), "Maison", "ok");
        var sourceB = new MediaSource(Guid.NewGuid(), "Secours", "broken");
        await repository.SaveAsync(sourceA);
        await repository.SaveAsync(sourceB);
        var catalog = new MediaCatalog(new MediaProviderResolver([new CatalogProvider("ok"), new ThrowingProvider("broken")]));
        var browse = new MediaBrowseService(repository, catalog);

        var result = await browse.BrowseAsync([MediaItemKind.Movie]);

        Assert.Single(result.Items);
        Assert.Equal("Film Démo", result.Items[0].Item.Title);
        Assert.Equal(1, result.LoadedSourceCount);
        Assert.Equal(1, result.FailedSourceCount);
    }

    [Fact]
    public async Task Search_matches_title_category_and_source_without_provider_specific_logic()
    {
        var repository = new InMemoryMediaSourceRepository();
        var source = new MediaSource(Guid.NewGuid(), "Cinéma Maison", "ok");
        await repository.SaveAsync(source);
        var catalog = new MediaCatalog(new MediaProviderResolver([new CatalogProvider("ok")]));
        var search = new MediaSearchService(new MediaBrowseService(repository, catalog));

        var byTitle = await search.SearchAsync("demo");
        var byCategory = await search.SearchAsync("action");
        var bySource = await search.SearchAsync("cinema");

        Assert.Contains(byTitle.Items, x => x.Item.Title == "Film Démo");
        Assert.Contains(byCategory.Items, x => x.Item.Title == "Film Démo");
        Assert.NotEmpty(bySource.Items);
    }

    [Fact]
    public async Task Favorite_toggle_and_continue_watching_are_provider_independent()
    {
        var sourceRepository = new InMemoryMediaSourceRepository();
        var favorites = new InMemoryMediaFavoriteRepository();
        var history = new InMemoryMediaHistoryRepository();
        var source = new MediaSource(Guid.NewGuid(), "Maison", "ok");
        await sourceRepository.SaveAsync(source);
        var catalog = new MediaCatalog(new MediaProviderResolver([new CatalogProvider("ok")]));
        var library = new MediaLibraryService(favorites, history, sourceRepository, catalog);
        var movie = (await catalog.GetAsync(source, new MediaCatalogRequest([MediaItemKind.Movie]))).Items.Single();

        Assert.True(await library.ToggleFavoriteAsync(movie));
        Assert.Single(await library.GetFavoritesAsync());
        Assert.False(await library.ToggleFavoriteAsync(movie));
        Assert.Empty(await library.GetFavoritesAsync());

        await history.SaveAsync(new MediaHistoryEntry(MediaReference.From(movie), movie.Title, TimeSpan.FromMinutes(12), TimeSpan.FromHours(2), DateTimeOffset.UtcNow, false));
        var live = new MediaItem(source.Id, "live:1", MediaItemKind.LiveChannel, "Live");
        await history.SaveAsync(new MediaHistoryEntry(MediaReference.From(live), live.Title, TimeSpan.Zero, null, DateTimeOffset.UtcNow, false));

        var continueItems = await library.GetContinueWatchingAsync();
        var item = Assert.Single(continueItems);
        Assert.Equal(movie.ExternalId, item.Reference.ExternalId);
        Assert.True(item.Progress > 0);
    }

    [Fact]
    public async Task Resolve_refreshes_current_catalog_instead_of_persisting_a_stream_or_item_object()
    {
        var sourceRepository = new InMemoryMediaSourceRepository();
        var source = new MediaSource(Guid.NewGuid(), "Maison", "ok");
        await sourceRepository.SaveAsync(source);
        var catalog = new MediaCatalog(new MediaProviderResolver([new CatalogProvider("ok")]));
        var library = new MediaLibraryService(new InMemoryMediaFavoriteRepository(), new InMemoryMediaHistoryRepository(), sourceRepository, catalog);
        var reference = new MediaReference(source.Id, "movie:1", MediaItemKind.Movie);

        var resolved = await library.ResolveAsync(reference);

        Assert.NotNull(resolved);
        Assert.Equal("Film Démo", resolved!.Item.Title);
        Assert.DoesNotContain("Stream", typeof(MediaFavorite).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("Url", typeof(MediaHistoryEntry).GetProperties().Select(x => x.Name));
    }

    [Fact]
    public async Task Playback_updates_history_when_repository_is_available()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", "play", "credential:play");
        var movie = new MediaItem(source.Id, "movie:42", MediaItemKind.Movie, "Film", duration: TimeSpan.FromHours(2));
        var history = new InMemoryMediaHistoryRepository();
        var engine = new PlaybackEngine();
        var playback = new PlaybackService(engine, new InMemoryPlaybackCheckpointStore(), [new Resolver("play")], history);

        await playback.StartAsync(source, movie, PlaybackTargets.LocalDevice);
        engine.Advance(TimeSpan.FromMinutes(15));
        await playback.PauseAsync();

        var entry = await history.FindAsync(MediaReference.From(movie));
        Assert.NotNull(entry);
        Assert.Equal(TimeSpan.FromMinutes(15), entry!.Position);
        Assert.False(entry.Completed);
    }

    [Fact]
    public void Media_core_registers_experience_services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediaProvider>(new CatalogProvider("ok"));
        services.AddUniversalRemoteMediaCore();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMediaBrowseService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMediaSearchService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMediaLibraryService>());
        Assert.NotNull(provider.GetRequiredService<IMediaFavoriteRepository>());
        Assert.NotNull(provider.GetRequiredService<IMediaHistoryRepository>());
    }

    private sealed class CatalogProvider(string id) : IMediaProvider
    {
        public string Id { get; } = id;

        public Task<MediaCatalogSnapshot> GetCatalogAsync(MediaSource source, MediaCatalogRequest request, CancellationToken cancellationToken = default)
        {
            var items = new List<MediaItem>();
            if (request.Kinds.Contains(MediaItemKind.Movie))
                items.Add(new MediaItem(source.Id, "movie:1", MediaItemKind.Movie, "Film Démo", "Action", TimeSpan.FromHours(2)));
            if (request.Kinds.Contains(MediaItemKind.LiveChannel))
                items.Add(new MediaItem(source.Id, "live:1", MediaItemKind.LiveChannel, "Info Démo", "Actualités"));
            return Task.FromResult(new MediaCatalogSnapshot(source.Id, items, DateTimeOffset.UtcNow));
        }
    }

    private sealed class ThrowingProvider(string id) : IMediaProvider
    {
        public string Id { get; } = id;
        public Task<MediaCatalogSnapshot> GetCatalogAsync(MediaSource source, MediaCatalogRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated provider failure");
    }

    private sealed class Resolver(string providerId) : IStreamResolver
    {
        public string ProviderId { get; } = providerId;
        public Task<ResolvedMediaStream> ResolveAsync(MediaSource source, MediaItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedMediaStream(new Uri("https://example.test/video.mp4"), false, "video/mp4"));
    }

    private sealed class PlaybackEngine : ILocalPlaybackEngine
    {
        public LocalPlaybackEngineSnapshot Snapshot { get; private set; } = new(LocalPlaybackEngineState.None, TimeSpan.Zero, TimeSpan.FromHours(2));
        public event EventHandler<LocalPlaybackEngineChangedEventArgs>? Changed;

        public Task LoadAsync(ResolvedMediaStream stream, string title, TimeSpan resumePosition, CancellationToken cancellationToken = default)
        {
            Set(new LocalPlaybackEngineSnapshot(LocalPlaybackEngineState.Stopped, resumePosition, TimeSpan.FromHours(2)));
            return Task.CompletedTask;
        }
        public Task PlayAsync(CancellationToken cancellationToken = default) { Set(Snapshot with { State = LocalPlaybackEngineState.Playing }); return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken cancellationToken = default) { Set(Snapshot with { State = LocalPlaybackEngineState.Paused }); return Task.CompletedTask; }
        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) { Set(Snapshot with { Position = position }); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { Set(Snapshot with { State = LocalPlaybackEngineState.Stopped }); return Task.CompletedTask; }
        public void Advance(TimeSpan position) => Set(Snapshot with { Position = position });
        private void Set(LocalPlaybackEngineSnapshot snapshot) { Snapshot = snapshot; Changed?.Invoke(this, new LocalPlaybackEngineChangedEventArgs(snapshot)); }
    }
}
