using System.Text.RegularExpressions;

namespace UniversalRemote.Media.Provider.M3U;

/// <summary>
/// Tolerant parser for IPTV-style extended M3U catalogues. HLS media/master manifests are deliberately rejected:
/// they are playback resources, not channel catalogues.
/// </summary>
public sealed partial class M3uPlaylistParser
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    public M3uPlaylist Parse(string content, Uri? baseUri = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (baseUri is not null && !baseUri.IsAbsoluteUri)
            throw new ArgumentException("Base URI must be absolute when specified.", nameof(baseUri));

        var lines = NormalizeLines(content);
        if (LooksLikeHlsManifest(lines))
            throw new M3uPlaylistFormatException("The source is an HLS playback manifest, not an IPTV M3U catalogue.");

        var warnings = new List<M3uParseWarning>();
        var entries = new List<M3uPlaylistEntry>();
        Uri? epgUri = null;
        PendingEntry? pending = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                var headerAttributes = ParseAttributes(line["#EXTM3U".Length..]);
                var rawEpg = FirstNonEmpty(headerAttributes, "url-tvg", "x-tvg-url", "tvg-url");
                if (TryResolveUri(rawEpg, baseUri, out var resolvedEpg))
                    epgUri = resolvedEpg;
                continue;
            }

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                if (pending is not null)
                    warnings.Add(new M3uParseWarning(pending.LineNumber, "missing-stream-uri"));

                pending = ParseExtInf(line, lineNumber);
                continue;
            }

            if (line[0] == '#')
                continue;

            if (!TryResolveUri(line, baseUri, out var streamUri) || streamUri is null || !IsSupportedStreamUri(streamUri))
            {
                warnings.Add(new M3uParseWarning(lineNumber, "invalid-stream-uri"));
                pending = null;
                continue;
            }

            if (pending is null)
            {
                entries.Add(new M3uPlaylistEntry
                {
                    Title = InferTitle(streamUri),
                    StreamUri = streamUri
                });
                continue;
            }

            entries.Add(new M3uPlaylistEntry
            {
                Title = FirstNonBlank(pending.Title, pending.TvgName, "Channel"),
                TvgId = pending.TvgId,
                TvgName = pending.TvgName,
                GroupTitle = pending.GroupTitle,
                LogoUri = TryResolveUri(pending.Logo, baseUri, out var logoUri) ? logoUri : null,
                StreamUri = streamUri
            });
            pending = null;
        }

        if (pending is not null)
            warnings.Add(new M3uParseWarning(pending.LineNumber, "missing-stream-uri"));

        return new M3uPlaylist
        {
            Entries = entries.AsReadOnly(),
            Warnings = warnings.AsReadOnly(),
            EpgUri = epgUri
        };
    }

    private static List<string> NormalizeLines(string content)
        => content
            .TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

    private static bool LooksLikeHlsManifest(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXT-X-TARGETDURATION", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("#EXT-X-MEDIA-SEQUENCE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static PendingEntry ParseExtInf(string line, int lineNumber)
    {
        var colon = line.IndexOf(':');
        var payload = colon >= 0 ? line[(colon + 1)..] : string.Empty;
        var comma = FindMetadataSeparator(payload);
        var metadata = comma >= 0 ? payload[..comma] : payload;
        var title = comma >= 0 ? payload[(comma + 1)..].Trim() : string.Empty;
        var attributes = ParseAttributes(metadata);

        return new PendingEntry(
            lineNumber,
            NormalizeOptional(title),
            FirstNonEmpty(attributes, "tvg-id"),
            FirstNonEmpty(attributes, "tvg-name"),
            FirstNonEmpty(attributes, "group-title"),
            FirstNonEmpty(attributes, "tvg-logo", "logo"));
    }

    private static int FindMetadataSeparator(string payload)
    {
        var quoted = false;
        for (var i = 0; i < payload.Length; i++)
        {
            if (payload[i] == '"') quoted = !quoted;
            else if (payload[i] == ',' && !quoted) return i;
        }
        return -1;
    }

    private static Dictionary<string, string> ParseAttributes(string text)
    {
        var values = new Dictionary<string, string>(KeyComparer);
        foreach (Match match in AttributeRegex().Matches(text))
        {
            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["plain"].Value;

            if (key.Length > 0 && value.Length > 0)
                values[key] = value.Trim();
        }
        return values;
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> attributes, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static string FirstNonBlank(params string?[] values)
        => values.First(static x => !string.IsNullOrWhiteSpace(x))!.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryResolveUri(string? raw, Uri? baseUri, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var value = raw.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            uri = absolute;
            return true;
        }

        if (baseUri is not null && Uri.TryCreate(baseUri, value, out var relative))
        {
            uri = relative;
            return true;
        }

        return false;
    }

    private static bool IsSupportedStreamUri(Uri uri)
        => uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
           uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
           uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) ||
           uri.Scheme.Equals("rtmp", StringComparison.OrdinalIgnoreCase) ||
           uri.Scheme.Equals("udp", StringComparison.OrdinalIgnoreCase);

    private static string InferTitle(Uri streamUri)
    {
        var segment = streamUri.Segments.LastOrDefault()?.Trim('/');
        if (!string.IsNullOrWhiteSpace(segment))
        {
            var decoded = Uri.UnescapeDataString(segment);
            var dot = decoded.LastIndexOf('.');
            return dot > 0 ? decoded[..dot] : decoded;
        }

        return streamUri.Host.Length > 0 ? streamUri.Host : "Channel";
    }

    [GeneratedRegex("(?<key>[A-Za-z0-9_-]+)\\s*=\\s*(?:\"(?<quoted>[^\"]*)\"|(?<plain>[^\\s,]+))", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeRegex();

    private sealed record PendingEntry(
        int LineNumber,
        string? Title,
        string? TvgId,
        string? TvgName,
        string? GroupTitle,
        string? Logo);
}
