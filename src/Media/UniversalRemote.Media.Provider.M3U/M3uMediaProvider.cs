using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.M3U;

/// <summary>
/// M3U/M3U8 catalogue provider. Playlist location is retrieved from secure credential storage and never
/// copied into normalized MediaItem objects.
/// </summary>
public sealed class M3uMediaProvider : IMediaProvider
{
    public const string ProviderId = "m3u";

    private readonly IMediaCredentialStore credentialStore;
    private readonly M3uPlaylistClient client;
    private readonly M3uPlaylistParser parser;

    public string Id => ProviderId;

    public M3uMediaProvider(
        IMediaCredentialStore credentialStore,
        M3uPlaylistClient client,
        M3uPlaylistParser parser)
    {
        this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<MediaCatalogSnapshot> GetCatalogAsync(
        MediaSource source,
        MediaCatalogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateSource(source);
        var playlistUri = await GetPlaylistUriAsync(source, cancellationToken).ConfigureAwait(false);
        var content = await client.DownloadAsync(playlistUri, cancellationToken).ConfigureAwait(false);
        var playlist = parser.Parse(content, playlistUri);

        var items = request.Kinds.Contains(MediaItemKind.LiveChannel)
            ? MapEntries(source.Id, playlist.Entries)
            : Array.Empty<MediaItem>();

        return new MediaCatalogSnapshot(source.Id, items, DateTimeOffset.UtcNow);
    }

    internal async Task<Uri> GetPlaylistUriAsync(MediaSource source, CancellationToken cancellationToken)
    {
        ValidateSource(source);
        var secret = await credentialStore.GetAsync(source.CredentialReference!, cancellationToken).ConfigureAwait(false)
            ?? throw new M3uSourceConfigurationException("The M3U source credentials are unavailable.");
        return ParsePlaylistUri(secret);
    }

    internal static void ValidateSource(MediaSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Media source does not belong to the M3U provider.", nameof(source));
        if (string.IsNullOrWhiteSpace(source.CredentialReference))
            throw new M3uSourceConfigurationException("The M3U source has no credential reference.");
    }

    private static Uri ParsePlaylistUri(MediaSecret secret)
    {
        var raw = secret.Reveal().Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new M3uSourceConfigurationException("The M3U source credentials do not contain a valid HTTP(S) playlist URI.");

        return uri;
    }

    private static MediaItem[] MapEntries(Guid sourceId, IReadOnlyList<M3uPlaylistEntry> entries)
        => M3uEntryIdentityFactory.Create(entries)
            .Select(x => new MediaItem(
                sourceId,
                x.ExternalId,
                MediaItemKind.LiveChannel,
                x.Entry.Title,
                x.Entry.GroupTitle,
                artworkUri: x.Entry.LogoUri,
                guideId: x.Entry.TvgId))
            .ToArray();
}
