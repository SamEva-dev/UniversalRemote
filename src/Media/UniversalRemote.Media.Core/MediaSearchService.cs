using System.Globalization;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

public sealed class MediaSearchService : IMediaSearchService
{
    private readonly IMediaBrowseService browse;

    public MediaSearchService(IMediaBrowseService browse)
        => this.browse = browse ?? throw new ArgumentNullException(nameof(browse));

    public async Task<MediaBrowseResult> SearchAsync(
        string query,
        IEnumerable<MediaItemKind>? kinds = null,
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maxResults));

        var requestedKinds = (kinds ?? Enum.GetValues<MediaItemKind>()).ToArray();
        var result = await browse.BrowseAsync(requestedKinds, cancellationToken).ConfigureAwait(false);
        var needle = query.Trim();

        var filtered = result.Items
            .Where(x => Contains(x.Item.Title, needle) || Contains(x.Item.Category, needle) || Contains(x.Source.DisplayName, needle))
            .OrderByDescending(x => StartsWith(x.Item.Title, needle))
            .ThenBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxResults)
            .ToArray();

        return result with { Items = filtered };
    }

    private static bool Contains(string? value, string query)
        => !string.IsNullOrWhiteSpace(value) && CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            value,
            query,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private static bool StartsWith(string value, string query)
        => CultureInfo.CurrentCulture.CompareInfo.IsPrefix(
            value,
            query,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
}
