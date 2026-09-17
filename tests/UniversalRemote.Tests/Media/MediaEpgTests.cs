using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Epg;
using Xunit;

namespace UniversalRemote.Media.Tests;

public sealed class MediaEpgTests
{
    private const string XmlTv = """
        <?xml version="1.0" encoding="UTF-8"?>
        <tv>
          <channel id="tf1.fr"><display-name>TF1</display-name><icon src="https://img.example.test/tf1.png" /></channel>
          <channel id="france2.fr"><display-name>France 2</display-name></channel>
          <programme start="20260917080000 +0200" stop="20260917090000 +0200" channel="tf1.fr">
            <title>Bonjour la France</title><desc>Magazine du matin</desc><category>Magazine</category>
          </programme>
          <programme start="20260917090000 +0200" stop="20260917100000 +0200" channel="tf1.fr">
            <title>Le Journal</title><category>Information</category>
          </programme>
          <programme start="20260917083000 +0200" stop="20260917093000 +0200" channel="france2.fr">
            <title>Télématin</title>
          </programme>
        </tv>
        """;

    [Fact]
    public void Xmltv_parser_reads_channels_and_programs_in_requested_window()
    {
        var parser = new XmlTvParser();
        var start = new DateTimeOffset(2026, 9, 17, 7, 30, 0, TimeSpan.FromHours(2));
        var end = start.AddHours(2);

        var result = parser.Parse(XmlTv, start, end);

        Assert.Equal(2, result.Channels.Count);
        Assert.Equal(3, result.Programs.Count);
        Assert.Equal("tf1.fr", result.Channels[0].GuideId);
        Assert.Equal("Bonjour la France", result.Programs[0].Title);
        Assert.Equal(TimeSpan.FromHours(2), result.Programs[0].StartsAt.Offset);
    }

    [Fact]
    public void Matcher_prefers_explicit_guide_id_then_uses_normalized_name()
    {
        var sourceId = Guid.NewGuid();
        var matcher = new EpgChannelMatcher();
        var guideChannels = new[]
        {
            new EpgChannel("tf1.fr", ["TF1"]),
            new EpgChannel("france2.fr", ["France 2", "France2"])
        };

        var explicitMatch = new MediaItem(sourceId, "one", MediaItemKind.LiveChannel, "TF1 HD", guideId: "tf1.fr");
        var nameMatch = new MediaItem(sourceId, "two", MediaItemKind.LiveChannel, "France 2");

        Assert.Equal("tf1.fr", matcher.Match(explicitMatch, guideChannels));
        Assert.Equal("france2.fr", matcher.Match(nameMatch, guideChannels));
    }

