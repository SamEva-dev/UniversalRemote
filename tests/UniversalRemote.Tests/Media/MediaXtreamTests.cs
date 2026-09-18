using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Provider.Xtream;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class MediaXtreamTests
{
    [Fact]
    public void Credential_codec_round_trips_without_exposing_secrets_in_ToString()
    {
        var credentials = new XtreamCredentials(new Uri("https://media.example.test:8443/base"), "alice@example.test", "very-secret");
        var secret = XtreamCredentialCodec.Encode(credentials);
        var decoded = XtreamCredentialCodec.Decode(secret);

        Assert.Equal("https://media.example.test:8443/base/", decoded.ServerBaseUri.AbsoluteUri);
        Assert.Equal("alice@example.test", decoded.Username);
        Assert.Equal("very-secret", decoded.Password);
        Assert.DoesNotContain("alice@example.test", credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret", credentials.ToString(), StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", secret.ToString());
    }

    [Fact]
    public async Task Provider_normalizes_live_vod_and_series_with_category_names()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:home");
        var provider = CreateProvider(new FixtureCredentialStore(ValidSecret()), new FixtureHttpHandler());

        var result = await provider.GetCatalogAsync(source, new MediaCatalogRequest([
            MediaItemKind.LiveChannel,
            MediaItemKind.Movie,
            MediaItemKind.Series
        ]));

        Assert.Equal(3, result.Items.Count);

        var live = Assert.Single(result.Items.Where(x => x.Kind == MediaItemKind.LiveChannel));
        Assert.Equal("France 2", live.Title);
        Assert.Equal("France", live.Category);
        Assert.Equal("xtream:live:101", live.ExternalId);

        var movie = Assert.Single(result.Items.Where(x => x.Kind == MediaItemKind.Movie));
        Assert.Equal("Demo Movie", movie.Title);
        Assert.Equal("Cinema", movie.Category);
        Assert.Equal("xtream:movie:202", movie.ExternalId);

        var series = Assert.Single(result.Items.Where(x => x.Kind == MediaItemKind.Series));
        Assert.Equal("Demo Series", series.Title);
        Assert.Equal("Drama", series.Category);
        Assert.Equal("xtream:series:303", series.ExternalId);

        Assert.DoesNotContain("username", string.Join('|', result.Items), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("very-secret", string.Join('|', result.Items), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_honors_catalogue_kind_filter_without_fetching_unrequested_families()
    {
        var handler = new FixtureHttpHandler();
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:movies");
        var provider = CreateProvider(new FixtureCredentialStore(ValidSecret()), handler);

        var result = await provider.GetCatalogAsync(source, new MediaCatalogRequest([MediaItemKind.Movie]));

        var movie = Assert.Single(result.Items);
        Assert.Equal(MediaItemKind.Movie, movie.Kind);
        Assert.Contains("get_vod_categories", handler.RequestedActions);
        Assert.Contains("get_vod_streams", handler.RequestedActions);
        Assert.DoesNotContain("get_live_streams", handler.RequestedActions);
        Assert.DoesNotContain("get_series", handler.RequestedActions);
    }

    [Fact]
    public async Task Series_details_are_loaded_lazily_and_normalized_into_seasons_and_episodes()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:series");
        var provider = CreateProvider(new FixtureCredentialStore(ValidSecret()), new FixtureHttpHandler());

        var details = await provider.GetSeriesAsync(source, "xtream:series:303");

        Assert.Equal("Demo Series", details.Title);
        var season = Assert.Single(details.Seasons);
        Assert.Equal(1, season.SeasonNumber);
        Assert.Equal("Season 1", season.Title);
        Assert.Equal("https://img.example.test/s1.jpg", season.ArtworkUri!.AbsoluteUri);

        var episode = Assert.Single(season.Episodes);
        Assert.Equal("xtream:episode:9001:mkv", episode.ExternalId);
        Assert.Equal(1, episode.SeasonNumber);
        Assert.Equal(1, episode.EpisodeNumber);
        Assert.Equal(TimeSpan.FromMinutes(45), episode.Duration);
        Assert.Equal("https://img.example.test/e1.jpg", episode.ArtworkUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Authentication_failure_is_sanitized()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:bad");
        var provider = CreateProvider(
            new FixtureCredentialStore(ValidSecret()),
            new FixtureHttpHandler(authenticated: false));

        var error = await Assert.ThrowsAsync<XtreamAuthenticationException>(() =>
            provider.GetCatalogAsync(source, new MediaCatalogRequest([MediaItemKind.LiveChannel])));

        Assert.DoesNotContain("very-secret", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("demo-user", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("media.example.test", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Http_failure_does_not_echo_request_uri_or_credentials()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:http-fail");
        var provider = CreateProvider(
            new FixtureCredentialStore(ValidSecret()),
            new FixtureHttpHandler(statusCode: HttpStatusCode.Unauthorized));

        var error = await Assert.ThrowsAsync<XtreamApiException>(() =>
            provider.GetCatalogAsync(source, new MediaCatalogRequest([MediaItemKind.LiveChannel])));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.DoesNotContain("very-secret", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("demo-user", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("media.example.test", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Stream_resolver_builds_live_movie_and_episode_urls_only_at_playback_time()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:play");
        var store = new FixtureCredentialStore(ValidSecret());
        var api = new XtreamApiClient(new HttpClient(new FixtureHttpHandler()));
        var provider = new XtreamMediaProvider(store, api);
        var resolver = new XtreamStreamResolver(provider, api);
        var catalogue = await provider.GetCatalogAsync(source, new MediaCatalogRequest([MediaItemKind.LiveChannel, MediaItemKind.Movie]));

        var live = await resolver.ResolveAsync(source, catalogue.Items.Single(x => x.Kind == MediaItemKind.LiveChannel));
        var movie = await resolver.ResolveAsync(source, catalogue.Items.Single(x => x.Kind == MediaItemKind.Movie));
        var details = await provider.GetSeriesAsync(source, "xtream:series:303");
        var episodeMetadata = Assert.Single(Assert.Single(details.Seasons).Episodes);
        var episodeItem = new MediaItem(source.Id, episodeMetadata.ExternalId, MediaItemKind.Episode, episodeMetadata.Title, duration: episodeMetadata.Duration);
        var episode = await resolver.ResolveAsync(source, episodeItem);

        Assert.Equal("https://media.example.test:8443/live/demo-user/very-secret/101.ts", live.StreamUri.AbsoluteUri);
        Assert.Equal("https://media.example.test:8443/movie/demo-user/very-secret/202.mkv", movie.StreamUri.AbsoluteUri);
        Assert.Equal("https://media.example.test:8443/series/demo-user/very-secret/9001.mkv", episode.StreamUri.AbsoluteUri);
        Assert.DoesNotContain("very-secret", live.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret", movie.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret", episode.ToString(), StringComparison.Ordinal);
    }


    [Fact]
    public async Task Authentication_accepts_active_status_when_compatible_panel_omits_auth_flag()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:status-only");
        var provider = CreateProvider(
            new FixtureCredentialStore(ValidSecret()),
            new RawJsonHandler("""{"user_info":{"status":"Active"}}"""));

        var result = await provider.GetCatalogAsync(source, new MediaCatalogRequest(Array.Empty<MediaItemKind>()));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Authentication_accepts_root_level_auth_shape_used_by_some_compatible_panels()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:root-auth");
        var provider = CreateProvider(
            new FixtureCredentialStore(ValidSecret()),
            new RawJsonHandler("""{"auth":1,"status":"Active"}"""));

        var result = await provider.GetCatalogAsync(source, new MediaCatalogRequest(Array.Empty<MediaItemKind>()));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Invalid_json_is_classified_without_echoing_credentials()
    {
        var source = new MediaSource(Guid.NewGuid(), "Maison", XtreamMediaProvider.ProviderId, "secure:xtream:html");
        var provider = CreateProvider(
            new FixtureCredentialStore(ValidSecret()),
            new RawJsonHandler("<html>portal</html>", "text/html"));

        var error = await Assert.ThrowsAsync<XtreamApiException>(() =>
            provider.GetCatalogAsync(source, new MediaCatalogRequest(Array.Empty<MediaItemKind>())));

        Assert.Equal(XtreamApiFailureKind.InvalidJson, error.FailureKind);
        Assert.DoesNotContain("very-secret", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("demo-user", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dependency_injection_registers_catalogue_and_series_provider_as_same_scoped_instance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediaCredentialStore>(new FixtureCredentialStore(ValidSecret()));
        services.AddUniversalRemoteXtreamProvider();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        var media = Assert.Single(scope.ServiceProvider.GetServices<IMediaProvider>());
        var series = Assert.Single(scope.ServiceProvider.GetServices<IMediaSeriesProvider>());
        Assert.Same(media, series);
        Assert.Equal(XtreamMediaProvider.ProviderId, media.Id);
        Assert.Equal(XtreamMediaProvider.ProviderId, Assert.Single(scope.ServiceProvider.GetServices<IStreamResolver>()).ProviderId);
    }

    private static MediaSecret ValidSecret()
        => XtreamCredentialCodec.Encode(new XtreamCredentials(
            new Uri("https://media.example.test:8443/"),
            "demo-user",
            "very-secret"));

    private static XtreamMediaProvider CreateProvider(IMediaCredentialStore credentialStore, HttpMessageHandler handler)
        => new(credentialStore, new XtreamApiClient(new HttpClient(handler)));

    private sealed class FixtureCredentialStore(MediaSecret? secret) : IMediaCredentialStore
    {
        public Task<MediaSecret?> GetAsync(string credentialReference, CancellationToken cancellationToken = default)
            => Task.FromResult(secret);

        public Task SetAsync(string credentialReference, MediaSecret value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> DeleteAsync(string credentialReference, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }


    private sealed class RawJsonHandler(string payload, string contentType = "application/json") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, contentType)
            });
    }

    private sealed class FixtureHttpHandler(bool authenticated = true, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> RequestedActions { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var action = QueryValue(request.RequestUri!, "action");
            if (!string.IsNullOrWhiteSpace(action)) RequestedActions.Add(action);

            var json = action switch
            {
                null or "" => authenticated
                    ? """{"user_info":{"auth":1,"status":"Active"},"server_info":{"url":"media.example.test"}}"""
                    : """{"user_info":{"auth":0,"status":"Disabled"}}""",
                "get_live_categories" => """[{"category_id":"1","category_name":"France"}]""",
                "get_live_streams" => """[{"name":"France 2","stream_id":101,"stream_icon":"https://img.example.test/f2.png","category_id":"1"}]""",
                "get_vod_categories" => """[{"category_id":"2","category_name":"Cinema"}]""",
                "get_vod_streams" => """[{"name":"Demo Movie","stream_id":"202","stream_icon":"https://img.example.test/movie.jpg","category_id":"2","container_extension":"mkv"}]""",
                "get_series_categories" => """[{"category_id":"3","category_name":"Drama"}]""",
                "get_series" => """[{"name":"Demo Series","series_id":303,"cover":"https://img.example.test/series.jpg","category_id":"3"}]""",
                "get_series_info" => """
                    {
                      "seasons":[{"season_number":1,"name":"Season 1","cover":"https://img.example.test/s1.jpg"}],
                      "info":{"name":"Demo Series"},
                      "episodes":{"1":[{"id":"9001","episode_num":1,"title":"Pilot","container_extension":"mkv","info":{"movie_image":"https://img.example.test/e1.jpg","duration_secs":2700}}]}
                    }
                    """,
                _ => "[]"
            };

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }

        private static string? QueryValue(Uri uri, string key)
        {
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]).Equals(key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(parts[1]);
            }
            return null;
        }
    }
}
