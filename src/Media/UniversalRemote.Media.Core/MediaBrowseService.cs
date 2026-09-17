using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

public sealed class MediaBrowseService : IMediaBrowseService
{
    private readonly IMediaSourceRepository sources;
    private readonly IMediaCatalog catalog;
    private readonly IMediaAccessPolicy access;

    public MediaBrowseService(
        IMediaSourceRepository sources,
        IMediaCatalog catalog,
        IMediaAccessPolicy? access = null)
    {
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.access = access ?? AllowAllMediaAccessPolicy.Instance;
    }

    public async Task<MediaBrowseResult> BrowseAsync(
        IEnumerable<MediaItemKind> kinds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var kindSet = kinds.ToHashSet();
        if (kindSet.Count == 0) return new MediaBrowseResult([], 0, 0);
        if (kindSet.Any(x => !Enum.IsDefined(x)))
            throw new ArgumentException("Browse request contains an unknown media kind.", nameof(kinds));

        var enabledSources = (await sources.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(x => x.IsEnabled)
            .ToArray();

        var tasks = enabledSources.Select(source => LoadSourceAsync(source, kindSet, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var entries = new List<MediaCatalogEntry>();

        foreach (var result in results.Where(x => x.Snapshot is not null))
        {
            foreach (var item in result.Snapshot!.Items)
            {
                var decision = await access.EvaluateAsync(item, cancellationToken).ConfigureAwait(false);
                if (decision.IsAllowed) entries.Add(new MediaCatalogEntry(result.Source, item));
            }
        }

        return new MediaBrowseResult(
            entries.OrderBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            results.Count(x => x.Snapshot is not null),
            results.Count(x => x.Snapshot is null));
    }

    private async Task<SourceLoadResult> LoadSourceAsync(
        MediaSource source,
        IReadOnlySet<MediaItemKind> kinds,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await catalog.GetAsync(source, new MediaCatalogRequest(kinds), cancellationToken).ConfigureAwait(false);
            return new SourceLoadResult(source, snapshot);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return new SourceLoadResult(source, null);
        }
    }

    private sealed record SourceLoadResult(MediaSource Source, MediaCatalogSnapshot? Snapshot);
}
