namespace UniversalRemote.Media.Provider.Xtream;

/// <summary>
/// Builds the standard M3U export URI exposed by many Xtream-compatible panels.
/// The returned URI contains credentials and must therefore only be kept in secure storage
/// or used transiently for an HTTP request. Never log or persist it as normal application data.
/// </summary>
public static class XtreamM3uCompatibility
{
    public static Uri BuildM3uPlusPlaylistUri(XtreamCredentials credentials, string output = "ts")
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Output format is required.", nameof(output));

        var basePath = credentials.ServerBaseUri.AbsolutePath.TrimEnd('/');
        if (basePath.EndsWith("/player_api.php", StringComparison.OrdinalIgnoreCase) ||
            basePath.EndsWith("/get.php", StringComparison.OrdinalIgnoreCase))
        {
            var slash = basePath.LastIndexOf('/');
            basePath = slash <= 0 ? string.Empty : basePath[..slash];
        }

        var path = string.IsNullOrWhiteSpace(basePath)
            ? "/get.php"
            : $"{basePath}/get.php";

        var query = string.Join("&",
            $"username={Uri.EscapeDataString(credentials.Username)}",
            $"password={Uri.EscapeDataString(credentials.Password)}",
            "type=m3u_plus",
            $"output={Uri.EscapeDataString(output.Trim())}");

        return new UriBuilder(credentials.ServerBaseUri)
        {
            Path = path,
            Query = query,
            Fragment = string.Empty
        }.Uri;
    }
}
