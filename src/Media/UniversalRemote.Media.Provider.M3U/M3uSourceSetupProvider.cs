using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.M3U;

public sealed class M3uSourceSetupProvider : IMediaSourceSetupProvider
{
    private static readonly IReadOnlyList<MediaSourceSetupField> SetupFields =
    [
        new("playlistUrl", "Playlist M3U/M3U8", "https://…/playlist.m3u", MediaSourceSetupFieldKind.Uri)
    ];

    public string ProviderId => M3uMediaProvider.ProviderId;
    public string DisplayName => "M3U / M3U8";
    public IReadOnlyList<MediaSourceSetupField> Fields => SetupFields;

    public MediaSecret CreateSecret(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!values.TryGetValue("playlistUrl", out var raw) ||
            !Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new M3uSourceConfigurationException("M3U playlist URI must use HTTP or HTTPS.");
        return new MediaSecret(uri.AbsoluteUri);
    }
}
