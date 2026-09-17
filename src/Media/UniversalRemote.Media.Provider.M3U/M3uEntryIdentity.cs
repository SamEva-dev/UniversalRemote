using System.Security.Cryptography;
using System.Text;

namespace UniversalRemote.Media.Provider.M3U;

internal sealed record M3uEntryIdentity(string ExternalId, M3uPlaylistEntry Entry);

/// <summary>Keeps catalogue IDs and playback lookup IDs deterministic and identical.</summary>
internal static class M3uEntryIdentityFactory
{
    public static IReadOnlyList<M3uEntryIdentity> Create(IReadOnlyList<M3uPlaylistEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var results = new List<M3uEntryIdentity>(entries.Count);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var externalId = CreateExternalId(entry);
            if (!usedIds.Add(externalId))
            {
                var collisionIdentity = $"{entry.StreamUri.GetLeftPart(UriPartial.Path)}|{entry.Title}";
                var suffix = Hash(collisionIdentity)[..8];
                externalId = $"{externalId}:{suffix}";
                var collision = 2;
                while (!usedIds.Add(externalId))
                    externalId = $"{externalId}:{collision++}";
            }

            results.Add(new M3uEntryIdentity(externalId, entry));
        }

        return results;
    }

    private static string CreateExternalId(M3uPlaylistEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.TvgId))
            return $"m3u:tvg:{NormalizeIdentifier(entry.TvgId)}";

        var pathWithoutQuery = entry.StreamUri.GetLeftPart(UriPartial.Path);
        var identity = $"{entry.Title}|{entry.GroupTitle}|{pathWithoutQuery}";
        return $"m3u:{Hash(identity)[..24]}";
    }

    private static string NormalizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? char.ToLowerInvariant(c) : '-');

        var normalized = builder.ToString().Trim('-');
        if (normalized.Length == 0)
            return Hash(value)[..24];
        return normalized.Length <= 180 ? normalized : $"id-{Hash(normalized)[..24]}";
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
