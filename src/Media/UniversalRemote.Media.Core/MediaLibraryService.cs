using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

public sealed class MediaLibraryService : IMediaLibraryService
{
    private static readonly TimeSpan MinimumContinuePosition = TimeSpan.FromSeconds(5);
    private readonly IMediaFavoriteRepository favorites;
    private readonly IMediaHistoryRepository history;
    private readonly IMediaSourceRepository sources;
    private readonly IMediaCatalog catalog;
    private readonly IMediaAccessPolicy access;

    public MediaLibraryService(
        IMediaFavoriteRepository favorites,
        IMediaHistoryRepository history,
        IMediaSourceRepository sources,
        IMediaCatalog catalog,
        IMediaAccessPolicy? access = null)
    {
        this.favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        this.history = history ?? throw new ArgumentNullException(nameof(history));
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.access = access ?? AllowAllMediaAccessPolicy.Instance;
    }

    public async Task<IReadOnlyList<MediaFavorite>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<MediaFavorite>();
        foreach (var item in await favorites.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var decision = await access.EvaluateAsync(item.Reference, item.Category, item.MinimumAge, cancellationToken).ConfigureAwait(false);
            if (decision.IsAllowed) result.Add(item);
        }
        return result;
    }

    public async Task<IReadOnlyList<MediaHistoryEntry>> GetHistoryAsync(int maxItems = 100, CancellationToken cancellationToken = default)
    {
        if (maxItems is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var result = new List<MediaHistoryEntry>();
        foreach (var item in await history.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var decision = await access.EvaluateAsync(item.Reference, item.Category, item.MinimumAge, cancellationToken).ConfigureAwait(false);
            if (decision.IsAllowed) result.Add(item);
            if (result.Count >= maxItems) break;
        }
        return result;
    }

    public async Task<IReadOnlyList<MediaHistoryEntry>> GetContinueWatchingAsync(int maxItems = 20, CancellationToken cancellationToken = default)
    {
        if (maxItems is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxItems));
        var result = new List<MediaHistoryEntry>();
        foreach (var item in await history.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (item.Reference.Kind is not (MediaItemKind.Movie or MediaItemKind.Series or MediaItemKind.Episode) ||
                item.Completed || item.Position < MinimumContinuePosition)
                continue;

            var decision = await access.EvaluateAsync(item.Reference, item.Category, item.MinimumAge, cancellationToken).ConfigureAwait(false);
            if (decision.IsAllowed) result.Add(item);
            if (result.Count >= maxItems) break;
        }
        return result;
    }

    public async Task<MediaDetails> GetDetailsAsync(MediaSource source, MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(item);
        if (source.Id != item.SourceId) throw new ArgumentException("Media item does not belong to source.", nameof(item));
        await EnsureAllowedAsync(item, cancellationToken).ConfigureAwait(false);
        var reference = MediaReference.From(item);
        var favoriteTask = favorites.FindAsync(reference, cancellationToken);
        var historyTask = history.FindAsync(reference, cancellationToken);
        await Task.WhenAll(favoriteTask, historyTask).ConfigureAwait(false);
        return new MediaDetails(source, item, favoriteTask.Result is not null, historyTask.Result);
    }

    public async Task<bool> ToggleFavoriteAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await EnsureAllowedAsync(item, cancellationToken).ConfigureAwait(false);
        var reference = MediaReference.From(item);
        if (await favorites.FindAsync(reference, cancellationToken).ConfigureAwait(false) is not null)
        {
            await favorites.DeleteAsync(reference, cancellationToken).ConfigureAwait(false);
            return false;
        }

        await favorites.SaveAsync(MediaFavorite.From(item, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<MediaCatalogEntry?> ResolveAsync(MediaReference reference, CancellationToken cancellationToken = default)
    {
        var source = await sources.FindAsync(reference.SourceId, cancellationToken).ConfigureAwait(false);
        if (source is null || !source.IsEnabled) return null;

        try
        {
            var snapshot = await catalog.GetAsync(source, new MediaCatalogRequest([reference.Kind]), cancellationToken).ConfigureAwait(false);
            var item = snapshot.Items.FirstOrDefault(x => string.Equals(x.ExternalId, reference.ExternalId, StringComparison.Ordinal));
            if (item is null) return null;
            var decision = await access.EvaluateAsync(item, cancellationToken).ConfigureAwait(false);
            return decision.IsAllowed ? new MediaCatalogEntry(source, item) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private async Task EnsureAllowedAsync(MediaItem item, CancellationToken cancellationToken)
    {
        var decision = await access.EvaluateAsync(item, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
            throw new UnauthorizedAccessException(decision.UserMessage ?? "Ce contenu est bloqué pour le profil actif.");
    }
}
