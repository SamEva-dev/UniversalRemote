using System.Globalization;
using System.Xml.Linq;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Epg;

public sealed class XmlTvParser
{
    private const int MaxChannels = 10000;
    private const int MaxPrograms = 100000;

    public EpgDocumentSnapshot Parse(string xml, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (windowEnd <= windowStart) throw new ArgumentException("EPG window end must be after start.", nameof(windowEnd));

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            throw new EpgFormatException("The EPG document is not valid XMLTV.");
        }

        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("tv", StringComparison.OrdinalIgnoreCase))
            throw new EpgFormatException("The EPG document does not contain an XMLTV root element.");

        var channels = root.Elements()
            .Where(static x => x.Name.LocalName.Equals("channel", StringComparison.OrdinalIgnoreCase))
            .Take(MaxChannels)
            .Select(ParseChannel)
            .Where(static x => x is not null)
            .Cast<EpgChannel>()
            .ToArray();

        var programs = new List<EpgProgram>();
        foreach (var element in root.Elements().Where(static x => x.Name.LocalName.Equals("programme", StringComparison.OrdinalIgnoreCase)))
        {
            if (programs.Count >= MaxPrograms) break;
            var program = ParseProgram(element);
            if (program is null) continue;
            if (program.EndsAt <= windowStart || program.StartsAt >= windowEnd) continue;
            programs.Add(program);
        }

        return new EpgDocumentSnapshot(channels, programs, DateTimeOffset.UtcNow);
    }

    private static EpgChannel? ParseChannel(XElement element)
    {
        var id = Normalize((string?)element.Attribute("id"));
        if (id is null) return null;
        var names = element.Elements()
            .Where(static x => x.Name.LocalName.Equals("display-name", StringComparison.OrdinalIgnoreCase))
            .Select(static x => Normalize(x.Value))
            .Where(static x => x is not null)
            .Cast<string>()
            .ToArray();
        if (names.Length == 0) names = [id];
        var icon = ParseHttpUri(element.Elements().FirstOrDefault(static x => x.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))?.Attribute("src")?.Value);
        return new EpgChannel(id, names, icon);
    }

    private static EpgProgram? ParseProgram(XElement element)
    {
        var channelId = Normalize((string?)element.Attribute("channel"));
        var title = Normalize(element.Elements().FirstOrDefault(static x => x.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))?.Value);
        if (channelId is null || title is null) return null;
        if (!TryParseXmlTvTime((string?)element.Attribute("start"), out var startsAt)) return null;
        if (!TryParseXmlTvTime((string?)element.Attribute("stop"), out var endsAt)) return null;
        if (endsAt <= startsAt) return null;

        var description = Normalize(element.Elements().FirstOrDefault(static x => x.Name.LocalName.Equals("desc", StringComparison.OrdinalIgnoreCase))?.Value);
        var category = Normalize(element.Elements().FirstOrDefault(static x => x.Name.LocalName.Equals("category", StringComparison.OrdinalIgnoreCase))?.Value);
        var icon = ParseHttpUri(element.Elements().FirstOrDefault(static x => x.Name.LocalName.Equals("icon", StringComparison.OrdinalIgnoreCase))?.Attribute("src")?.Value);
        return new EpgProgram(channelId, title, startsAt, endsAt, description, category, icon);
    }

    internal static bool TryParseXmlTvTime(string? raw, out DateTimeOffset value)
    {
        value = default;
        var text = Normalize(raw);
        if (text is null) return false;

        // XMLTV commonly uses `yyyyMMddHHmmss +0200`; normalize the offset for DateTimeOffset.
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 && parts[1].Length == 5 && (parts[1][0] == '+' || parts[1][0] == '-'))
            text = $"{parts[0]} {parts[1][..3]}:{parts[1][3..]}";

        string[] formats = [
            "yyyyMMddHHmmss zzz",
            "yyyyMMddHHmm zzz",
            "yyyyMMddHHmmss",
            "yyyyMMddHHmm"
        ];

        return DateTimeOffset.TryParseExact(
            text,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out value);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Uri? ParseHttpUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }
}
