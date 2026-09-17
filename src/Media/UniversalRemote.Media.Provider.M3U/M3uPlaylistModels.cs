namespace UniversalRemote.Media.Provider.M3U;

/// <summary>One sanitized warning produced while parsing a playlist.</summary>
public sealed record M3uParseWarning(int LineNumber, string Code);

/// <summary>Parsed M3U catalogue entry. StreamUri is intentionally confined to the concrete provider package.</summary>
public sealed record M3uPlaylistEntry
{
    public required string Title { get; init; }
    public string? TvgId { get; init; }
    public string? TvgName { get; init; }
    public string? GroupTitle { get; init; }
    public Uri? LogoUri { get; init; }
    public required Uri StreamUri { get; init; }
}

/// <summary>Parsed IPTV M3U catalogue plus non-sensitive diagnostics.</summary>
public sealed record M3uPlaylist
{
    public required IReadOnlyList<M3uPlaylistEntry> Entries { get; init; }
    public required IReadOnlyList<M3uParseWarning> Warnings { get; init; }
    public Uri? EpgUri { get; init; }
}

public sealed class M3uPlaylistFormatException : FormatException
{
    public M3uPlaylistFormatException(string message) : base(message) { }
}

public sealed class M3uSourceConfigurationException : InvalidOperationException
{
    public M3uSourceConfigurationException(string message) : base(message) { }
}

public sealed class M3uPlaylistLoadException : InvalidOperationException
{
    public System.Net.HttpStatusCode? StatusCode { get; }

    public M3uPlaylistLoadException(string message, System.Net.HttpStatusCode? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
