using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

/// <summary>Routes normalized catalogue requests to the provider selected by MediaSource.ProviderId.</summary>
public sealed class MediaCatalog : IMediaCatalog
{
    private readonly MediaProviderResolver resolver;

    public MediaCatalog(MediaProviderResolver resolver)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public Task<MediaCatalogSnapshot> GetAsync(
        MediaSource source,
        MediaCatalogRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!source.IsEnabled)
            throw new InvalidOperationException("The media source is disabled.");

        var provider = resolver.Resolve(source.ProviderId);
        return provider.GetCatalogAsync(source, request ?? new MediaCatalogRequest(), cancellationToken);
    }
}
