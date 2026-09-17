using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

public sealed class MediaSeriesService : IMediaSeriesService
{
    private readonly IReadOnlyDictionary<string, IMediaSeriesProvider> providers;

    public MediaSeriesService(IEnumerable<IMediaSeriesProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var snapshot = providers.ToArray();
        var duplicate = snapshot.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"More than one series provider is registered for '{duplicate.Key}'.");
        this.providers = snapshot.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public Task<MediaSeriesDetails?> GetSeriesAsync(MediaSource source, MediaItem series, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(series);
        if (series.SourceId != source.Id) throw new ArgumentException("Series does not belong to source.", nameof(series));
        if (series.Kind != MediaItemKind.Series) throw new ArgumentException("Selected item is not a series.", nameof(series));
        if (!providers.TryGetValue(source.ProviderId, out var provider)) return Task.FromResult<MediaSeriesDetails?>(null);
        return LoadAsync(provider, source, series.ExternalId, cancellationToken);
    }

    private static async Task<MediaSeriesDetails?> LoadAsync(
        IMediaSeriesProvider provider,
        MediaSource source,
        string externalId,
        CancellationToken cancellationToken)
        => await provider.GetSeriesAsync(source, externalId, cancellationToken).ConfigureAwait(false);
}
