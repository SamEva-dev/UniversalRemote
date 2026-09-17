using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.Xtream;

/// <summary>
/// Normalizes an Xtream-compatible Player API into UniversalRemote Media concepts.
/// Catalogue refresh loads Live/VOD/Series only; episodes are loaded lazily through IMediaSeriesProvider.
/// </summary>
public sealed class XtreamMediaProvider : IMediaProvider, IMediaSeriesProvider
{
    public const string ProviderId = "xtream";

    private readonly IMediaCredentialStore credentialStore;
    private readonly XtreamApiClient client;

    public string Id => ProviderId;

    public XtreamMediaProvider(IMediaCredentialStore credentialStore, XtreamApiClient client)
    {
        this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<MediaCatalogSnapshot> GetCatalogAsync(
        MediaSource source,
        MediaCatalogRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);
        ArgumentNullException.ThrowIfNull(request);
        var credentials = await GetCredentialsAsync(source, cancellationToken).ConfigureAwait(false);
        await client.ValidateAuthenticationAsync(credentials, cancellationToken).ConfigureAwait(false);

        var items = new List<MediaItem>();
        if (request.Kinds.Contains(MediaItemKind.LiveChannel))
            items.AddRange(await LoadLiveAsync(source.Id, credentials, cancellationToken).ConfigureAwait(false));
        if (request.Kinds.Contains(MediaItemKind.Movie))
            items.AddRange(await LoadMoviesAsync(source.Id, credentials, cancellationToken).ConfigureAwait(false));
        if (request.Kinds.Contains(MediaItemKind.Series))
            items.AddRange(await LoadSeriesAsync(source.Id, credentials, cancellationToken).ConfigureAwait(false));

        return new MediaCatalogSnapshot(source.Id, items, DateTimeOffset.UtcNow);
    }

    public async Task<MediaSeriesDetails> GetSeriesAsync(
        MediaSource source,
        string seriesExternalId,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesExternalId);
        var normalizedSeriesExternalId = seriesExternalId.Trim();
        var seriesId = ParseExternalId(normalizedSeriesExternalId, "xtream:series:");
        var credentials = await GetCredentialsAsync(source, cancellationToken).ConfigureAwait(false);
        await client.ValidateAuthenticationAsync(credentials, cancellationToken).ConfigureAwait(false);

        var info = await client.GetSeriesInfoAsync(credentials, seriesId, cancellationToken).ConfigureAwait(false);
        var episodes = info.Episodes.Select(x => new MediaEpisode(
            source.Id,
            normalizedSeriesExternalId,
            $"xtream:episode:{x.Id}:{XtreamStreamResolver.NormalizeExtension(x.ContainerExtension, "mkv")}",
            x.Title,
            x.SeasonNumber,
            x.EpisodeNumber,
            x.Duration,
            x.ArtworkUri)).ToArray();

        var seasons = episodes
            .GroupBy(x => x.SeasonNumber)
            .OrderBy(x => x.Key)
            .Select(group => new MediaSeason(
                group.Key,
                group.Key == 0 ? "Specials" : $"Season {group.Key}",
                group,
                info.SeasonArtwork.GetValueOrDefault(group.Key)))
            .ToArray();

        return new MediaSeriesDetails(source.Id, normalizedSeriesExternalId, info.Title, seasons);
    }

    private async Task<IReadOnlyList<MediaItem>> LoadLiveAsync(Guid sourceId, XtreamCredentials credentials, CancellationToken cancellationToken)
    {
        var categoriesTask = client.GetCategoriesAsync(credentials, "get_live_categories", cancellationToken);
        var streamsTask = client.GetStreamsAsync(credentials, "get_live_streams", cancellationToken);
        await Task.WhenAll(categoriesTask, streamsTask).ConfigureAwait(false);
        var categories = ToCategoryMap(await categoriesTask.ConfigureAwait(false));
        return (await streamsTask.ConfigureAwait(false))
            .Select(x => new MediaItem(sourceId, $"xtream:live:{x.Id}", MediaItemKind.LiveChannel, x.Name, CategoryName(categories, x.CategoryId), artworkUri: x.ArtworkUri))
            .ToArray();
    }

    private async Task<IReadOnlyList<MediaItem>> LoadMoviesAsync(Guid sourceId, XtreamCredentials credentials, CancellationToken cancellationToken)
    {
        var categoriesTask = client.GetCategoriesAsync(credentials, "get_vod_categories", cancellationToken);
        var streamsTask = client.GetStreamsAsync(credentials, "get_vod_streams", cancellationToken);
        await Task.WhenAll(categoriesTask, streamsTask).ConfigureAwait(false);
        var categories = ToCategoryMap(await categoriesTask.ConfigureAwait(false));
        return (await streamsTask.ConfigureAwait(false))
            .Select(x => new MediaItem(sourceId, $"xtream:movie:{x.Id}", MediaItemKind.Movie, x.Name, CategoryName(categories, x.CategoryId), artworkUri: x.ArtworkUri))
            .ToArray();
    }

    private async Task<IReadOnlyList<MediaItem>> LoadSeriesAsync(Guid sourceId, XtreamCredentials credentials, CancellationToken cancellationToken)
    {
        var categoriesTask = client.GetCategoriesAsync(credentials, "get_series_categories", cancellationToken);
        var seriesTask = client.GetSeriesAsync(credentials, cancellationToken);
        await Task.WhenAll(categoriesTask, seriesTask).ConfigureAwait(false);
        var categories = ToCategoryMap(await categoriesTask.ConfigureAwait(false));
        return (await seriesTask.ConfigureAwait(false))
            .Select(x => new MediaItem(sourceId, $"xtream:series:{x.Id}", MediaItemKind.Series, x.Name, CategoryName(categories, x.CategoryId), artworkUri: x.ArtworkUri))
            .ToArray();
    }

    internal async Task<XtreamCredentials> GetCredentialsAsync(MediaSource source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.CredentialReference))
            throw new XtreamSourceConfigurationException("The Xtream source has no credential reference.");
        var secret = await credentialStore.GetAsync(source.CredentialReference, cancellationToken).ConfigureAwait(false)
                     ?? throw new XtreamSourceConfigurationException("The Xtream source credentials are unavailable.");
        return XtreamCredentialCodec.Decode(secret);
    }

    internal static void ValidateSource(MediaSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Media source does not belong to the Xtream provider.", nameof(source));
    }

    private static IReadOnlyDictionary<string, string> ToCategoryMap(IEnumerable<XtreamCategory> categories)
        => categories
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Name, StringComparer.OrdinalIgnoreCase);

    private static string? CategoryName(IReadOnlyDictionary<string, string> categories, string? categoryId)
        => categoryId is not null && categories.TryGetValue(categoryId, out var name) ? name : null;

    private static string ParseExternalId(string externalId, string prefix)
    {
        var normalized = externalId.Trim();
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Series external ID does not belong to the Xtream provider.", nameof(externalId));
        var providerId = normalized[prefix.Length..];
        if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 120)
            throw new ArgumentException("Series external ID is invalid.", nameof(externalId));
        return providerId;
    }
}
