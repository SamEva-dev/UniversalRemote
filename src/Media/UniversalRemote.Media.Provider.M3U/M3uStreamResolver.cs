using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.M3U;

/// <summary>Resolves an M3U catalogue ID back to its ephemeral stream URI only when playback starts.</summary>
public sealed class M3uStreamResolver : IStreamResolver
{
    private readonly M3uMediaProvider provider;
    private readonly M3uPlaylistClient client;
    private readonly M3uPlaylistParser parser;

    public string ProviderId => M3uMediaProvider.ProviderId;

    public M3uStreamResolver(M3uMediaProvider provider, M3uPlaylistClient client, M3uPlaylistParser parser)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<ResolvedMediaStream> ResolveAsync(
        MediaSource source,
        MediaItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(item);
        if (item.SourceId != source.Id)
            throw new ArgumentException("Media item does not belong to the selected source.", nameof(item));
        if (item.Kind != MediaItemKind.LiveChannel)
            throw new NotSupportedException("M3U playback currently supports live catalogue entries only.");

        var playlistUri = await provider.GetPlaylistUriAsync(source, cancellationToken).ConfigureAwait(false);
        var content = await client.DownloadAsync(playlistUri, cancellationToken).ConfigureAwait(false);
        var playlist = parser.Parse(content, playlistUri);
        var match = M3uEntryIdentityFactory.Create(playlist.Entries)
            .FirstOrDefault(x => string.Equals(x.ExternalId, item.ExternalId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            throw new M3uSourceConfigurationException("The selected channel is no longer present in the playlist.");

        return new ResolvedMediaStream(match.Entry.StreamUri, isLive: true, contentType: ContentType(match.Entry.StreamUri));
    }

    private static string? ContentType(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) return "application/vnd.apple.mpegurl";
        if (path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)) return "application/dash+xml";
        if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) return "video/mp2t";
        if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return "video/mp4";
        return null;
    }
}
