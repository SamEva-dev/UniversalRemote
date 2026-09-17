using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Epg;

public sealed class EpgGuideService
    : IEpgGuideService
{
    private static readonly TimeSpan CacheFreshness = TimeSpan.FromHours(4);
    private readonly IMediaCatalog mediaCatalog;
    private readonly EpgProviderResolver providerResolver;
    private readonly EpgChannelMatcher matcher;
    private readonly IEpgCache cache;
    private readonly IMediaAccessPolicy? access;

    public EpgGuideService(
        IMediaCatalog mediaCatalog,
        EpgProviderResolver providerResolver,
        EpgChannelMatcher matcher,
        IEpgCache cache,
        IMediaAccessPolicy? access = null)
    {
        this.mediaCatalog = mediaCatalog ?? throw new ArgumentNullException(nameof(mediaCatalog));
        this.providerResolver = providerResolver ?? throw new ArgumentNullException(nameof(providerResolver));
        this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.access = access;
    }

    public async Task<EpgGuideSnapshot> GetGuideAsync(
        MediaSource mediaSource,
        EpgSource epgSource,
        EpgGuideRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaSource);
        ArgumentNullException.ThrowIfNull(epgSource);
        ArgumentNullException.ThrowIfNull(request);
        if (epgSource.MediaSourceId != mediaSource.Id)
            throw new ArgumentException("The EPG source is attached to another media source.", nameof(epgSource));

        if (!request.ForceRefresh)
        {
            var cached = await cache.GetAsync(mediaSource.Id, epgSource.Id, request.WindowStart, request.WindowEnd, cancellationToken).ConfigureAwait(false);
            if (cached is not null && DateTimeOffset.UtcNow - cached.RefreshedAt <= CacheFreshness)
                return await FilterForActiveProfileAsync(cached, cancellationToken).ConfigureAwait(false);
        }

        var provider = providerResolver.Resolve(epgSource.ProviderId);
        var catalogTask = mediaCatalog.GetAsync(mediaSource, new MediaCatalogRequest([MediaItemKind.LiveChannel]), cancellationToken);
        var epgTask = provider.GetAsync(epgSource, request, cancellationToken);
        await Task.WhenAll(catalogTask, epgTask).ConfigureAwait(false);

        var catalog = await catalogTask.ConfigureAwait(false);
        var epg = await epgTask.ConfigureAwait(false);
        var programsByChannel = epg.Programs
            .Where(x => x.EndsAt > request.WindowStart && x.StartsAt < request.WindowEnd)
            .GroupBy(x => x.ChannelGuideId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(p => p.StartsAt).ToArray(), StringComparer.OrdinalIgnoreCase);

        var rows = catalog.Items
            .Where(x => x.Kind == MediaItemKind.LiveChannel)
            .Select(channel =>
            {
                var guideId = matcher.Match(channel, epg.Channels);
                var programs = guideId is not null && programsByChannel.TryGetValue(guideId, out var list)
                    ? list
                    : Array.Empty<EpgProgram>();
                return new EpgGuideChannel(channel, guideId, programs);
            })
            .ToArray();

        var rawSnapshot = new EpgGuideSnapshot(mediaSource.Id, epgSource.Id, request.WindowStart, request.WindowEnd, rows, DateTimeOffset.UtcNow);
        await cache.SetAsync(rawSnapshot, cancellationToken).ConfigureAwait(false);
        return await FilterForActiveProfileAsync(rawSnapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EpgGuideSnapshot> FilterForActiveProfileAsync(EpgGuideSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (access is null) return snapshot;
        var allowed = new List<EpgGuideChannel>();
        foreach (var channel in snapshot.Channels)
        {
            if ((await access.EvaluateAsync(channel.Channel, cancellationToken).ConfigureAwait(false)).IsAllowed)
                allowed.Add(channel);
        }
        return new EpgGuideSnapshot(
            snapshot.MediaSourceId,
            snapshot.EpgSourceId,
            snapshot.WindowStart,
            snapshot.WindowEnd,
            allowed,
            snapshot.RefreshedAt);
    }
}
