using System.Buffers;
using System.Net;
using System.Text.Json;

namespace UniversalRemote.Media.Provider.Xtream;

/// <summary>
/// Tolerant client for the de-facto Xtream Player API. Request URLs can contain credentials,
/// therefore failures are intentionally rethrown without request URI or inner exception details.
/// </summary>
public sealed class XtreamApiClient
{
    public const int DefaultMaximumResponseBytes = 32 * 1024 * 1024;
    private const int MaximumRedirects = 3;

    private readonly HttpClient httpClient;
    private readonly int maximumResponseBytes;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public XtreamApiClient(HttpClient httpClient) : this(httpClient, DefaultMaximumResponseBytes) { }

    public XtreamApiClient(HttpClient httpClient, int maximumResponseBytes)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (maximumResponseBytes < 64 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes), "Xtream response limit must be at least 64 KiB.");
        this.maximumResponseBytes = maximumResponseBytes;
    }

    internal async Task ValidateAuthenticationAsync(XtreamCredentials credentials, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(credentials, action: null, parameters: null, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new XtreamApiException(
                "The Xtream server returned an invalid authentication payload.",
                failureKind: XtreamApiFailureKind.InvalidPayload);

        // Most Xtream panels expose { user_info: { auth, status } }. Some compatible panels expose
        // the same fields directly at the root, so accept both shapes.
        var userInfo = root.TryGetProperty("user_info", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : root;

        var hasAuth = userInfo.TryGetProperty("auth", out _);
        var authenticated = XtreamJson.IsTruthy(userInfo, "auth");
        var status = XtreamJson.String(userInfo, "status");
        var activeStatus = status is not null &&
            (status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
             status.Equals("Enabled", StringComparison.OrdinalIgnoreCase));

        // Compatibility: a few panels omit "auth" but explicitly report an Active/Enabled status.
        if ((!hasAuth && activeStatus) || (authenticated && (status is null || activeStatus)))
            return;

        throw new XtreamAuthenticationException("The Xtream account is not authenticated or active.");
    }

    internal async Task<IReadOnlyList<XtreamCategory>> GetCategoriesAsync(
        XtreamCredentials credentials,
        string action,
        CancellationToken cancellationToken)
    {
        using var document = await GetAsync(credentials, action, null, cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new XtreamApiException(
                "The Xtream server returned an invalid category payload.",
                failureKind: XtreamApiFailureKind.InvalidPayload);

        var results = new List<XtreamCategory>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var id = XtreamJson.String(element, "category_id");
            var name = XtreamJson.String(element, "category_name");
            if (id is null || name is null) continue;
            results.Add(new XtreamCategory(id, name));
        }
        return results;
    }

    internal async Task<IReadOnlyList<XtreamStream>> GetStreamsAsync(
        XtreamCredentials credentials,
        string action,
        CancellationToken cancellationToken)
    {
        using var document = await GetAsync(credentials, action, null, cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new XtreamApiException(
                "The Xtream server returned an invalid stream payload.",
                failureKind: XtreamApiFailureKind.InvalidPayload);

        var results = new List<XtreamStream>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var id = XtreamJson.String(element, "stream_id");
            var name = XtreamJson.String(element, "name");
            if (id is null || name is null) continue;
            results.Add(new XtreamStream(
                id,
                name,
                XtreamJson.String(element, "category_id"),
                XtreamJson.AbsoluteHttpUri(XtreamJson.String(element, "stream_icon")),
                XtreamJson.String(element, "container_extension")));
        }
        return results;
    }

    internal async Task<IReadOnlyList<XtreamSeries>> GetSeriesAsync(
        XtreamCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var document = await GetAsync(credentials, "get_series", null, cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new XtreamApiException(
                "The Xtream server returned an invalid series payload.",
                failureKind: XtreamApiFailureKind.InvalidPayload);

        var results = new List<XtreamSeries>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var id = XtreamJson.String(element, "series_id");
            var name = XtreamJson.String(element, "name");
            if (id is null || name is null) continue;
            results.Add(new XtreamSeries(
                id,
                name,
                XtreamJson.String(element, "category_id"),
                XtreamJson.AbsoluteHttpUri(XtreamJson.String(element, "cover"))));
        }
        return results;
    }

    internal async Task<XtreamSeriesInfo> GetSeriesInfoAsync(
        XtreamCredentials credentials,
        string seriesId,
        CancellationToken cancellationToken)
    {
        using var document = await GetAsync(
            credentials,
            "get_series_info",
            new Dictionary<string, string> { ["series_id"] = seriesId },
            cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new XtreamApiException(
                "The Xtream server returned an invalid series detail payload.",
                failureKind: XtreamApiFailureKind.InvalidPayload);

        var title = root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object
            ? XtreamJson.String(info, "name") ?? XtreamJson.String(info, "title") ?? "Series"
            : "Series";

        var seasonArtwork = ParseSeasonArtwork(root);
        var episodes = ParseEpisodes(root);
        return new XtreamSeriesInfo(title, seasonArtwork, episodes);
    }

    private async Task<JsonDocument> GetAsync(
        XtreamCredentials credentials,
        string? action,
        IReadOnlyDictionary<string, string>? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        cancellationToken.ThrowIfCancellationRequested();

        var endpoint = BuildEndpoint(credentials, action, parameters);
        for (var redirectCount = 0; redirectCount <= MaximumRedirects; redirectCount++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Accept.ParseAdd("application/json, text/json, */*");
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new XtreamApiException(
                    "The Xtream server request timed out.",
                    failureKind: XtreamApiFailureKind.Timeout);
            }
            catch (HttpRequestException)
            {
                throw new XtreamApiException(
                    "The Xtream server could not be reached.",
                    failureKind: XtreamApiFailureKind.Network);
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount == MaximumRedirects || response.Headers.Location is null)
                        throw new XtreamApiException(
                            "The Xtream server returned an unsupported redirect.",
                            response.StatusCode,
                            XtreamApiFailureKind.Redirect);

                    var redirected = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(endpoint, response.Headers.Location);

                    // Never forward credentials to another host automatically. Same-host redirects are common
                    // when a panel normalizes HTTP/HTTPS or an API path, and are safe to follow explicitly.
                    if (!redirected.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase))
                        throw new XtreamApiException(
                            "The Xtream server redirects to another host.",
                            response.StatusCode,
                            XtreamApiFailureKind.Redirect);

                    if (string.IsNullOrEmpty(redirected.Query))
                    {
                        redirected = new UriBuilder(redirected) { Query = endpoint.Query.TrimStart('?') }.Uri;
                    }

                    endpoint = redirected;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new XtreamApiException(
                        "The Xtream server returned an HTTP error.",
                        response.StatusCode,
                        XtreamApiFailureKind.HttpStatus);

                if (response.Content.Headers.ContentLength is long declared && declared > maximumResponseBytes)
                    throw new XtreamApiException(
                        "The Xtream server response exceeds the configured size limit.",
                        failureKind: XtreamApiFailureKind.ResponseTooLarge);

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var payload = await ReadLimitedAsync(stream, maximumResponseBytes, cancellationToken).ConfigureAwait(false);
                try
                {
                    return JsonDocument.Parse(payload);
                }
                catch (JsonException)
                {
                    throw new XtreamApiException(
                        "The Xtream server returned a non-JSON response.",
                        failureKind: XtreamApiFailureKind.InvalidJson);
                }
            }
        }

        throw new XtreamApiException(
            "The Xtream server redirect limit was exceeded.",
            failureKind: XtreamApiFailureKind.Redirect);
    }

    private static Uri BuildEndpoint(
        XtreamCredentials credentials,
        string? action,
        IReadOnlyDictionary<string, string>? parameters)
    {
        var endpoint = BuildPlayerApiUri(credentials.ServerBaseUri);
        var values = new List<KeyValuePair<string, string>>
        {
            new("username", credentials.Username),
            new("password", credentials.Password)
        };
        if (!string.IsNullOrWhiteSpace(action)) values.Add(new("action", action));
        if (parameters is not null) values.AddRange(parameters);

        var query = string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        return new UriBuilder(endpoint) { Query = query }.Uri;
    }

    private static Uri BuildPlayerApiUri(Uri serverBaseUri)
    {
        var path = serverBaseUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/player_api.php", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/get.php", StringComparison.OrdinalIgnoreCase))
        {
            var slash = path.LastIndexOf('/');
            path = slash <= 0 ? string.Empty : path[..slash];
        }

        var apiPath = string.IsNullOrWhiteSpace(path)
            ? "/player_api.php"
            : $"{path}/player_api.php";

        return new UriBuilder(serverBaseUri)
        {
            Path = apiPath,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static IReadOnlyDictionary<int, Uri?> ParseSeasonArtwork(JsonElement root)
    {
        var result = new Dictionary<int, Uri?>();
        if (!root.TryGetProperty("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array) return result;

        foreach (var season in seasons.EnumerateArray())
        {
            if (season.ValueKind != JsonValueKind.Object) continue;
            var number = XtreamJson.Int32(season, "season_number");
            if (number is null || number < 0) continue;
            var cover = XtreamJson.AbsoluteHttpUri(XtreamJson.String(season, "cover"))
                        ?? XtreamJson.AbsoluteHttpUri(XtreamJson.String(season, "cover_big"));
            result[number.Value] = cover;
        }
        return result;
    }

    private static IReadOnlyList<XtreamEpisode> ParseEpisodes(JsonElement root)
    {
        var result = new List<XtreamEpisode>();
        if (!root.TryGetProperty("episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Object) return result;

        foreach (var seasonProperty in episodes.EnumerateObject())
        {
            _ = int.TryParse(seasonProperty.Name, out var seasonFromKey);
            if (seasonProperty.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var episode in seasonProperty.Value.EnumerateArray())
            {
                if (episode.ValueKind != JsonValueKind.Object) continue;
                var id = XtreamJson.String(episode, "id");
                if (id is null) continue;
                var episodeNumber = XtreamJson.Int32(episode, "episode_num") ?? 0;
                var seasonNumber = XtreamJson.Int32(episode, "season") ?? seasonFromKey;
                var title = XtreamJson.String(episode, "title") ?? $"Episode {episodeNumber}";

                TimeSpan? duration = null;
                Uri? artwork = null;
                var containerExtension = XtreamJson.String(episode, "container_extension");
                if (episode.TryGetProperty("info", out var episodeInfo) && episodeInfo.ValueKind == JsonValueKind.Object)
                {
                    duration = XtreamJson.DurationSeconds(episodeInfo, "duration_secs", "duration_seconds");
                    artwork = XtreamJson.AbsoluteHttpUri(XtreamJson.String(episodeInfo, "movie_image"));
                    containerExtension ??= XtreamJson.String(episodeInfo, "container_extension");
                }

                result.Add(new XtreamEpisode(id, title, Math.Max(0, seasonNumber), Math.Max(0, episodeNumber), duration, artwork, containerExtension));
            }
        }
        return result;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var output = new MemoryStream(Math.Min(maximumBytes, 512 * 1024));
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > maximumBytes)
                    throw new XtreamApiException(
                        "The Xtream server response exceeds the configured size limit.",
                        failureKind: XtreamApiFailureKind.ResponseTooLarge);
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