    [Fact]
    public async Task Xmltv_provider_does_not_echo_sensitive_endpoint_on_http_failure()
    {
        var source = new EpgSource(Guid.NewGuid(), Guid.NewGuid(), "Guide", XmlTvEpgProvider.ProviderId, "secure:epg:1");
        var provider = new XmlTvEpgProvider(
            new FixtureCredentialStore("https://guide.example.test/xmltv.php?username=demo&password=very-secret"),
            new XmlTvClient(new HttpClient(new FixtureHttpHandler("denied", HttpStatusCode.Unauthorized))),
            new XmlTvParser());
        var request = new EpgGuideRequest(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(3));

        var error = await Assert.ThrowsAsync<EpgLoadException>(() => provider.GetAsync(source, request));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("very-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("guide.example.test", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Guide_service_matches_live_channels_and_uses_cache()
    {
        var mediaSource = new MediaSource(Guid.NewGuid(), "Maison", "fixture", "secure:media");
        var epgSource = new EpgSource(Guid.NewGuid(), mediaSource.Id, "Guide", "fixture-epg", "secure:epg");
        var start = new DateTimeOffset(2026, 9, 17, 7, 30, 0, TimeSpan.FromHours(2));
        var end = start.AddHours(2);
        var channel = new MediaItem(mediaSource.Id, "fixture:tf1", MediaItemKind.LiveChannel, "TF1 HD", guideId: "tf1.fr");
        var mediaProvider = new FixtureMediaProvider(channel);
        var epgProvider = new FixtureEpgProvider(new XmlTvParser().Parse(XmlTv, start, end));
        var cache = new MemoryEpgCache();
        var catalog = new FixtureMediaCatalog(mediaProvider);
        var service = new EpgGuideService(catalog, new EpgProviderResolver([epgProvider]), new EpgChannelMatcher(), cache);

        var first = await service.GetGuideAsync(mediaSource, epgSource, new EpgGuideRequest(start, end));
        var second = await service.GetGuideAsync(mediaSource, epgSource, new EpgGuideRequest(start, end));

        var row = Assert.Single(first.Channels);
        Assert.Equal("tf1.fr", row.MatchedGuideId);
        Assert.Equal(2, row.Programs.Count);
        Assert.Equal(1, epgProvider.CallCount);
        Assert.Equal(first.RefreshedAt, second.RefreshedAt);
    }

    [Fact]
    public async Task File_cache_roundtrips_without_credentials_or_stream_urls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "universalremote-epg-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var mediaSourceId = Guid.NewGuid();
            var epgSourceId = Guid.NewGuid();
            var start = DateTimeOffset.UtcNow;
            var end = start.AddHours(2);
            var channel = new MediaItem(mediaSourceId, "m3u:tvg:tf1.fr", MediaItemKind.LiveChannel, "TF1", guideId: "tf1.fr");
            var program = new EpgProgram("tf1.fr", "Journal", start, start.AddMinutes(30));
            var snapshot = new EpgGuideSnapshot(mediaSourceId, epgSourceId, start, end, [new EpgGuideChannel(channel, "tf1.fr", [program])], DateTimeOffset.UtcNow);
            var cache = new FileEpgCache(directory);

            await cache.SetAsync(snapshot);
            var restored = await cache.GetAsync(mediaSourceId, epgSourceId, start, end);

            Assert.NotNull(restored);
            Assert.Equal("TF1", restored!.Channels[0].Channel.Title);
            var json = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "*.json")));
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("stream", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Dependency_injection_registers_xmltv_epg_stack()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "universalremote-epg-di", Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IMediaCredentialStore>(new FixtureCredentialStore("https://guide.example.test/epg.xml"));
            services.AddSingleton<IMediaCatalog>(new EmptyMediaCatalog());
            services.AddUniversalRemoteMediaEpg(cacheDirectory);
            using var root = services.BuildServiceProvider();
            using var scope = root.CreateScope();

            Assert.Equal(XmlTvEpgProvider.ProviderId, Assert.Single(scope.ServiceProvider.GetServices<IEpgProvider>()).Id);
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEpgGuideService>());
            Assert.IsType<FileEpgCache>(scope.ServiceProvider.GetRequiredService<IEpgCache>());
        }
        finally
        {
            if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private sealed class FixtureCredentialStore(string secret) : IMediaCredentialStore
    {
        public Task<MediaSecret?> GetAsync(string credentialReference, CancellationToken cancellationToken = default) => Task.FromResult<MediaSecret?>(new MediaSecret(secret));
        public Task SetAsync(string credentialReference, MediaSecret value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(string credentialReference, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FixtureHttpHandler(string content, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(content, Encoding.UTF8, "application/xml") });
    }

    private sealed class FixtureEpgProvider(EpgDocumentSnapshot snapshot) : IEpgProvider
    {
        public string Id => "fixture-epg";
        public int CallCount { get; private set; }
        public Task<EpgDocumentSnapshot> GetAsync(EpgSource source, EpgGuideRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FixtureMediaProvider(MediaItem item) : IMediaProvider
    {
        public string Id => "fixture";
        public Task<MediaCatalogSnapshot> GetCatalogAsync(MediaSource source, MediaCatalogRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new MediaCatalogSnapshot(source.Id, [item], DateTimeOffset.UtcNow));
    }

    private sealed class FixtureMediaCatalog(FixtureMediaProvider provider) : IMediaCatalog
    {
        public Task<MediaCatalogSnapshot> GetAsync(MediaSource source, MediaCatalogRequest? request = null, CancellationToken cancellationToken = default)
            => provider.GetCatalogAsync(source, request ?? new MediaCatalogRequest(), cancellationToken);
    }

    private sealed class EmptyMediaCatalog : IMediaCatalog
    {
        public Task<MediaCatalogSnapshot> GetAsync(MediaSource source, MediaCatalogRequest? request = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new MediaCatalogSnapshot(source.Id, Array.Empty<MediaItem>(), DateTimeOffset.UtcNow));
    }

    private sealed class MemoryEpgCache : IEpgCache
    {
        private EpgGuideSnapshot? snapshot;
        public Task<EpgGuideSnapshot?> GetAsync(Guid mediaSourceId, Guid epgSourceId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default)
            => Task.FromResult(snapshot is { } value && value.MediaSourceId == mediaSourceId && value.EpgSourceId == epgSourceId && value.WindowStart == windowStart && value.WindowEnd == windowEnd ? value : null);
        public Task SetAsync(EpgGuideSnapshot value, CancellationToken cancellationToken = default) { snapshot = value; return Task.CompletedTask; }
        public Task ClearAsync(Guid mediaSourceId, Guid epgSourceId, CancellationToken cancellationToken = default) { snapshot = null; return Task.CompletedTask; }
    }
}
