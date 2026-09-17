using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Provider.M3U;
using Xunit;

namespace UniversalRemote.Media.Tests;

public sealed class MediaM3uTests
{
    private const string SamplePlaylist = """
        #EXTM3U url-tvg="https://guide.example.test/epg.xml"
        #EXTINF:-1 tvg-id="tf1.fr" tvg-name="TF1" tvg-logo="https://img.example.test/tf1.png" group-title="France",TF1 HD
        https://stream.example.test/live/tf1/index.m3u8?token=secret-value
        #EXTINF:-1 tvg-id="france2.fr" group-title="France",France 2
        /live/france2/index.m3u8
        #EXTINF:-1 group-title="News",Demo News
        https://stream.example.test/live/news.ts
        """;

    [Fact]
    public void Parser_reads_extended_metadata_and_resolves_relative_uris()
    {
        var parser = new M3uPlaylistParser();

        var result = parser.Parse(SamplePlaylist, new Uri("https://playlist.example.test/user/list.m3u"));

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal("https://guide.example.test/epg.xml", result.EpgUri!.AbsoluteUri);

        var first = result.Entries[0];
        Assert.Equal("TF1 HD", first.Title);
        Assert.Equal("tf1.fr", first.TvgId);
        Assert.Equal("TF1", first.TvgName);
        Assert.Equal("France", first.GroupTitle);
        Assert.Equal("https://img.example.test/tf1.png", first.LogoUri!.AbsoluteUri);

        var second = result.Entries[1];
        Assert.Equal("https://playlist.example.test/live/france2/index.m3u8", second.StreamUri.AbsoluteUri);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parser_is_tolerant_but_reports_sanitized_invalid_entries()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 group-title="Demo",Missing stream
            not a uri
            #EXTINF:-1 group-title="Demo",Good channel
            https://example.test/live/good.m3u8
            """;

        var result = new M3uPlaylistParser().Parse(playlist);

        Assert.Single(result.Entries);
        Assert.Equal("Good channel", result.Entries[0].Title);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("invalid-stream-uri", warning.Code);
        Assert.DoesNotContain("not a uri", warning.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parser_rejects_hls_playback_manifest_as_catalogue()
    {
        const string hls = """
            #EXTM3U
            #EXT-X-TARGETDURATION:6
            #EXT-X-MEDIA-SEQUENCE:1
            #EXTINF:6.0,
            segment001.ts
            """;

        var error = Assert.Throws<M3uPlaylistFormatException>(() => new M3uPlaylistParser().Parse(hls, new Uri("https://example.test/live/index.m3u8")));

        Assert.Contains("HLS", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_normalizes_live_channels_without_leaking_stream_uri()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", M3uMediaProvider.ProviderId, "secure:m3u:1");
        var credentials = new FixtureCredentialStore("https://playlist.example.test/list.m3u?username=demo&password=top-secret");
        var handler = new FixtureHttpHandler(SamplePlaylist);
        var provider = CreateProvider(credentials, handler);

        var result = await provider.GetCatalogAsync(source, new MediaCatalogRequest([MediaItemKind.LiveChannel]));

        Assert.Equal(3, result.Items.Count);
        var tf1 = result.Items[0];
        Assert.Equal("TF1 HD", tf1.Title);
        Assert.Equal("France", tf1.Category);
        Assert.Equal("m3u:tvg:tf1.fr", tf1.ExternalId);
        Assert.Equal("tf1.fr", tf1.GuideId);
        Assert.Equal("https://img.example.test/tf1.png", tf1.ArtworkUri!.AbsoluteUri);
        Assert.DoesNotContain("StreamUri", typeof(MediaItem).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("top-secret", tf1.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_honors_catalogue_kind_filter()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", M3uMediaProvider.ProviderId, "secure:m3u:2");
        var provider = CreateProvider(
            new FixtureCredentialStore("https://playlist.example.test/list.m3u"),
            new FixtureHttpHandler(SamplePlaylist));

        var result = await provider.GetCatalogAsync(source, new MediaCatalogRequest([MediaItemKind.Movie]));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Provider_requires_secure_credential_reference()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", M3uMediaProvider.ProviderId);
        var provider = CreateProvider(new FixtureCredentialStore(null), new FixtureHttpHandler(SamplePlaylist));

        var error = await Assert.ThrowsAsync<M3uSourceConfigurationException>(() =>
            provider.GetCatalogAsync(source, new MediaCatalogRequest()));

        Assert.DoesNotContain("http", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_failures_do_not_echo_sensitive_playlist_url()
    {
        const string secretUrl = "https://playlist.example.test/list.m3u?username=demo&password=very-secret";
        var source = new MediaSource(Guid.NewGuid(), "Maison", M3uMediaProvider.ProviderId, "secure:m3u:3");
        var provider = CreateProvider(
            new FixtureCredentialStore(secretUrl),
            new FixtureHttpHandler("denied", HttpStatusCode.Unauthorized));

        var error = await Assert.ThrowsAsync<M3uPlaylistLoadException>(() =>
            provider.GetCatalogAsync(source, new MediaCatalogRequest()));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("very-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("playlist.example.test", error.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Stream_resolver_returns_ephemeral_channel_uri_without_putting_it_in_the_catalogue_item()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", M3uMediaProvider.ProviderId, "secure:m3u:play");
        var credentials = new FixtureCredentialStore("https://playlist.example.test/list.m3u?password=playlist-secret");
        var client = new M3uPlaylistClient(new HttpClient(new FixtureHttpHandler(SamplePlaylist)));
        var parser = new M3uPlaylistParser();
        var provider = new M3uMediaProvider(credentials, client, parser);
        var catalogue = await provider.GetCatalogAsync(source, new MediaCatalogRequest([MediaItemKind.LiveChannel]));
        var channel = catalogue.Items.First(x => x.ExternalId == "m3u:tvg:tf1.fr");
        var resolver = new M3uStreamResolver(provider, client, parser);

        var stream = await resolver.ResolveAsync(source, channel);

        Assert.True(stream.IsLive);
        Assert.Equal("https://stream.example.test/live/tf1/index.m3u8?token=secret-value", stream.StreamUri.AbsoluteUri);
        Assert.DoesNotContain("secret-value", channel.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", stream.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dependency_injection_registers_m3u_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediaCredentialStore>(new FixtureCredentialStore("https://playlist.example.test/list.m3u"));
        services.AddUniversalRemoteM3uProvider();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        var provider = Assert.Single(scope.ServiceProvider.GetServices<IMediaProvider>());
        Assert.Equal(M3uMediaProvider.ProviderId, provider.Id);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<M3uPlaylistParser>());
        Assert.Equal(M3uMediaProvider.ProviderId, Assert.Single(scope.ServiceProvider.GetServices<IStreamResolver>()).ProviderId);
    }

    private static M3uMediaProvider CreateProvider(IMediaCredentialStore credentialStore, HttpMessageHandler handler)
        => new(
            credentialStore,
            new M3uPlaylistClient(new HttpClient(handler)),
            new M3uPlaylistParser());

    private sealed class FixtureCredentialStore(string? secret) : IMediaCredentialStore
    {
        public Task<MediaSecret?> GetAsync(string credentialReference, CancellationToken cancellationToken = default)
            => Task.FromResult(secret is null ? null : new MediaSecret(secret));

        public Task SetAsync(string credentialReference, MediaSecret value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> DeleteAsync(string credentialReference, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FixtureHttpHandler(string content, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/x-mpegURL")
            };
            return Task.FromResult(response);
        }
    }
}
