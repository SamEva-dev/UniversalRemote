using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.Xtream;

/// <summary>
/// Resolves Xtream catalogue identifiers into ephemeral player URLs. Credentials exist only in this provider layer
/// and in the returned ResolvedMediaStream; neither value is logged or copied into PlaybackSession.
/// </summary>
public sealed class XtreamStreamResolver : IStreamResolver
{
    private readonly XtreamMediaProvider provider;
    private readonly XtreamApiClient client;

    public string ProviderId => XtreamMediaProvider.ProviderId;

    public XtreamStreamResolver(XtreamMediaProvider provider, XtreamApiClient client)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ResolvedMediaStream> ResolveAsync(
        MediaSource source,
        MediaItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(item);
        XtreamMediaProvider.ValidateSource(source);
        if (item.SourceId != source.Id)
            throw new ArgumentException("Media item does not belong to the selected source.", nameof(item));

        var credentials = await provider.GetCredentialsAsync(source, cancellationToken).ConfigureAwait(false);
        await client.ValidateAuthenticationAsync(credentials, cancellationToken).ConfigureAwait(false);

        return item.Kind switch
        {
            MediaItemKind.LiveChannel => await ResolveStreamAsync(credentials, item, "xtream:live:", "get_live_streams", "live", "ts", isLive: true, cancellationToken: cancellationToken).ConfigureAwait(false),
            MediaItemKind.Movie => await ResolveStreamAsync(credentials, item, "xtream:movie:", "get_vod_streams", "movie", "mp4", isLive: false, cancellationToken: cancellationToken).ConfigureAwait(false),
            MediaItemKind.Episode => ResolveEpisode(credentials, item),
            MediaItemKind.Series => throw new NotSupportedException("Select an episode before starting series playback."),
            _ => throw new NotSupportedException("The selected media kind is not supported by the Xtream playback resolver.")
        };
    }

    private async Task<ResolvedMediaStream> ResolveStreamAsync(
        XtreamCredentials credentials,
        MediaItem item,
        string externalPrefix,
        string apiAction,
        string pathFamily,
        string fallbackExtension,
        bool isLive,
        CancellationToken cancellationToken)
    {
        var streamId = ParseSimpleId(item.ExternalId, externalPrefix);
        var streams = await client.GetStreamsAsync(credentials, apiAction, cancellationToken).ConfigureAwait(false);
        var descriptor = streams.FirstOrDefault(x => string.Equals(x.Id, streamId, StringComparison.OrdinalIgnoreCase))
                         ?? throw new XtreamSourceConfigurationException("The selected media item is no longer available from the Xtream source.");
        var extension = NormalizeExtension(descriptor.ContainerExtension, fallbackExtension);
        return new ResolvedMediaStream(
            BuildPlaybackUri(credentials, pathFamily, streamId, extension),
            isLive,
            ContentType(extension));
    }

    private static ResolvedMediaStream ResolveEpisode(XtreamCredentials credentials, MediaItem item)
    {
        const string prefix = "xtream:episode:";
        if (!item.ExternalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new XtreamSourceConfigurationException("The selected episode ID is invalid.");

        var locator = item.ExternalId[prefix.Length..];
        var separator = locator.LastIndexOf(':');
        var episodeId = separator > 0 ? locator[..separator] : locator;
        var extension = separator > 0 ? locator[(separator + 1)..] : "mkv";
        ValidateProviderId(episodeId);
        extension = NormalizeExtension(extension, "mkv");

        return new ResolvedMediaStream(
            BuildPlaybackUri(credentials, "series", episodeId, extension),
            isLive: false,
            contentType: ContentType(extension));
    }

    private static string ParseSimpleId(string externalId, string prefix)
    {
        if (!externalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new XtreamSourceConfigurationException("The selected media ID is invalid.");
        var id = externalId[prefix.Length..];
        ValidateProviderId(id);
        return id;
    }

    private static void ValidateProviderId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 120 || id.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
            throw new XtreamSourceConfigurationException("The selected media ID is invalid.");
    }

    internal static string NormalizeExtension(string? extension, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(extension) ? fallback : extension.Trim().TrimStart('.');
        if (candidate.Length is < 1 or > 12 || candidate.Any(c => !char.IsLetterOrDigit(c)))
            return fallback;
        return candidate.ToLowerInvariant();
    }

    private static Uri BuildPlaybackUri(XtreamCredentials credentials, string family, string streamId, string extension)
    {
        var relative = string.Join('/',
            Uri.EscapeDataString(family),
            Uri.EscapeDataString(credentials.Username),
            Uri.EscapeDataString(credentials.Password),
            $"{Uri.EscapeDataString(streamId)}.{Uri.EscapeDataString(extension)}");
        return new Uri(credentials.ServerBaseUri, relative);
    }

    private static string? ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        "m3u8" => "application/vnd.apple.mpegurl",
        "mpd" => "application/dash+xml",
        "ts" => "video/mp2t",
        "mp4" => "video/mp4",
        "mkv" => "video/x-matroska",
        "avi" => "video/x-msvideo",
        _ => null
    };
}
