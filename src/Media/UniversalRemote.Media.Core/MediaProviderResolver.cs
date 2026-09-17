using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

public sealed class MediaProviderUnavailableException : InvalidOperationException
{
    public string ProviderId { get; }

    public MediaProviderUnavailableException(string providerId)
        : base($"Media provider '{providerId}' is not available.")
    {
        ProviderId = providerId;
    }
}

/// <summary>Resolves a media source to exactly one provider using a stable provider identifier.</summary>
public sealed class MediaProviderResolver
{
    private readonly IReadOnlyDictionary<string, IMediaProvider> providers;

    public MediaProviderResolver(IEnumerable<IMediaProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var snapshot = providers.ToArray();
        if (snapshot.Any(x => x is null)) throw new ArgumentException("Media provider collection must not contain null values.", nameof(providers));

        var invalid = snapshot.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Id));
        if (invalid is not null) throw new InvalidOperationException("A registered media provider has an empty ID.");

        var duplicate = snapshot
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Multiple media providers are registered with ID '{duplicate.Key}'.");

        this.providers = snapshot.ToDictionary(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public bool TryResolve(string providerId, out IMediaProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            provider = null;
            return false;
        }

        return providers.TryGetValue(providerId.Trim(), out provider);
    }

    public IMediaProvider Resolve(string providerId)
    {
        if (!TryResolve(providerId, out var provider) || provider is null)
            throw new MediaProviderUnavailableException(providerId?.Trim() ?? string.Empty);
        return provider;
    }
}
