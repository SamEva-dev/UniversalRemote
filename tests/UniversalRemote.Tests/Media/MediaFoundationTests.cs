using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Core;
using Xunit;

namespace UniversalRemote.Media.Tests;

public sealed class MediaFoundationTests
{
    [Fact]
    public void Media_source_contains_reference_not_secret()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", "m3u", "secure:media:1");

        Assert.Equal("secure:media:1", source.CredentialReference);
        Assert.DoesNotContain("Password", typeof(MediaSource).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("Username", typeof(MediaSource).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("Url", typeof(MediaSource).GetProperties().Select(x => x.Name));
    }

    [Fact]
    public void Media_item_never_contains_stream_uri()
    {
        var item = new MediaItem(Guid.NewGuid(), "channel:1", MediaItemKind.LiveChannel, "Demo TV", "News");

        Assert.Equal(MediaItemKind.LiveChannel, item.Kind);
        Assert.DoesNotContain("Stream", typeof(MediaItem).GetProperties().Select(x => x.Name));
    }

    [Fact]
    public void Media_secret_is_redacted_when_formatted()
    {
        var secret = new MediaSecret("user=demo&password=top-secret");

        Assert.Equal("[REDACTED]", secret.ToString());
        Assert.DoesNotContain("top-secret", secret.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Playback_target_can_link_to_device_without_becoming_media_source()
    {
        var deviceId = Guid.NewGuid();
        var target = new PlaybackTarget(
            "android-tv:salon",
            "Android TV Salon",
            PlaybackTargetKind.AndroidTv,
            [PlaybackCapability.Start, PlaybackCapability.Pause],
            deviceId);

        Assert.Equal(deviceId, target.DeviceId);
        Assert.Contains(PlaybackCapability.Start, target.Capabilities);
        Assert.False(target is object && typeof(MediaSource).IsAssignableFrom(target.GetType()));
    }

    [Fact]
    public async Task Catalog_routes_source_to_matching_provider()
    {
        var source = new MediaSource(Guid.NewGuid(), "Demo", "fixture");
        var provider = new FixtureProvider();
        var catalog = new MediaCatalog(new MediaProviderResolver([provider]));

        var result = await catalog.GetAsync(source);

        Assert.Equal(source.Id, result.SourceId);
        var item = Assert.Single(result.Items);
        Assert.Equal("Fixture Channel", item.Title);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void Duplicate_provider_ids_are_rejected_case_insensitively()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new MediaProviderResolver([new FixtureProvider("m3u"), new FixtureProvider("M3U")]));
    }

    [Fact]
    public void Dependency_injection_registers_media_core_without_concrete_provider()
    {
        var services = new ServiceCollection();
        services.AddUniversalRemoteMediaCore();
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMediaCatalog>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMediaSourceRepository>());
        Assert.Throws<MediaProviderUnavailableException>(() =>
            scope.ServiceProvider.GetRequiredService<MediaProviderResolver>().Resolve("missing"));
    }

    [Fact]
    public async Task In_memory_source_repository_stores_metadata_only()
    {
        var repository = new InMemoryMediaSourceRepository();
        var source = new MediaSource(Guid.NewGuid(), "My source", "fixture", "secure:42");

        await repository.SaveAsync(source);
        var loaded = await repository.FindAsync(source.Id);

        Assert.Equal(source, loaded);
        Assert.Equal("secure:42", loaded!.CredentialReference);
    }

    private sealed class FixtureProvider : IMediaProvider
    {
        public string Id { get; }
        public int CallCount { get; private set; }

        public FixtureProvider(string id = "fixture") => Id = id;

        public Task<MediaCatalogSnapshot> GetCatalogAsync(
            MediaSource source,
            MediaCatalogRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var item = new MediaItem(source.Id, "fixture:1", MediaItemKind.LiveChannel, "Fixture Channel");
            return Task.FromResult(new MediaCatalogSnapshot(source.Id, [item], DateTimeOffset.UtcNow));
        }
    }
}
